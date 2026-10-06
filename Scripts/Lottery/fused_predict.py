# -*- coding: utf-8 -*-
r"""
融合算法预测（评分级）：LSTM(原版机器学习) 0.6 : LotteryML 0.4

流程：
  1) 载入两套模型；分别取红/蓝每号分数/概率并归一化，加权融合（不含随机成分）；
  2) 生成 CANDIDATES(默认100) 注候选：温度采样 + 和值过滤 + 注内去重 + 避热门；
  3) 对候选做“位次频率投票”，选出得分最高且互异的 N 注；
  4) 仅输出这 N 注（是否入库由上层决定），中间 100 注不落库。

历史购买记录用作“负样本排斥”：未命中购彩中的高频号在采样时轻微降权。

用法：
    $env:PGPASSWORD='...'
    python fused_predict.py --type ssq --count 5 --db-host ... --ml-model-dir .\models --lml-model-dir <AppData>\NewCosmos\LotteryML
"""
import os
import sys
import json
import math
import argparse
from collections import Counter

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _HERE)
sys.path.insert(0, os.path.join(_HERE, "LotteryML_clean"))

from predict_lottery import LotteryPredictor, MLPredictor  # noqa: E402
import predictor as lml  # noqa: E402  (LotteryML_clean)

try:
    import psycopg2
except ImportError:
    psycopg2 = None

WEIGHT_LSTM = 0.6
WEIGHT_LML = 0.4
CANDIDATES = 100
NEG_PENALTY = 0.9


def _norm(d):
    s = sum(d.values())
    return {k: (v / s if s > 0 else 0.0) for k, v in d.items()}


def _load_negatives(lottery_type, host, port, name, user, pwd):
    """未命中的历史购买 → 负样本号码频率（用于轻微降权）。"""
    if psycopg2 is None or not pwd:
        return Counter()
    try:
        conn = psycopg2.connect(host=host, port=port, dbname=name, user=user, password=pwd)
        cur = conn.cursor()
        cur.execute("""SELECT red_numbers, blue_numbers FROM nc_lottery_user_purchases
                       WHERE lottery_type=%s AND is_verified=TRUE AND is_hit=FALSE""", (lottery_type.upper(),))
        cnt = Counter()
        for r, b in cur.fetchall():
            for n in (json.loads(r) if isinstance(r, str) else r or []):
                cnt[int(n)] += 1
        cur.close(); conn.close()
        return cnt
    except Exception:
        return Counter()


def _penalty_map(neg: Counter, max_n: int):
    if not neg:
        return {n: 1.0 for n in range(1, max_n + 1)}
    vals = [neg.get(n, 0) for n in range(1, max_n + 1)]
    threshold = max(1, int(np.mean(vals) + np.std(vals)))
    return {n: (NEG_PENALTY if neg.get(n, 0) >= threshold else 1.0) for n in range(1, max_n + 1)}


def _sample(probs, k, temp, rng, penal):
    p = np.array([max(0.0, probs.get(i + 1, 0.0)) * penal.get(i + 1, 1.0) for i in range(len(penal))])
    if p.sum() <= 0:
        p = np.ones(len(penal))
    p = np.power(np.clip(p, 1e-12, None), 1.0 / max(0.05, temp))
    p = p / p.sum()
    p = (1 - lml.UNIFORM_MIX) * p + lml.UNIFORM_MIX * (np.ones_like(p) / len(p))
    idx = rng.choice(len(p), size=k, replace=False, p=p)
    return sorted(int(i) + 1 for i in idx)


def main():
    ap = argparse.ArgumentParser(description="融合算法预测（LSTM 0.6 + LotteryML 0.4）")
    ap.add_argument("--type", choices=["ssq", "dlt"], default="ssq")
    ap.add_argument("--count", type=int, default=5, help="最终输出注数 N")
    ap.add_argument("--candidates", type=int, default=CANDIDATES, help="内部候选注数")
    ap.add_argument("--weight-lstm", type=float, default=WEIGHT_LSTM)
    ap.add_argument("--weight-lml", type=float, default=WEIGHT_LML)
    ap.add_argument("--ml-model-dir", default=os.path.join(_HERE, "models"))
    ap.add_argument("--lml-model-dir", default=lml.MODEL_DIR)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--output", default=None)
    ap.add_argument("--db-host", default=None)
    ap.add_argument("--db-port", type=int, default=None)
    ap.add_argument("--db-name", default=None)
    ap.add_argument("--db-user", default=None)
    ap.add_argument("--db-password", default=None)
    args = ap.parse_args()

    pwd = args.db_password or os.environ.get("PGPASSWORD") or os.environ.get("NEWCOSMOS_DB_PASSWORD")
    cfg = lml.CONFIGS[args.type]
    rm, bm, rc, bc = cfg["red_max"], cfg["blue_max"], cfg["red_count"], cfg["blue_count"]

    # ── 历史开奖（最新在前）──
    loader = LotteryPredictor(args.type)
    draws = []
    if args.db_host and args.db_name and args.db_user and pwd:
        draws = loader.load_history_from_db(args.db_host, args.db_port or 5432, args.db_name, args.db_user, pwd)
    if not draws:
        print("[ERROR] 无历史开奖数据", file=sys.stderr)
        sys.exit(1)
    n = len(draws)

    # ── LSTM 分数（最近 30 期，时间顺序）──
    lstm = MLPredictor(args.type)
    if not lstm.load_model(args.ml_model_dir):
        print("[ERROR] 机器学习(LSTM)模型未训练或加载失败", file=sys.stderr)
        sys.exit(2)
    ml_hist = [{"red_numbers": d["red_numbers"], "blue_numbers": d["blue_numbers"]}
               for d in reversed(draws[:30])]
    ml_scores = lstm.score_numbers(ml_hist)
    if not ml_scores:
        print("[ERROR] LSTM 分数计算失败", file=sys.stderr)
        sys.exit(2)
    ml_red, ml_blue = ml_scores

    # ── LotteryML 概率 ──
    ls_models = lml.load_models(args.type, cfg, args.lml_model_dir)
    if ls_models is None:
        print("[ERROR] LotteryML 模型未训练或加载失败", file=sys.stderr)
        sys.exit(2)
    lml_hist = [{"reds": d["red_numbers"], "blues": d["blue_numbers"]} for d in draws]
    lml_red = lml.number_probabilities(ls_models, "red", lml_hist, cfg, freq_window=100)
    lml_blue = lml.number_probabilities(ls_models, "blue", lml_hist, cfg, freq_window=100)

    # ── 归一化 + 加权融合（无随机成分）──
    mlr, mlb = _norm(ml_red), _norm(ml_blue)
    lr, lb = _norm(lml_red), _norm(lml_blue)
    w1, w2 = args.weight_lstm, args.weight_lml
    fused_red = {i: w1 * mlr.get(i, 0.0) + w2 * lr.get(i, 0.0) for i in range(1, rm + 1)}
    fused_blue = {i: w1 * mlb.get(i, 0.0) + w2 * lb.get(i, 0.0) for i in range(1, bm + 1)}

    # ── 负样本排斥（历史未命中购买的高频号轻微降权）──
    neg = _load_negatives(args.type, args.db_host, args.db_port or 5432, args.db_name, args.db_user, pwd)
    penal_red = _penalty_map(neg, rm)
    penal_blue = _penalty_map(neg, bm)

    # ── 生成候选（温度 + 和值过滤 + 注内去重）──
    rng = np.random.default_rng(args.seed)
    regime, temp = lml._regime_and_temp(lml_hist, cfg)
    recent_sums_red = [sum(d["red_numbers"]) for d in draws[:50]]
    recent_sums_blue = [sum(d["blue_numbers"]) for d in draws[:50]]

    candidates = []
    for _ in range(args.candidates):
        for _ in range(lml.MAX_RETRIES):
            reds = _sample(fused_red, rc, temp, rng, penal_red)
            if lml._sum_ok(reds, recent_sums_red):
                break
        for _ in range(lml.MAX_RETRIES):
            blues = _sample(fused_blue, bc, temp, rng, penal_blue)
            if lml._sum_ok(blues, recent_sums_blue):
                break
        candidates.append((tuple(reds), tuple(blues)))

    # ── 位次频率投票 → 选 N 注 ──
    red_pos = [Counter() for _ in range(rc)]
    blue_pos = [Counter() for _ in range(bc)]
    for reds, blues in candidates:
        for i, v in enumerate(reds):
            red_pos[i][v] += 1
        for i, v in enumerate(blues):
            blue_pos[i][v] += 1

    def vote_score(t):
        reds, blues = t
        return (sum(red_pos[i][v] for i, v in enumerate(reds))
                + sum(blue_pos[i][v] for i, v in enumerate(blues)))

    ranked = sorted(set(candidates), key=vote_score, reverse=True)
    chosen = ranked[: max(1, args.count)]

    uni_red = 1.0 / rm
    uni_blue = 1.0 / bm

    def conf(t):
        reds, blues = t
        rr = (sum(fused_red.get(v, 0.0) for v in reds) / rc) / uni_red
        br = (sum(fused_blue.get(v, 0.0) for v in blues) / bc) / uni_blue
        return round(min(99.0, max(0.0, (0.7 * rr + 0.3 * br) * 50.0)), 1)

    predictions = [{"red_numbers": list(reds), "blue_numbers": list(blues),
                    "algorithm": "fusion", "confidence": conf((reds, blues))}
                   for reds, blues in chosen]

    output = {
        "lottery_type": args.type,
        "algorithm": "fusion",
        "count": len(predictions),
        "candidates": args.candidates,
        "weights": {"lstm": w1, "lottery_ml": w2},
        "regime": regime,
        "predictions": predictions,
        "timestamp": __import__("datetime").datetime.now().isoformat(),
    }
    output = json.loads(json.dumps(output, default=lambda o: int(o) if isinstance(o, np.integer) else float(o)))

    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(output, f, ensure_ascii=False, indent=2)
        print(f"预测结果已保存: {args.output}")
    else:
        print(json.dumps(output, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
