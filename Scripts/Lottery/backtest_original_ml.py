# -*- coding: utf-8 -*-
r"""
原版机器学习（TensorFlow LSTM）回测：
  A1 = 完全复刻原版行为（加载顺序 ASC → 特征取最旧 30 期 → 固定一注）
  A2 = 原版模型 + 修复排序（每期用其前最近 30 期，按时间顺序）

与 backtest.py 同口径：tests/window 一致，输出均红/均蓝/中奖率/ROI + 相对随机基线。

用法（A2，项目环境）：
    $env:PGPASSWORD='...'
    python backtest_original_ml.py --type ssq --variant a2 --script-dir . --models-dir .\models
用法（A1，publish 原始环境）：
    publish\win-x64\python-embed\python.exe backtest_original_ml.py --type ssq --variant a1 \
        --script-dir <publish\...\Scripts\Lottery> --models-dir <publish\...\Scripts\Lottery\models>
"""
import os
import sys
import json
import math
import argparse

import psycopg2

TICKET_COST = 2.0
PRIZE_AMOUNT = {1: 5_000_000, 2: 200_000, 3: 3_000, 4: 200, 5: 10, 6: 5, 0: 0}


def prize_level(type_code, rh, bh):
    if type_code == 6:  # ssq
        if rh == 6 and bh == 1: return 1
        if rh == 6: return 2
        if rh == 5 and bh == 1: return 3
        if rh == 5 or (rh == 4 and bh == 1): return 4
        if rh == 4 or (rh == 3 and bh == 1): return 5
        if bh == 1: return 6
        return 0
    # dlt
    if rh == 5 and bh == 2: return 1
    if rh == 5 and bh == 1: return 2
    if rh == 5: return 3
    if rh == 4 and bh == 2: return 4
    if rh == 4 and bh == 1: return 5
    if rh == 3 and bh == 2: return 5
    return 0


def load_draws(lottery_type, host, port, name, user, pwd):
    conn = psycopg2.connect(host=host, port=port, dbname=name, user=user, password=pwd)
    cur = conn.cursor()
    cur.execute("SELECT red_numbers_json, blue_numbers_json FROM nc_lottery_draws "
                "WHERE lottery_type=%s ORDER BY draw_date DESC", (lottery_type.upper(),))
    rows = cur.fetchall()
    cur.close(); conn.close()
    return [{"red_numbers": json.loads(r[0]), "blue_numbers": json.loads(r[1])} for r in rows]


def evaluate(tickets, draws, tests, type_code):
    red_hits, blue_hits, levels = [], [], []
    cost = 0.0; prize = 0.0
    for i in range(tests):
        actual_r = set(draws[i]["red_numbers"]); actual_b = set(draws[i]["blue_numbers"])
        for t in tickets:
            rh = len(set(t["red_numbers"]) & actual_r)
            bh = len(set(t["blue_numbers"]) & actual_b)
            lvl = prize_level(type_code, rh, bh)
            red_hits.append(rh); blue_hits.append(bh); levels.append(lvl)
            cost += TICKET_COST; prize += PRIZE_AMOUNT.get(lvl, 0)
    m = len(red_hits)
    return {
        "samples": m,
        "avg_red_hits": round(sum(red_hits) / m, 4),
        "avg_blue_hits": round(sum(blue_hits) / m, 4),
        "any_prize_rate": round(sum(1 for l in levels if l > 0) / m, 5),
        "cost": round(cost, 2), "prize": round(prize, 2),
        "roi": round(prize / cost, 5) if cost else 0.0,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--type", choices=["ssq", "dlt"], default="ssq")
    ap.add_argument("--variant", choices=["a1", "a2"], required=True)
    ap.add_argument("--tests", type=int, default=300)
    ap.add_argument("--script-dir", required=True)
    ap.add_argument("--models-dir", required=True)
    ap.add_argument("--output", default=None)
    ap.add_argument("--db-host", default="192.168.0.2")
    ap.add_argument("--db-port", type=int, default=5432)
    ap.add_argument("--db-name", default="new_cosmos")
    ap.add_argument("--db-user", default="new_cosmos")
    ap.add_argument("--db-password", default=None)
    args = ap.parse_args()

    pwd = args.db_password or os.environ.get("PGPASSWORD") or os.environ.get("NEWCOSMOS_DB_PASSWORD")
    sys.path.insert(0, os.path.abspath(args.script_dir))
    import predict_lottery as pl

    draws = load_draws(args.type, args.db_host, args.db_port, args.db_name, args.db_user, pwd)
    if len(draws) < args.tests + 30:
        raise SystemExit("历史数据不足")

    mlp = pl.MLPredictor(args.type)
    if not mlp.load_model(args.models_dir):
        raise SystemExit("模型加载失败")

    if args.variant == "a1":
        # 复刻原版：ASC 加载 → 特征取最旧 30 期 → 固定一注
        asc = list(reversed(draws))
        ticket = mlp.predict(1, asc[:30], min_confidence=0)[0]
        tickets = [ticket]
        note = "A1 复刻原版（ASC → 最旧30期 → 固定一注）"
    else:
        # A2 修复排序：每期用其前最近 30 期（时间顺序）
        tickets = None
        note = "A2 修复排序（每期用最近30期，时间顺序）"

    if args.variant == "a1":
        result = evaluate(tickets, draws, args.tests, 6 if args.type == "ssq" else 5)
    else:
        red_hits, blue_hits, levels = [], [], []
        cost = 0.0; prize = 0.0
        for i in range(args.tests):
            hist = list(reversed(draws[i + 1: i + 1 + 30]))
            preds = mlp.predict(1, hist, min_confidence=0)
            if not preds:
                continue
            actual_r = set(draws[i]["red_numbers"]); actual_b = set(draws[i]["blue_numbers"])
            rh = len(set(preds[0]["red_numbers"]) & actual_r)
            bh = len(set(preds[0]["blue_numbers"]) & actual_b)
            lvl = prize_level(6 if args.type == "ssq" else 5, rh, bh)
            red_hits.append(rh); blue_hits.append(bh); levels.append(lvl)
            cost += TICKET_COST; prize += PRIZE_AMOUNT.get(lvl, 0)
            if (i + 1) % 50 == 0:
                print(f"  [A2] {i+1}/{args.tests}", file=sys.stderr)
        m = len(red_hits)
        result = {"samples": m,
                  "avg_red_hits": round(sum(red_hits) / m, 4),
                  "avg_blue_hits": round(sum(blue_hits) / m, 4),
                  "any_prize_rate": round(sum(1 for l in levels if l > 0) / m, 5),
                  "cost": round(cost, 2), "prize": round(prize, 2),
                  "roi": round(prize / cost, 5) if cost else 0.0}

    rc = 6 if args.type == "ssq" else 5
    rm = 33 if args.type == "ssq" else 35
    bc = 1 if args.type == "ssq" else 2
    bm = 16 if args.type == "ssq" else 12
    result["baseline_red"] = round(rc * rc / rm, 4)
    result["baseline_blue"] = round(bc * bc / bm, 4)
    result["variant"] = args.variant
    result["note"] = note

    print(f"\n[{args.type.upper()} / {args.variant}] {note}")
    print(f"  均红={result['avg_red_hits']} (基线 {result['baseline_red']})  均蓝={result['avg_blue_hits']} (基线 {result['baseline_blue']})")
    print(f"  中奖率={result['any_prize_rate']*100:.2f}%  ROI={result['roi']}  样本={result['samples']}")

    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        print(f"  报告已保存: {args.output}")


if __name__ == "__main__":
    main()
