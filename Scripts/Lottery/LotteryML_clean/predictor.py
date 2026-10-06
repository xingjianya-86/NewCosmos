# -*- coding: utf-8 -*-
r"""
LotteryML_clean —— 独立实现（clean-room）的逐位概率预测器

方法学参考公开的统计建模思路（逐位分类 + 梯度提升 + 概率校准 + 温度采样 + 均匀混合 + 和值过滤 +
多轮共识），代码为本项目自行编写，不包含任何第三方受限代码。

重要：彩票为独立同分布随机事件，本模型无法提高中奖概率，仅作为“规范化的选号策略/娱乐”。

用法：
    $env:PGPASSWORD='...'
    python LotteryML_clean\predictor.py --type ssq --count 5 --force-retrain
    python LotteryML_clean\predictor.py --type dlt --count 5
    python LotteryML_clean\predictor.py --type ssq --accuracy --db-host ...
"""
import os
import sys
import json
import math
import argparse
from datetime import datetime

import numpy as np
import joblib
from sklearn.ensemble import HistGradientBoostingClassifier, RandomForestClassifier, VotingClassifier
from sklearn.calibration import CalibratedClassifierCV
from sklearn.linear_model import LogisticRegression

try:
    import psycopg2
except ImportError:
    psycopg2 = None

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.join(BASE_DIR, "models")

CONFIGS = {
    "ssq": {"red_count": 6, "blue_count": 1, "red_max": 33, "blue_max": 16},
    "dlt": {"red_count": 5, "blue_count": 2, "red_max": 35, "blue_max": 12},
}

# 超参
LAG_WINDOW = 5            # 作为特征的最近期数
FREQ_WINDOW = 100         # 频次/遗漏统计窗口
ENTROPY_WINDOWS = (10, 25, 50)
FREQ_DECAY = 0.97
PREDICTION_RUNS = 10      # 每期生成的轮次（共识投票用）
UNIFORM_MIX = 0.3         # 与均匀分布混合权重（防模式坍缩）
SUM_MEAN_ALLOWANCE = 0.05
SUM_MODE_ALLOWANCE = 0.15
MAX_RETRIES = 20
TEMPERATURES = {0: 0.8, 1: 1.2, 2: 1.6}  # 按熵 regime 的温度


# ─────────────────────────── 数据加载 ───────────────────────────

def load_history_from_db(lottery_type, db_host, db_port, db_name, db_user, db_password):
    if psycopg2 is None:
        print("[ERROR] psycopg2 未安装", file=sys.stderr)
        return []
    try:
        conn = psycopg2.connect(host=db_host, port=db_port, dbname=db_name, user=db_user, password=db_password)
        cur = conn.cursor()
        cur.execute("SELECT red_numbers_json, blue_numbers_json, draw_date FROM nc_lottery_draws "
                    "WHERE lottery_type=%s ORDER BY draw_date DESC", (lottery_type.upper(),))
        rows = cur.fetchall()
        cur.close()
        conn.close()
        return [{"reds": json.loads(r[0]), "blues": json.loads(r[1]),
                 "date": str(r[2])} for r in rows]
    except Exception as e:
        print(f"[ERROR] 数据库加载失败: {e}", file=sys.stderr)
        return []


# ─────────────────────────── 特征工程（泄漏防护：仅用更旧期）───────────────────────────

def _entropy(counter, total):
    if total <= 0:
        return 0.0
    h = 0.0
    for c in counter.values():
        p = c / total
        if p > 0:
            h -= p * math.log(p, 2)
    return h


def build_features(hist, ball, cfg, lag_window=LAG_WINDOW, freq_window=FREQ_WINDOW):
    """hist: 更旧期在前（list of dict，index0 为最近）。返回 np.ndarray 特征向量。"""
    max_n = cfg["red_max"] if ball == "red" else cfg["blue_max"]
    key = "reds" if ball == "red" else "blues"

    recent = hist[:lag_window]
    # 1) lag one-hot
    feat = []
    for d in recent:
        vec = [0.0] * max_n
        for n in d[key]:
            if 1 <= n <= max_n:
                vec[n - 1] = 1.0
        feat.extend(vec)
    if len(recent) < lag_window:
        feat.extend([0.0] * max_n * (lag_window - len(recent)))

    # 2) recency 加权频次
    freq = [0.0] * max_n
    for i, d in enumerate(hist[:freq_window]):
        w = FREQ_DECAY ** i
        for n in d[key]:
            if 1 <= n <= max_n:
                freq[n - 1] += w
    mx = max(freq) if freq else 0.0
    feat.extend([f / mx if mx > 0 else 0.0 for f in freq])

    # 3) 当前遗漏（从最近向旧扫描）
    missing = [float(freq_window)] * max_n
    remaining = set(range(1, max_n + 1))
    for off, d in enumerate(hist[:freq_window]):
        for n in d[key]:
            if n in remaining:
                missing[n - 1] = float(off)
                remaining.discard(n)
        if not remaining:
            break
    feat.extend(missing)

    # 4) 多尺度熵
    for W in ENTROPY_WINDOWS:
        cnt = {}
        tot = 0
        for d in hist[:W]:
            for n in d[key]:
                if 1 <= n <= max_n:
                    cnt[n] = cnt.get(n, 0) + 1
                    tot += 1
        feat.append(_entropy(cnt, tot))

    # 5) 和值统计（该球种滚动的和均值/众数/标准差近似）
    sums = [sum(d[key]) for d in hist[:50]]
    if sums:
        feat.append(float(np.mean(sums)))
        feat.append(float(np.std(sums)))
        feat.append(float(max(set(sums), key=sums.count)))
    else:
        feat.extend([0.0, 0.0, 0.0])

    return np.asarray(feat, dtype=np.float64)


# ─────────────────────────── 训练 / 预测 ───────────────────────────

def _build_position_model(calibrated=True):
    rf = RandomForestClassifier(n_estimators=50, max_depth=6, min_samples_leaf=25,
                                random_state=42, n_jobs=-1, class_weight="balanced")
    if calibrated:
        rf = CalibratedClassifierCV(rf, cv=2, method="sigmoid")
    hgb = HistGradientBoostingClassifier(max_iter=100, max_depth=4, min_samples_leaf=30,
                                         learning_rate=0.05, random_state=42)
    lr = LogisticRegression(max_iter=300, C=0.1, class_weight="balanced")
    return VotingClassifier([("rf", rf), ("hgb", hgb), ("lr", lr)], voting="soft")


def _fit_position_model(X, y):
    """优先带校准；若某类样本过少无法校准则退回未校准。"""
    try:
        m = _build_position_model(calibrated=True)
        m.fit(X, y)
        return m
    except ValueError:
        m = _build_position_model(calibrated=False)
        m.fit(X, y)
        return m


def train(lottery_type, history, cfg, save=True, model_dir=MODEL_DIR, lag_window=LAG_WINDOW, freq_window=FREQ_WINDOW):
    if len(history) < lag_window + 50:
        raise RuntimeError("历史数据不足，无法训练")
    n_pos_red = cfg["red_count"]
    n_pos_blue = cfg["blue_count"]
    models = {"red": [], "blue": [], "signature": {"lag": lag_window, "freq": freq_window}}
    if not save:
        models["_no_save"] = True

    for ball, n_pos, max_n in (("red", n_pos_red, cfg["red_max"]), ("blue", n_pos_blue, cfg["blue_max"])):
        key = "reds" if ball == "red" else "blues"
        # 目标 i 的特征仅用 i 之后的更旧期
        X, ys = [], [[] for _ in range(n_pos)]
        for i in range(0, len(history) - lag_window):
            hist = history[i + 1: i + 1 + (freq_window if freq_window > lag_window else lag_window)]
            vals = sorted(history[i][key])
            if len(vals) < n_pos:
                continue
            X.append(build_features(hist, ball, cfg, lag_window=lag_window, freq_window=freq_window))
            for p in range(n_pos):
                ys[p].append(vals[p])
        if not X:
            raise RuntimeError("训练样本为空")
        X = np.asarray(X)
        for p in range(n_pos):
            m = _fit_position_model(X, ys[p])
            models[ball].append(m)
        print(f"  {ball} 位模型 {n_pos} 个已训练（样本 {len(X)}）", file=sys.stderr)

    if not save:
        return models, None
    os.makedirs(model_dir, exist_ok=True)
    path = os.path.join(model_dir, f"{lottery_type}_models.joblib")
    joblib.dump(models, path)
    return models, path


def number_probabilities(models, ball, hist, cfg, lag_window=LAG_WINDOW, freq_window=FREQ_WINDOW):
    """返回指定球种每个号码的概率（供融合算法调用）。hist 为更旧在前。"""
    feat = build_features(hist, ball, cfg, lag_window=lag_window, freq_window=freq_window)
    max_n = cfg["red_max"] if ball == "red" else cfg["blue_max"]
    scores = _number_scores(models, ball, feat, max_n)
    total = float(scores.sum())
    if total > 0:
        scores = scores / total
    return {i + 1: float(scores[i]) for i in range(max_n)}


def load_models(lottery_type, cfg, model_dir=MODEL_DIR):
    path = os.path.join(model_dir, f"{lottery_type}_models.joblib")
    if not os.path.exists(path):
        return None
    return joblib.load(path)


def _regime_and_temp(history, cfg):
    """按近 50 期红球熵分档取温度"""
    cnt, tot = {}, 0
    for d in history[:50]:
        for n in d["reds"]:
            cnt[n] = cnt.get(n, 0) + 1
            tot += 1
    ent = _entropy(cnt, tot)
    hi = math.log(cfg["red_max"], 2) * 0.92
    lo = math.log(cfg["red_max"], 2) * 0.82
    regime = 2 if ent >= hi else (0 if ent <= lo else 1)
    return regime, TEMPERATURES[regime]


def _number_scores(models, ball, feat, max_n):
    """把各位置模型概率聚合为每个号码的得分"""
    scores = np.zeros(max_n)
    for m in models[ball]:
        try:
            proba = m.predict_proba([feat])[0]
        except Exception:
            continue
        for cls, p in zip(m.classes_, proba):
            c = int(cls)
            if 1 <= c <= max_n:
                scores[c - 1] += float(p)
    total = scores.sum()
    if total > 0:
        scores /= total
    return scores


def _sample_numbers(scores, k, temperature, rng):
    p = np.asarray(scores, dtype=np.float64)
    p = np.power(np.clip(p, 1e-12, None), 1.0 / max(0.05, temperature))
    p = p / p.sum()
    p = (1 - UNIFORM_MIX) * p + UNIFORM_MIX * (np.ones_like(p) / len(p))
    picked = rng.choice(len(p), size=k, replace=False, p=p)
    return sorted(int(i) + 1 for i in picked)


def _sum_ok(nums, recent_sums):
    if not recent_sums:
        return True
    mean = float(np.mean(recent_sums))
    mode = max(set(recent_sums), key=recent_sums.count)
    s = sum(nums)
    return abs(s - mean) <= SUM_MEAN_ALLOWANCE * max(1.0, mean) or abs(s - mode) <= SUM_MODE_ALLOWANCE * max(1.0, mode)


def predict(lottery_type, models, history, cfg, count, rng, lag_window=LAG_WINDOW, freq_window=FREQ_WINDOW):
    feat_red = build_features(history, "red", cfg, lag_window=lag_window, freq_window=freq_window)
    feat_blue = build_features(history, "blue", cfg, lag_window=lag_window, freq_window=freq_window)
    red_scores = _number_scores(models, "red", feat_red, cfg["red_max"])
    blue_scores = _number_scores(models, "blue", feat_blue, cfg["blue_max"])
    recent_sums_red = [sum(d["reds"]) for d in history[:50]]
    recent_sums_blue = [sum(d["blues"]) for d in history[:50]]
    regime, temp = _regime_and_temp(history, cfg)

    runs = []
    for _ in range(PREDICTION_RUNS):
        for _ in range(MAX_RETRIES):
            reds = _sample_numbers(red_scores, cfg["red_count"], temp, rng)
            if _sum_ok(reds, recent_sums_red):
                break
        for _ in range(MAX_RETRIES):
            blues = _sample_numbers(blue_scores, cfg["blue_count"], temp, rng)
            if _sum_ok(blues, recent_sums_blue):
                break
        conf = round(float(np.mean([red_scores[n - 1] for n in reds]) * 100 * (cfg["red_count"])) +
                     float(np.mean([blue_scores[n - 1] for n in blues]) * 100 * (cfg["blue_count"])), 1)
        runs.append({"red_numbers": reds, "blue_numbers": blues,
                     "algorithm": "lottery_ml", "confidence": min(99.0, max(0.0, conf))})

    # 共识：按位多数投票
    def consensus(prefix):
        n_pos = cfg["red_count"] if prefix == "red" else cfg["blue_count"]
        out = []
        for pos in range(n_pos):
            from collections import Counter
            c = Counter(r[f"{prefix}_numbers"][pos] for r in runs)
            out.append(c.most_common(1)[0][0])
        return sorted(out)

    # 选取前 count 注（去重优先）
    seen, chosen = set(), []
    for r in runs:
        key = (tuple(r["red_numbers"]), tuple(r["blue_numbers"]))
        if key in seen:
            continue
        seen.add(key)
        chosen.append(r)
        if len(chosen) >= count:
            break
    while len(chosen) < count:
        chosen.append(runs[len(chosen) % len(runs)])

    return chosen, {"red_numbers": consensus("red"), "blue_numbers": consensus("blue"),
                    "regime": regime, "temperature": temp}


def accuracy(lottery_type, history, cfg, tests=60):
    """时间序列外验证：仅在较旧数据上训练、在较新数据上回测（无数据泄漏）"""
    split = max(LAG_WINDOW + 60, int(len(history) * 0.8))
    if split >= len(history) - 10:
        split = len(history) - 10
    train_hist = history[split:]           # 较旧数据用于训练
    models, _ = train(lottery_type, train_hist, cfg, save=False)

    rng = np.random.default_rng(42)
    red_hits, blue_hits = [], []
    total = min(tests, split - LAG_WINDOW - 1)
    for i in range(0, total):
        hist = history[i + 1:]              # 全部比目标更旧的期（含测试区间内更旧者，非标签泄漏）
        try:
            preds, _ = predict(lottery_type, models, hist, cfg, 1, rng)
        except Exception:
            continue
        pr, pb = set(preds[0]["red_numbers"]), set(preds[0]["blue_numbers"])
        red_hits.append(len(pr & set(history[i]["reds"])))
        blue_hits.append(len(pb & set(history[i]["blues"])))
        if (i + 1) % 20 == 0:
            print(f"  回测进度 {i + 1}/{total}", file=sys.stderr)
    base_red = cfg["red_count"] * cfg["red_count"] / cfg["red_max"]
    base_blue = cfg["blue_count"] * cfg["blue_count"] / cfg["blue_max"]
    return {
        "samples": len(red_hits),
        "avg_red_hits": round(float(np.mean(red_hits)), 4) if red_hits else 0.0,
        "baseline_red": round(base_red, 4),
        "avg_blue_hits": round(float(np.mean(blue_hits)), 4) if blue_hits else 0.0,
        "baseline_blue": round(base_blue, 4),
        "note": "train on older 80%, test on recent; no leakage",
    }


def main():
    ap = argparse.ArgumentParser(description="LotteryML_clean 预测器")
    ap.add_argument("--type", choices=["ssq", "dlt"], default="ssq")
    ap.add_argument("--count", type=int, default=5)
    ap.add_argument("--force-retrain", action="store_true")
    ap.add_argument("--train-only", action="store_true")
    ap.add_argument("--accuracy", action="store_true")
    ap.add_argument("--accuracy-tests", type=int, default=60, help="accuracy 回测期数（越大越慢）")
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--model-dir", default=MODEL_DIR, help="模型保存目录（建议用户 AppData）")
    ap.add_argument("--output", default=None)
    ap.add_argument("--db-host", default=None)
    ap.add_argument("--db-port", type=int, default=None)
    ap.add_argument("--db-name", default=None)
    ap.add_argument("--db-user", default=None)
    ap.add_argument("--db-password", default=None)
    args = ap.parse_args()

    cfg = CONFIGS[args.type]
    db_password = args.db_password or os.environ.get("PGPASSWORD") or os.environ.get("NEWCOSMOS_DB_PASSWORD")

    history = []
    if args.db_host and args.db_name and args.db_user and db_password:
        history = load_history_from_db(args.type, args.db_host, args.db_port or 5432,
                                       args.db_name, args.db_user, db_password)
    if not history:
        print("[ERROR] 无历史数据（需 --db-* 与 PGPASSWORD）", file=sys.stderr)
        sys.exit(1)

    # accuracy 采用“训练集/测试集”时间切分，先于模型加载执行（避免用全量模型产生数据泄漏）
    if args.accuracy:
        result = accuracy(args.type, history, cfg, tests=args.accuracy_tests)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return

    models = None if args.force_retrain else load_models(args.type, cfg, args.model_dir)
    if models is None:
        print(f"训练 {args.type} 模型...", file=sys.stderr)
        models, path = train(args.type, history, cfg, model_dir=args.model_dir)
        print(f"模型已保存: {path}", file=sys.stderr)

    if args.train_only:
        print(json.dumps({"lottery_type": args.type, "trained": True, "timestamp": datetime.now().isoformat()},
                         ensure_ascii=False))
        return

    rng = np.random.default_rng(args.seed)

    preds, cons = predict(args.type, models, history, cfg, args.count, rng)
    output = {
        "lottery_type": args.type,
        "algorithm": "lottery_ml",
        "count": len(preds),
        "prediction_runs": PREDICTION_RUNS,
        "predictions": preds,
        "consensus": cons,
        "timestamp": datetime.now().isoformat(),
    }
    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(output, f, ensure_ascii=False, indent=2)
        print(f"预测结果已保存: {args.output}")
    else:
        print(json.dumps(output, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
