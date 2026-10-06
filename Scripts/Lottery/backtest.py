# -*- coding: utf-8 -*-
r"""
彩票策略回测框架（walk-forward，逐期只用历史数据）

目的：用真实历史数据评估各选号策略相对“随机基线”的增益，给出
  - 平均红/蓝命中
  - 中奖率（任一奖级）
  - ROI = 奖金 / 成本
  - 相对随机基线的增益 + 95% 置信区间 + 显著性（含 Holm-Bonferroni 多重检验校正）

重要：彩票为独立同分布随机事件，任何算法都无法提高中奖概率。本框架用于“证伪/证真”，
      结论若显示无显著增益即为正常结果。

用法：
    $env:PGPASSWORD='...'
    python backtest.py --type ssq --tests 500 --window 100 --count 1
    python backtest.py --type dlt --tests 500 --window 100 --count 1 --algos random,statistics,quantitative,ensemble
"""
import os
import sys
import json
import math
import argparse
import random

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _HERE)
sys.path.insert(0, os.path.join(_HERE, "LotteryML_clean"))

from predict_lottery import (
    LotteryPredictor, RandomPicker, StatisticsPredictor,
    QuantitativePredictor, EnsemblePredictor, _diversify_results,
)
import predictor as ml

# 单注成本（元）
TICKET_COST = 2.0

# 简化奖级奖金（与 C# LotteryPredictService.GetPrizeAmount 口径一致）
PRIZE_AMOUNT = {1: 5_000_000, 2: 200_000, 3: 3_000, 4: 200, 5: 10, 6: 5, 0: 0}


def prize_level_ssq(red_hit: int, blue_hit: int) -> int:
    if red_hit == 6 and blue_hit == 1:
        return 1
    if red_hit == 6:
        return 2
    if red_hit == 5 and blue_hit == 1:
        return 3
    if red_hit == 5 or (red_hit == 4 and blue_hit == 1):
        return 4
    if red_hit == 4 or (red_hit == 3 and blue_hit == 1):
        return 5
    if blue_hit == 1:
        return 6
    return 0


def prize_level_dlt(red_hit: int, blue_hit: int) -> int:
    if red_hit == 5 and blue_hit == 2:
        return 1
    if red_hit == 5 and blue_hit == 1:
        return 2
    if red_hit == 5:
        return 3
    if red_hit == 4 and blue_hit == 2:
        return 4
    if red_hit == 4 and blue_hit == 1:
        return 5
    if red_hit == 3 and blue_hit == 2:
        return 5
    return 0


def make_predictor(algo: str, lottery_type: str, period: int):
    if algo == "random":
        return RandomPicker(lottery_type)
    if algo == "statistics":
        return StatisticsPredictor(lottery_type, period)
    if algo == "quantitative":
        return QuantitativePredictor(lottery_type, period)
    if algo == "ensemble":
        return EnsemblePredictor(lottery_type, period)
    raise ValueError(f"不支持的算法: {algo}")


def _phi(z: float) -> float:
    """标准正态分布 CDF"""
    return 0.5 * (1.0 + math.erf(z / math.sqrt(2.0)))


def run_algo(algo: str, lottery_type: str, draws, window: int, tests: int, count: int, avoid_popular: bool):
    """draws 为最新在前；对每个目标期 i，用 [i+1, i+1+window) 的历史预测第 i 期。"""
    n = len(draws)
    max_i = n - window - 1
    if max_i < 0:
        return None
    total = min(tests, max_i + 1)

    ml_models = None
    ml_cfg = None
    ml_rng = None
    if algo == "lottery_ml":
        ml_cfg = ml.CONFIGS[lottery_type]
        # 防泄漏：仅在比所有测试期历史更旧的期上训练
        train_start = total + window
        train_draws = draws[train_start:]
        if len(train_draws) < ml.LAG_WINDOW + 50:
            print("  [lottery_ml] 训练数据不足，跳过", file=sys.stderr)
            return None
        print(f"  [lottery_ml] 训练集 {len(train_draws)} 期（更旧，无泄漏）…", file=sys.stderr)
        train_ml = [{"reds": d["red_numbers"], "blues": d["blue_numbers"]} for d in train_draws]
        ml_models, _ = ml.train(lottery_type, train_ml, ml_cfg, save=False,
                                lag_window=ml.LAG_WINDOW, freq_window=window)
        ml_rng = np.random.default_rng(42)
        predictor = None
        red_count = ml_cfg["red_count"]
    else:
        predictor = make_predictor(algo, lottery_type, window)
        red_count = predictor.config["red_count"]

    red_hits, blue_hits, levels = [], [], []
    cost = 0.0
    prize = 0.0

    for i in range(0, total):
        history = draws[i + 1: i + 1 + window]
        actual_reds = set(int(x) for x in draws[i]["red_numbers"])
        actual_blues = set(int(x) for x in draws[i]["blue_numbers"])
        if algo == "lottery_ml":
            hist_ml = [{"reds": d["red_numbers"], "blues": d["blue_numbers"]} for d in history]
            preds, _ = ml.predict(lottery_type, ml_models, hist_ml, ml_cfg, count, ml_rng,
                                  lag_window=ml.LAG_WINDOW, freq_window=window)
            cfg_use = ml_cfg
        elif isinstance(predictor, RandomPicker):
            preds = predictor.predict(count)
            cfg_use = predictor.config
        else:
            preds = predictor.predict(count, history, min_confidence=0)
            cfg_use = predictor.config
        if not preds:
            continue
        if avoid_popular:
            preds = _diversify_results(preds, cfg_use, avoid_popular=True)
        if (i + 1) % 50 == 0:
            print(f"  [{algo}] {i + 1}/{total}", file=sys.stderr)
        for p in preds:
            pr = set(int(x) for x in p.get("red_numbers", []))
            pb = set(int(x) for x in p.get("blue_numbers", []))
            rh = len(pr & actual_reds)
            bh = len(pb & actual_blues)
            lvl = prize_level_ssq(rh, bh) if red_count == 6 else prize_level_dlt(rh, bh)
            red_hits.append(rh)
            blue_hits.append(bh)
            levels.append(lvl)
            cost += TICKET_COST
            prize += PRIZE_AMOUNT.get(lvl, 0)
    if not red_hits:
        return None

    m = len(red_hits)
    avg_red = sum(red_hits) / m
    avg_blue = sum(blue_hits) / m
    hit_rate = sum(1 for lvl in levels if lvl > 0) / m
    roi = prize / cost if cost else 0.0
    return {
        "algo": algo,
        "tickets": m,
        "avg_red_hits": round(avg_red, 4),
        "avg_blue_hits": round(avg_blue, 4),
        "any_prize_rate": round(hit_rate, 5),
        "cost": round(cost, 2),
        "prize": round(prize, 2),
        "roi": round(roi, 5),
        "_red_hits": red_hits,
    }


def holm_bonferroni(pvals):
    """返回 {index: adjusted_p}（Holm）"""
    order = sorted(range(len(pvals)), key=lambda k: pvals[k])
    m = len(pvals)
    adj = [1.0] * m
    running = 0.0
    for rank, idx in enumerate(order):
        val = min(1.0, (m - rank) * pvals[idx])
        running = max(running, val)
        adj[idx] = running
    return adj


def main():
    ap = argparse.ArgumentParser(description="彩票策略回测（walk-forward）")
    ap.add_argument("--type", choices=["ssq", "dlt"], default="ssq")
    ap.add_argument("--tests", type=int, default=500, help="回测目标期数")
    ap.add_argument("--window", type=int, default=100, help="每期使用的历史窗口")
    ap.add_argument("--count", type=int, default=1, help="每期投注注数")
    ap.add_argument("--algos", default="random,statistics,quantitative,ensemble,lottery_ml")
    ap.add_argument("--no-avoid-popular", action="store_true")
    ap.add_argument("--data-dir", default="./data")
    ap.add_argument("--output", default=None)
    # 数据库连接参数
    ap.add_argument("--db-host", default=None)
    ap.add_argument("--db-port", type=int, default=None)
    ap.add_argument("--db-name", default=None)
    ap.add_argument("--db-user", default=None)
    ap.add_argument("--db-password", default=None)
    args = ap.parse_args()

    db_password = args.db_password or os.environ.get("PGPASSWORD") or os.environ.get("NEWCOSMOS_DB_PASSWORD")

    loader = LotteryPredictor(args.type)
    draws = []
    if args.db_host and args.db_name and args.db_user and db_password:
        draws = loader.load_history_from_db(args.db_host, args.db_port or 5432,
                                           args.db_name, args.db_user, db_password)
    if not draws:
        draws = loader.load_history(args.data_dir)
    if not draws:
        print("[ERROR] 无历史数据", file=sys.stderr)
        sys.exit(1)

    # 供 run_algo 判断红球数量使用的彩种标记
    red_count = loader.config["red_count"]
    blue_count = loader.config["blue_count"]
    print(f"历史 {len(draws)} 期；回测目标 {args.tests} 期；窗口 {args.window}；每期 {args.count} 注", file=sys.stderr)

    algos = [a.strip() for a in args.algos.split(",") if a.strip()]
    # 随机基线（红球理论期望 + 经验 ROI）
    baseline_red = red_count * red_count / loader.config["red_max"]
    baseline_blue = blue_count * blue_count / loader.config["blue_max"]

    reports = []
    for a in algos:
        rep = run_algo(a, args.type, draws, args.window, args.tests, args.count, not args.no_avoid_popular)
        if rep:
            reports.append(rep)

    # 显著性检验：各算法红球命中均值 vs 随机理论均值
    pvals = []
    for rep in reports:
        hits = rep["_red_hits"]
        n = len(hits)
        mean = sum(hits) / n
        if n > 1:
            var = sum((x - mean) ** 2 for x in hits) / (n - 1)
            se = math.sqrt(var / n) if var > 0 else 0.0
        else:
            se = 0.0
        diff = mean - baseline_red
        z = diff / se if se > 0 else 0.0
        p = 2 * (1 - _phi(abs(z))) if se > 0 else 1.0
        rep["diff_vs_baseline"] = round(diff, 4)
        rep["ci95"] = round(1.96 * se, 4)
        rep["p_value"] = round(p, 5)
        pvals.append(p)
    adj = holm_bonferroni(pvals) if pvals else []
    for rep, ap_ in zip(reports, adj):
        rep["p_adj_holm"] = round(ap_, 5)

    report = {
        "lottery_type": args.type,
        "tests": args.tests,
        "window": args.window,
        "count_per_draw": args.count,
        "baseline_avg_red": round(baseline_red, 4),
        "baseline_avg_blue": round(baseline_blue, 4),
        "results": [{k: v for k, v in r.items() if not k.startswith("_")} for r in reports],
    }

    print("\n" + "=" * 92)
    print(f"{'算法':<12}{'均红':>8}{'均蓝':>8}{'中奖率':>9}{'ROI':>10}{'vs基线':>10}{'95%CI':>9}{'p':>9}{'p_adj':>9}")
    print("-" * 92)
    for r in reports:
        print(f"{r['algo']:<12}{r['avg_red_hits']:>8.3f}{r['avg_blue_hits']:>8.3f}"
              f"{r['any_prize_rate']*100:>8.2f}%{r['roi']:>10.4f}{r['diff_vs_baseline']:>10.3f}"
              f"{r['ci95']:>9.3f}{r['p_value']:>9.4f}{r['p_adj_holm']:>9.4f}")
    print("=" * 92)
    print(f"随机理论基线：均红={baseline_red:.3f} 均蓝={baseline_blue:.3f}")
    print("说明：diff>0 且 p_adj<0.05 才算统计显著优于随机；彩票期望收益恒为负，ROI<1 属正常。")

    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        print(f"\n报告已保存: {args.output}")


if __name__ == "__main__":
    main()
