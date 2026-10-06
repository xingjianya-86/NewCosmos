#!/usr/bin/env python3
"""
彩票预测算法集合
支持双色球(SSQ)和大乐透(DLT)
包含：随机机选、基础统计、量化算法、机器学习
"""

import json
import sys
import os
import argparse
import random
import math
from datetime import datetime
from typing import List, Dict, Tuple, Optional
from collections import Counter
from pathlib import Path

try:
    import numpy as np
    HAS_NUMPY = True
except ImportError:
    HAS_NUMPY = False
    print("[WARN] numpy未安装，部分功能将受限", file=sys.stderr)


def _json_safe(obj):
    """递归将 numpy 标量/数组转为 Python 原生类型，保证 json 可序列化（stdout 仅输出 JSON）。"""
    if not HAS_NUMPY:
        return obj
    if isinstance(obj, dict):
        return {_json_safe(k): _json_safe(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [_json_safe(v) for v in obj]
    if isinstance(obj, np.ndarray):
        return _json_safe(obj.tolist())
    if isinstance(obj, np.integer):
        return int(obj)
    if isinstance(obj, np.floating):
        return float(obj)
    if isinstance(obj, np.bool_):
        return bool(obj)
    return obj


def _popular_penalty(nums: List[int]) -> float:
    """热门度罚分（越低越“冷门”）：生日号(1-31)、连号、全小/全大越多分越高。
    仅用于期望值优化（中奖时分摊更少），不影响中奖概率。"""
    s = sorted(nums)
    penalty = float(sum(1 for n in s if n <= 31))      # 生日号偏好
    penalty += 3.0 * sum(1 for a, b in zip(s, s[1:]) if b == a + 1)  # 连号
    if s and s[-1] <= 16:
        penalty += 5.0                                  # 全小
    if s and s[0] >= 20:
        penalty += 5.0                                  # 全大
    return penalty


def _fill_unique(picked: List[int], pool_range, need: int) -> List[int]:
    """从 pool_range 中补足不重复号码至 need 个"""
    picked = list(dict.fromkeys(int(x) for x in picked))
    if len(picked) >= need:
        return sorted(picked[:need])
    remaining = [n for n in pool_range if n not in picked]
    random.shuffle(remaining)
    picked.extend(remaining[:need - len(picked)])
    return sorted(picked)


def _diversify_results(results: List[Dict], config: Dict, avoid_popular: bool = True) -> List[Dict]:
    """保证同一批多注互异 + 蓝球/后区分散；avoid_popular 时向冷门组合靠拢。
    仅调整选号分布，不改变单注中奖概率。"""
    red_count = config["red_count"]
    red_max = config["red_max"]
    blue_count = config["blue_count"]
    blue_max = config["blue_max"]
    red_range = list(range(1, red_max + 1))
    blue_range = list(range(1, blue_max + 1))

    used_red = set()
    used_blue = set()

    for r in results:
        # ── 红球：规范化数量并去重 ──
        reds = _fill_unique([int(x) for x in r.get("red_numbers", [])], red_range, red_count)

        # ── 与已用组合重复时，替换一枚号码（优先冷门）──
        guard = 0
        while tuple(reds) in used_red and guard < 50:
            guard += 1
            idx = random.randrange(red_count)
            base = [x for i, x in enumerate(reds) if i != idx]
            candidates = [n for n in red_range if n not in reds]
            if not candidates:
                break
            if avoid_popular:
                candidates.sort(key=lambda n: _popular_penalty(base + [n]))
            else:
                random.shuffle(candidates)
            reds = sorted(base + [candidates[0]])
        used_red.add(tuple(reds))
        r["red_numbers"] = reds

        # ── 蓝球/后区：优先使用尚未出现的号码（分散）──
        blues = _fill_unique([int(x) for x in r.get("blue_numbers", [])], blue_range, blue_count)
        free = [n for n in blue_range if n not in used_blue]
        if blue_count == 1 and free:
            blues = [random.choice(free)]
        elif blue_count > 1:
            chosen = [n for n in blues if n not in used_blue]
            need = blue_count - len(chosen)
            if need > 0:
                extra = [n for n in free if n not in chosen]
                random.shuffle(extra)
                chosen.extend(extra[:need])
            if len(chosen) < blue_count:
                chosen.extend([n for n in blues if n not in chosen][:blue_count - len(chosen)])
            blues = sorted(chosen[:blue_count])
        used_blue.update(blues)
        r["blue_numbers"] = blues

    return results


class LotteryPredictor:
    """彩票预测器基类"""

    def __init__(self, lottery_type: str = "ssq"):
        self.lottery_type = lottery_type
        self.config = self._get_config()

    def _get_config(self) -> Dict:
        """获取彩种配置"""
        if self.lottery_type == "ssq":
            return {
                "name": "双色球",
                "red_count": 6,
                "blue_count": 1,
                "red_max": 33,
                "blue_max": 16,
                "red_range": range(1, 34),
                "blue_range": range(1, 17)
            }
        elif self.lottery_type == "dlt":
            return {
                "name": "大乐透",
                "red_count": 5,
                "blue_count": 2,
                "red_max": 35,
                "blue_max": 12,
                "red_range": range(1, 36),
                "blue_range": range(1, 13)
            }
        else:
            raise ValueError(f"不支持的彩种: {self.lottery_type}")

    def load_history(self, data_dir: str = "./data") -> List[Dict]:
        """加载历史数据"""
        filename = os.path.join(data_dir, f"{self.lottery_type}_history.json")
        if not os.path.exists(filename):
            print(f"[WARN] 历史数据文件不存在: {filename}", file=sys.stderr)
            return []
        with open(filename, 'r', encoding='utf-8') as f:
            return json.load(f)

    def _resample(self, generate_one, min_confidence: float, max_retries: int) -> Dict:
        """单注重采样：置信度未超过门槛则重新生成，保留历史最高分结果。

        min_confidence <= 0 表示禁用门槛（用于内部投票等场景，首轮即返回）。
        """
        best = generate_one()
        if min_confidence <= 0:
            return best
        attempts = 0
        while best.get("confidence", 0) <= min_confidence and attempts < max_retries:
            candidate = generate_one()
            if candidate.get("confidence", 0) > best.get("confidence", 0):
                best = candidate
            attempts += 1
        return best

    @staticmethod
    def _ratio_confidence(picked: List[int], scores: Dict[int, float],
                          base: float = 60.0, span: float = 35.0,
                          cap: float = 95.0) -> float:
        """按选中号码得分占最高分比例映射置信度（base ~ base+span）"""
        if not scores:
            return base
        max_score = max(scores.values())
        if max_score <= 0:
            return base
        picked_values = [scores.get(num, 0.0) for num in picked]
        avg_score = sum(picked_values) / max(1, len(picked_values))
        ratio = max(0.0, min(1.0, avg_score / max_score))
        return min(cap, base + ratio * span)

    def load_history_from_db(self, db_host: str, db_port: int, db_name: str,
                              db_user: str, db_password: str) -> List[Dict]:
        """从 PostgreSQL 加载历史数据"""
        try:
            import psycopg2
        except ImportError:
            print("[WARN] psycopg2 未安装，无法连接数据库", file=sys.stderr)
            return []

        try:
            conn = psycopg2.connect(
                host=db_host, port=db_port, dbname=db_name,
                user=db_user, password=db_password
            )
            cur = conn.cursor()
            cur.execute("""
                SELECT red_numbers_json, blue_numbers_json
                FROM nc_lottery_draws
                WHERE lottery_type = %s
                ORDER BY draw_date DESC
            """, (self.lottery_type.upper(),))

            history = []
            for row in cur.fetchall():
                reds = json.loads(row[0])
                blues = json.loads(row[1])
                history.append({"red_numbers": reds, "blue_numbers": blues})

            cur.close()
            conn.close()
            print(f"  从数据库加载 {len(history)} 期历史数据", file=sys.stderr)
            return history
        except Exception as e:
            print(f"[WARN] 数据库连接失败: {e}", file=sys.stderr)
            return []


class RandomPicker(LotteryPredictor):
    """随机机选"""

    def predict(self, count: int = 1) -> List[Dict]:
        """随机生成号码"""
        results = []
        for _ in range(count):
            reds = sorted(random.sample(self.config["red_range"], self.config["red_count"]))
            blues = sorted(random.sample(self.config["blue_range"], self.config["blue_count"]))
            results.append({
                "red_numbers": reds,
                "blue_numbers": blues,
                "algorithm": "random",
                "confidence": 0
            })
        return results


class MLPredictor(LotteryPredictor):
    """机器学习预测（TensorFlow/LSTM）"""

    def __init__(self, lottery_type: str = "ssq"):
        super().__init__(lottery_type)
        self.model = None
        self.scaler = None

    def load_model(self, model_dir: str = "./models"):
        """加载预训练模型"""
        try:
            import tensorflow as tf
            model_path = os.path.join(model_dir, f"{self.lottery_type}_model.keras")
            if os.path.exists(model_path):
                self.model = tf.keras.models.load_model(model_path)
                print(f"模型加载成功: {model_path}", file=sys.stderr)
                return True
        except ImportError:
            print("[WARN] TensorFlow未安装，ML预测不可用", file=sys.stderr)
        except Exception as e:
            print(f"[ERROR] 模型加载失败: {e}", file=sys.stderr)
        return False

    def score_numbers(self, history: List[Dict]):
        """返回红/蓝每号分数（不选号）。history 需按时间顺序传入最近 30 期。"""
        if self.model is None or not history:
            return None
        try:
            features = self._prepare_features(history)
            if features is None:
                return None
            pred = self.model.predict(features, verbose=0)
            red = {i + 1: float(pred[0][i]) for i in range(self.config["red_max"])}
            blue = {i + 1: float(pred[0][self.config["red_max"] + i])
                    for i in range(self.config["blue_max"])}
            return red, blue
        except Exception as e:
            print(f"[ERROR] ML 分数计算失败: {e}", file=sys.stderr)
            return None

    def predict(self, count: int = 1, history: List[Dict] = None,
                min_confidence: float = 80.0, max_retries: int = 5) -> List[Dict]:
        """ML预测（history 需按时间顺序传入；取最近 30 期作为输入）"""
        if self.model is None or not history:
            return RandomPicker(self.lottery_type).predict(count)

        try:
            import numpy as np

            features = self._prepare_features(history)
            if features is None:
                return RandomPicker(self.lottery_type).predict(count)

            pred = self.model.predict(features, verbose=0)

            red_scores = {i + 1: float(pred[0][i]) for i in range(self.config["red_max"])}
            blue_scores = {i + 1: float(pred[0][self.config["red_max"] + i])
                           for i in range(self.config["blue_max"])}

            def generate_one():
                reds = self._decode_reds(pred[0][:self.config["red_max"]])
                blues = self._decode_blues(
                    pred[0][self.config["red_max"]:self.config["red_max"] + self.config["blue_max"]])
                red_conf = self._ratio_confidence(reds, red_scores)
                blue_conf = self._ratio_confidence(blues, blue_scores)
                confidence = round(red_conf * 0.7 + blue_conf * 0.3, 1)
                return {
                    "red_numbers": sorted(reds),
                    "blue_numbers": sorted(blues),
                    "algorithm": "ml",
                    "confidence": confidence
                }

            return [self._resample(generate_one, min_confidence, max_retries)
                    for _ in range(count)]

        except Exception as e:
            print(f"[ERROR] ML预测失败: {e}", file=sys.stderr)
            return RandomPicker(self.lottery_type).predict(count)

    def _prepare_features(self, history: List[Dict]) -> Optional['np.ndarray']:
        """取传入序列的前 30 期（调用方应按时间顺序传入最近 30 期）"""
        if not HAS_NUMPY:
            return None
        recent = history[:30]
        if len(recent) < 30:
            return None
        features = []
        for draw in recent:
            reds = draw.get("red_numbers", [])
            blues = draw.get("blue_numbers", [])
            red_vec = [0] * self.config["red_max"]
            for num in reds:
                if 1 <= num <= self.config["red_max"]:
                    red_vec[num - 1] = 1
            blue_vec = [0] * self.config["blue_max"]
            for num in blues:
                if 1 <= num <= self.config["blue_max"]:
                    blue_vec[num - 1] = 1
            features.append(red_vec + blue_vec)
        return np.array([features])

    def _decode_reds(self, pred: 'np.ndarray') -> List[int]:
        indices = np.argsort(pred)[-self.config["red_count"]:]
        return [int(i) + 1 for i in indices if 0 <= i < self.config["red_max"]]

    def _decode_blues(self, pred: 'np.ndarray') -> List[int]:
        indices = np.argsort(pred)[-self.config["blue_count"]:]
        return [int(i) + 1 for i in indices if 0 <= i < self.config["blue_max"]]


def main():
    parser = argparse.ArgumentParser(description="彩票预测工具")
    parser.add_argument("--type", choices=["ssq", "dlt"], default="ssq",
                        help="彩种类型")
    parser.add_argument("--algorithm", choices=["random", "ml"],
                        default="random", help="预测算法")
    parser.add_argument("--count", type=int, default=5, help="生成注数")
    parser.add_argument("--period", type=int, default=100, help="统计期数范围")
    parser.add_argument("--min-confidence", type=float, default=80.0,
                        help="最低置信度门槛（百分比），不足则重新计算；机选不受限")
    parser.add_argument("--max-retries", type=int, default=5,
                        help="单注未达标时最大重采样次数，耗尽后保留最高分结果")
    parser.add_argument("--avoid-popular", action=argparse.BooleanOptionalAction, default=True,
                        help="避开热门号组合（期望值优化，不影响中奖概率；--no-avoid-popular 关闭）")
    parser.add_argument("--data-dir", default="./data", help="数据目录")
    parser.add_argument("--output", default=None, help="输出文件路径")
    # 数据库连接参数
    parser.add_argument("--db-host", default=None, help="数据库主机")
    parser.add_argument("--db-port", type=int, default=None, help="数据库端口")
    parser.add_argument("--db-name", default=None, help="数据库名称")
    parser.add_argument("--db-user", default=None, help="数据库用户")
    parser.add_argument("--db-password", default=None, help="数据库密码")

    args = parser.parse_args()

    # 数据库密码优先取参数，其次环境变量（避免明文出现在命令行）
    db_password = args.db_password or os.environ.get("PGPASSWORD") or os.environ.get("NEWCOSMOS_DB_PASSWORD")

    # 加载历史数据
    predictor = LotteryPredictor(args.type)
    
    # 优先从数据库加载，其次从文件加载
    history = []
    if args.db_host and args.db_name and args.db_user and db_password:
        history = predictor.load_history_from_db(
            args.db_host, args.db_port or 5432, args.db_name,
            args.db_user, db_password
        )
    
    if not history:
        history = predictor.load_history(args.data_dir)

    if not history:
        print("[WARN] 无历史数据，将使用随机预测", file=sys.stderr)

    # 选择算法（仅保留 随机 / 机器学习 作内部组件；其余算法已移除）
    if args.algorithm == "random":
        picker = RandomPicker(args.type)
        results = picker.predict(args.count)
    elif args.algorithm == "ml":
        picker = MLPredictor(args.type)
        picker.load_model()
        # history 最新在前 → 取最近 30 期并反转为时间顺序（修复原版“取最旧”BUG）
        ml_history = list(reversed(history[:30]))
        results = picker.predict(args.count, ml_history,
                                 min_confidence=args.min_confidence,
                                 max_retries=args.max_retries)
    else:
        print(f"[ERROR] 不支持的算法: {args.algorithm}", file=sys.stderr)
        sys.exit(1)

    # 多注互异 + 蓝球/后区分散 + 冷门化（期望值优化，不改变单注中奖概率）
    results = _diversify_results(results, predictor.config, avoid_popular=args.avoid_popular)

    # 是否全部达标（供 C# 端判断是否需要兜底重试；空结果视为未达标）
    threshold_met = bool(results) and all(
        p.get("confidence", 0) > args.min_confidence for p in results
    )

    # 格式化输出
    output = {
        "lottery_type": args.type,
        "algorithm": args.algorithm,
        "count": len(results),
        "min_confidence": args.min_confidence,
        "threshold_met": threshold_met,
        "predictions": results,
        "timestamp": datetime.now().isoformat()
    }

    # 归一为 JSON 安全类型（消除 numpy 标量/数组），保证 C# 端可解析
    output = _json_safe(output)

    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(output, f, ensure_ascii=False, indent=2)
        print(f"预测结果已保存到: {args.output}")
    else:
        print(json.dumps(output, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
