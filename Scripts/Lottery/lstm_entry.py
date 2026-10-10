# -*- coding: utf-8 -*-
"""
lstm_entry —— NewCosmos 彩票机器学习(LSTM)统一入口（train / predict）。

内嵌依赖：KittenCN/predict_Lottery_ticket（commit 6cf60bb730e78e5e77ab0fdcd151fced9f19385a，
GPL-3.0）——「基于tensorflow lstm模型的彩票预测」，原生支持双色球(ssq)/大乐透(dlt)。
本文件为 NewCosmos 侧适配层，职责：

  1) PATHS 原地重定向到可写主目录（NEWCOSMOS_LOTTERY_HOME 优先，缺省 %APPDATA%\\NewCosmos\\LotteryLSTM），
     安装在 Program Files 时数据/模型仍可写；原版 config/config.yaml 保持 GitHub 默认不动；
  2) 数据源：开奖数据下载链路（Scripts\\Lottery\\fetch_history.py → PostgreSQL nc_lottery_draws，
     由应用首页「同步开奖数据」维护）。本层只读该表导出为原版 CSV（期数/红球_i/蓝球_i），
     **不联网抓取**——数据库不可达或期数不足时直接报错，提示先同步数据；
  3) train  ：导出全量历史 → 原版 train_lottery_models，
     网络结构/epochs/batch/学习率/早停全 GitHub 默认，窗口 --window 默认 73；
  4) predict：导出全量历史 → 取**最后 window 期**作为输入窗口 → 模型逐位概率 →
     负样本降权（未中奖购彩高频号 ×0.9，仅预测期生效）→ 温度采样 N 注互异 →
     输出 C# LotteryPredictService.ParsePredictions 兼容 JSON（stdout 仅 JSON，日志走 stderr）。

原版源码改动仅 1 处：src\\bootstrap.py（原版自述为「runtime compatibility」兼容层）
追加 Keras 3 下 tf.transpose 不能吃 KerasTensor 的适配；模型定义 src\\modeling.py 保持原样。

用法：
  python lstm_entry.py train   --name ssq --window 73 --db-host ... --db-port ... --db-name ... --db-user ...
  python lstm_entry.py predict --name ssq --count 5 --window 73 --db-host ... --db-port ... --db-name ... --db-user ...
"""
import argparse
import json
import os
import platform
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

_HERE = Path(__file__).resolve().parent  # Scripts\Lottery
_PROJECT = _HERE / "predict_Lottery_ticket"
if not (_PROJECT / "src").is_dir():
    print(f"[ERROR] 原版工程缺失: {_PROJECT}", file=sys.stderr)
    sys.exit(3)
sys.path.insert(0, str(_PROJECT))

import pandas as pd  # noqa: E402

import src.config as _cfg  # noqa: E402  (必须先于任何 pipeline 导入)


def _resolve_home() -> Path:
    env = os.environ.get("NEWCOSMOS_LOTTERY_HOME")
    if env:
        return Path(env)
    if platform.system() == "Windows":
        appdata = os.environ.get("APPDATA") or str(Path.home() / "AppData" / "Roaming")
        return Path(appdata) / "NewCosmos" / "LotteryLSTM"
    return Path.home() / ".local" / "share" / "NewCosmos" / "LotteryLSTM"


# PATHS 原地变异（pipeline/data_fetcher 以同一 dict 对象引用，变异即全体生效）
_HOME = _resolve_home()
for _key in ("data", "model", "predict", "logs"):
    _cfg.PATHS[_key] = _HOME / _key
for _p in _cfg.PATHS.values():
    Path(_p).mkdir(parents=True, exist_ok=True)

# 原版 TF/Keras 兼容 shim（best-effort，与原版 scripts 一致）
try:
    import src.bootstrap  # noqa: F401
except Exception:
    pass

from src.common import train_pipeline  # noqa: E402
from src.config import DATA_FILE_NAME, PATHS, get_lottery_config  # noqa: E402
from src.data_fetcher import load_history  # noqa: E402

NEG_PENALTY = 0.9        # 负样本降权系数（沿用原融合版）
TEMPERATURE = 1.0        # 采样温度（1.0=按模型概率原样采样）
DEFAULT_WINDOW = 73      # 回看窗口（与 Constants\LotteryConstants.LSTM_WINDOW 同步）
DEFAULT_COUNT = 5
MAX_COUNT = 20
MAX_DUP_RETRY = 40       # 单注互异采样重试上限

# 本层暴露的彩种（原版另支持 pls/qxc/kl8/sd，应用侧不暴露）
SUPPORTED_LOTTERIES = ["ssq", "dlt"]

# 开奖数据下载链路落库表（唯一数据源）
DRAWS_TABLE = "nc_lottery_draws"


def _warn(msg: str) -> None:
    print(msg, file=sys.stderr)


def _db_connect(args):
    """按 --db-* 参数建立 psycopg2 连接。密码只走环境变量，不进命令行（AGENTS §12）。"""
    if not args.db_host:
        raise RuntimeError(f"缺少数据库连接参数（--db-host），无法从 {DRAWS_TABLE} 导出开奖数据")
    try:
        import psycopg2
    except ImportError as exc:
        raise RuntimeError("缺少 psycopg2 依赖，无法从数据库导出开奖数据") from exc
    pwd = os.environ.get("PGPASSWORD") or os.environ.get("NEWCOSMOS_DB_PASSWORD")
    if not pwd:
        raise RuntimeError("未设置数据库密码环境变量（PGPASSWORD / NEWCOSMOS_DB_PASSWORD）")
    return psycopg2.connect(
        host=args.db_host,
        port=args.db_port or 5432,
        dbname=args.db_name,
        user=args.db_user,
        password=pwd,
        connect_timeout=5,
    )


def _export_from_db(code: str, args, window: int) -> int:
    """从 nc_lottery_draws 导出全量历史到 data/<code>/data.csv（原版 CSV 格式）。返回导出期数。"""
    cfg = get_lottery_config(code)
    red_count = cfg.red.sequence_len
    blue_count = cfg.blue.sequence_len if cfg.blue else 0

    conn = _db_connect(args)
    try:
        cur = conn.cursor()
        # 期号在 SSQ/DLT 下等长零填充，字符串升序 == 时间升序（已核对全库 0 偏差）
        cur.execute(
            f"SELECT draw_number, red_numbers_json, blue_numbers_json "
            f"FROM {DRAWS_TABLE} WHERE lottery_type=%s ORDER BY draw_number",
            (code.upper(),),
        )
        rows = cur.fetchall()
        cur.close()
    finally:
        conn.close()

    records = []
    skipped = 0
    for draw_number, red_json, blue_json in rows:
        reds = json.loads(red_json) if isinstance(red_json, str) else (red_json or [])
        blues = json.loads(blue_json) if isinstance(blue_json, str) else (blue_json or [])
        if len(reds) != red_count or len(blues) != blue_count:
            skipped += 1
            continue
        record = {"期数": str(draw_number)}
        for idx, value in enumerate(reds):
            record[f"红球_{idx + 1}"] = f"{int(value):02d}"
        for idx, value in enumerate(blues):
            record[f"蓝球_{idx + 1}"] = f"{int(value):02d}"
        records.append(record)

    if len(records) < window + 1:
        raise RuntimeError(
            f"开奖数据不足: {len(records)} 期 <= 窗口 {window}，"
            f"请先在彩票首页同步 {cfg.name} 开奖数据"
        )

    save_dir = PATHS["data"] / code
    save_dir.mkdir(parents=True, exist_ok=True)
    output_path = save_dir / DATA_FILE_NAME
    pd.DataFrame(records).to_csv(output_path, index=False, encoding="utf-8")

    meta = {
        "code": code,
        "source": "postgresql",
        "table": DRAWS_TABLE,
        "total_issues": len(records),
        "skipped": skipped,
        "saved_path": str(output_path),
        "timestamp": datetime.now(timezone.utc).isoformat(),
    }
    (save_dir / "download_meta.json").write_text(
        json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    _warn(f"[INFO] 已从 {DRAWS_TABLE} 导出 {len(records)} 期{cfg.name}历史 → {output_path}")
    if skipped:
        _warn(f"[WARN] 跳过 {skipped} 期号码不完整的记录")
    return len(records)


def _ensure_data(code: str, args, window: int) -> None:
    """导出训练/预测所需历史数据（唯一来源 = nc_lottery_draws，失败即终止）。"""
    try:
        _export_from_db(code, args, window)
    except SystemExit:
        raise
    except Exception as exc:
        _warn(f"[ERROR] 从数据库导出开奖数据失败: {exc}")
        sys.exit(3)


def _load_negative_penalties(args, cfg):
    """未中奖购彩记录 → (红球降权表, 蓝球降权表)。频次 >= mean+std 的号 ×NEG_PENALTY。
    无 psycopg2 / 无连接参数 / 无密码时返回空表（负样本仅增强，不阻断预测）。
    降权表尺寸按彩种 num_classes 生成（双色球 33/16，大乐透 35/12）。"""
    red_max = cfg.red.num_classes
    blue_max = cfg.blue.num_classes if cfg.blue else 0

    def _flat(max_n):
        return {n: 1.0 for n in range(1, max_n + 1)}

    empty = (_flat(red_max), _flat(blue_max))
    if not args.db_host:
        return empty
    try:
        import psycopg2
    except ImportError:
        return empty
    pwd = os.environ.get("PGPASSWORD") or os.environ.get("NEWCOSMOS_DB_PASSWORD")
    if not pwd:
        return empty
    red_cnt, blue_cnt = Counter(), Counter()
    try:
        conn = psycopg2.connect(
            host=args.db_host, port=args.db_port or 5432, dbname=args.db_name,
            user=args.db_user, password=pwd, connect_timeout=5,
        )
        cur = conn.cursor()
        cur.execute(
            "SELECT red_numbers, blue_numbers FROM nc_lottery_user_purchases "
            "WHERE lottery_type=%s AND is_verified=TRUE AND is_hit=FALSE",
            (cfg.code.upper(),),
        )
        for r, b in cur.fetchall():
            for n in (json.loads(r) if isinstance(r, str) else (r or [])):
                red_cnt[int(n)] += 1
            for n in (json.loads(b) if isinstance(b, str) else (b or [])):
                blue_cnt[int(n)] += 1
        cur.close()
        conn.close()
    except Exception as exc:
        _warn(f"[WARN] 负样本查询失败（跳过降权）: {exc}")
        return empty

    def penalty(cnt, max_n):
        if not cnt or max_n <= 0:
            return _flat(max_n)
        vals = [cnt.get(n, 0) for n in range(1, max_n + 1)]
        mean = sum(vals) / len(vals)
        var = sum((v - mean) ** 2 for v in vals) / len(vals)
        threshold = max(1, int(mean + var ** 0.5))
        return {n: (NEG_PENALTY if cnt.get(n, 0) >= threshold else 1.0)
                for n in range(1, max_n + 1)}

    return penalty(red_cnt, red_max), penalty(blue_cnt, blue_max)


def _sample_position(probs_row, chosen, penal, rng, temp=TEMPERATURE):
    """按位采样一个类别索引（降权 + 注内去重 + 温度）。probs_row: 该球位的类别概率。"""
    import numpy as np
    p = np.array([max(0.0, float(probs_row[i])) * penal.get(i + 1, 1.0)
                  for i in range(len(probs_row))], dtype=float)
    for idx in chosen:
        if idx < len(p):
            p[idx] = 0.0
    if p.sum() <= 0:
        p = np.ones(len(p)) / len(p)
    else:
        p = np.power(p, 1.0 / max(0.05, temp))
        p = p / p.sum()
    return int(rng.choice(len(p), p=p))


def _latest_window(df, prefix, count, window, needs_offset):
    """取历史最后 window 期构成预测输入（shape: window × count）。

    必须取「最后 window 期」而非训练样本数组的最后一条——后者是
    row[n-window-1 : n-1]，会让模型复述最后一期已知开奖，而不是预测下一期。
    """
    import numpy as np
    columns = [f"{prefix}_{idx + 1}" for idx in range(count)]
    values = df[columns].to_numpy(dtype=np.int32)
    if needs_offset:
        values = values - 1
    return values[-window:]


def cmd_train(args) -> None:
    _ensure_data(args.name, args, args.window)
    summary = train_pipeline(name=args.name, window_size=args.window)
    out = {
        "action": "train",
        "lottery_type": args.name,
        "window": args.window,
        "code": getattr(summary, "code", args.name),
        "trained_on_issues": list(getattr(summary, "trained_on_issues", ())),
        "timestamp": getattr(summary, "timestamp", ""),
    }
    print(json.dumps(out, ensure_ascii=False))


def cmd_predict(args) -> None:
    import numpy as np
    from src.pipeline import load_trained_models
    from src.preprocessing import prepare_training_arrays

    code = args.name
    cfg = get_lottery_config(code)
    window = args.window

    # 模型就绪检查（窗口目录 red/blue 双模型）
    model_dir = PATHS["model"] / code / f"window_{window}"
    if not (model_dir / "red.keras").exists() or not (model_dir / "blue.keras").exists():
        _warn(f"[ERROR] 模型未训练或窗口不匹配: {model_dir}")
        sys.exit(2)

    _ensure_data(code, args, window)
    df = load_history(code)
    if len(df) <= window:
        _warn(f"[ERROR] 历史数据不足: {len(df)} 期 <= 窗口 {window}")
        sys.exit(3)

    arrays = prepare_training_arrays(df, cfg, window)
    models = load_trained_models(code, window)

    sorted_df = df.sort_values("期数").reset_index(drop=True)
    red_dataset = arrays["red"]
    red_input = _latest_window(
        sorted_df, "红球", cfg.red.sequence_len, window, red_dataset.needs_offset
    ).reshape(1, window, cfg.red.sequence_len)
    red_probs = models["red"].predict(red_input, verbose=0)[0]  # (seq_len, num_classes)

    blue_probs = None
    blue_needs_offset = False
    if cfg.blue and "blue" in models:
        blue_dataset = arrays["blue"]
        blue_needs_offset = blue_dataset.needs_offset
        blue_input = _latest_window(
            sorted_df, "蓝球", cfg.blue.sequence_len, window, blue_needs_offset
        ).reshape(1, window, cfg.blue.sequence_len)
        blue_probs = models["blue"].predict(blue_input, verbose=0)[0]  # (seq_len, num_classes)

    penal_red, penal_blue = _load_negative_penalties(args, cfg)
    rc, bc = cfg.red.sequence_len, (cfg.blue.sequence_len if cfg.blue else 0)
    rng = np.random.default_rng()  # 每次调用新熵源：连续预测出不同注

    seen = set()
    predictions = []
    for _ in range(max(1, args.count)):
        note = None
        for _attempt in range(MAX_DUP_RETRY):
            reds_idx = []
            for pos in range(rc):
                reds_idx.append(_sample_position(red_probs[pos], reds_idx, penal_red, rng))
            blues_idx = []
            if blue_probs is not None:
                for pos in range(bc):
                    blues_idx.append(_sample_position(blue_probs[pos], blues_idx, penal_blue, rng))
            key = (tuple(sorted(reds_idx)), tuple(sorted(blues_idx)))
            if key not in seen:
                seen.add(key)
                # 保留未排序位序：置信度要与概率行一一对应（排序后位置会错位）
                note = (reds_idx, blues_idx, key)
                break
            if note is None:
                note = (reds_idx, blues_idx, key)  # 兜底：重试耗尽仍重复则接受
        reds_idx, blues_idx, (sorted_reds, sorted_blues) = note

        # 类别索引 → 原始号码（needs_offset：数据为 1..N 时训练做了 -1）
        reds_out = [x + 1 for x in sorted_reds] if red_dataset.needs_offset else list(sorted_reds)
        blues_out = []
        if blue_probs is not None:
            blues_out = [x + 1 for x in sorted_blues] if blue_needs_offset else list(sorted_blues)

        # 置信度：选中号概率 / 均匀基线（0.7 红 + 0.3 蓝，×50 封顶 99——与原融合版口径一致）
        rr = float(np.mean([red_probs[i][reds_idx[i]] for i in range(rc)])) * cfg.red.num_classes
        bb = 1.0
        if blue_probs is not None and blues_idx:
            bb = float(np.mean([blue_probs[i][blues_idx[i]] for i in range(len(blues_idx))])) \
                * cfg.blue.num_classes
        confidence = round(min(99.0, max(0.0, (0.7 * rr + 0.3 * bb) * 50.0)), 1)

        predictions.append({
            "red_numbers": [int(x) for x in reds_out],
            "blue_numbers": [int(x) for x in blues_out],
            "confidence": confidence,
        })

    out = {
        "lottery_type": code,
        "algorithm": "lstm",
        "window": window,
        "count": len(predictions),
        "predictions": predictions,
    }
    print(json.dumps(out, ensure_ascii=False))


def _add_db_args(parser) -> None:
    parser.add_argument("--db-host", default=None)
    parser.add_argument("--db-port", type=int, default=5432)
    parser.add_argument("--db-name", default=None)
    parser.add_argument("--db-user", default=None)


def main() -> None:
    parser = argparse.ArgumentParser(description="NewCosmos 彩票 LSTM 统一入口")
    sub = parser.add_subparsers(dest="action", required=True)

    p_train = sub.add_parser("train", help="从 nc_lottery_draws 导出历史并训练 LSTM 模型")
    p_train.add_argument("--name", choices=SUPPORTED_LOTTERIES, required=True)
    p_train.add_argument("--window", type=int, default=DEFAULT_WINDOW)
    _add_db_args(p_train)

    p_pred = sub.add_parser("predict", help="预测下一期号码")
    p_pred.add_argument("--name", choices=SUPPORTED_LOTTERIES, required=True)
    p_pred.add_argument("--count", type=int, default=DEFAULT_COUNT)
    p_pred.add_argument("--window", type=int, default=DEFAULT_WINDOW)
    _add_db_args(p_pred)

    args = parser.parse_args()
    if args.action == "train":
        cmd_train(args)
    else:
        if args.count < 1 or args.count > MAX_COUNT:
            parser.error(f"--count 须在 1..{MAX_COUNT}")
        cmd_predict(args)


if __name__ == "__main__":
    main()
