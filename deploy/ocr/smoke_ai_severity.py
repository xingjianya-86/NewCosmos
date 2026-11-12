#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""AI 重病判定冒烟（/ai/severity）。

回归集固化自 2026-11-11 补测（33 例）：FP 高危组、正例回归组、业务裁定组
（脑梗塞无"急性"不算 / 糖尿病未分型不算 / 癌症待查算）。改 prompt、改清单、
换模型后必须重跑本脚本，全 PASS 才算通过。

用法：
    python3 smoke_ai_severity.py [base_url]     # 默认 http://192.168.0.2:8788
    bash -c 'python3 smoke_ai_severity.py http://127.0.0.1:8788'
退出码：0=全过，1=有失败。LLM 冷启动首调约 35-40s，全量约 3-4 分钟。
"""
from __future__ import annotations

import json
import sys
import time
import urllib.error
import urllib.request

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://192.168.0.2:8788").rstrip("/")
TIMEOUT = 180  # 秒/请求（覆盖 LLM 冷启动 ~40s）

# (name, code, expected_verdict, expected_source 或 None=不校验)
# expected 支持 "|" 备选（LLM 在 unknown/not_severe 边界的安全方向抖动可接受）
CASES: list[tuple[str, str, str, str | None]] = [
    # ── 确定性层（精确/包含/编码段），应全部 source=deterministic ──
    ("急性心肌梗塞", "I21.9", "severe", "deterministic"),
    ("I型糖尿病", "E10.9", "severe", "deterministic"),
    ("乳腺癌", "", "severe", "deterministic"),
    ("右乳腺浸润性癌", "", "severe", "deterministic"),
    ("植物人", "", "severe", "deterministic"),
    ("股骨头缺血性坏死", "", "severe", "deterministic"),
    ("急性脑梗塞", "", "severe", "deterministic"),
    ("脑出血", "", "severe", "deterministic"),
    ("甲状腺乳头状癌", "", "severe", "deterministic"),
    ("海绵状血管瘤", "", "severe", "deterministic"),
    ("尿毒症，规律血液透析", "", "severe", "deterministic"),        # ⊃尿毒症
    ("慢性乙型肝炎肝硬化", "", "severe", "deterministic"),          # ⊃肝硬化
    ("甲状腺功能亢进（甲亢）", "", "severe", "deterministic"),      # ⊃甲亢
    ("癌症待查", "", "severe", "deterministic"),                    # 业务裁定：算（别名）
    ("恶性肿瘤待查", "", "severe", "deterministic"),                # ⊃恶性肿瘤
    # ── LLM 兜底（语义变体正例）──
    ("重度精神分裂症", "", "severe", "deterministic"),              # 别名→重症精神病（LLM 对该桥不稳定，走确定性）
    ("慢性肾功能不全4期", "", "severe", "deterministic"),           # 别名包含→慢性肾脏病（同上）
    ("肾功能不全", "N18.5", "severe", "deterministic"),            # 纯编码段路径：N18→慢性肾脏病
    # ── LLM 负例（FP 高危组，2026-11-11 v5 反向禁令修复后应全过）──
    ("2型糖尿病", "E11.9", "not_severe", None),
    ("陈旧性脑梗死", "", "not_severe", None),
    ("急性上呼吸道感染", "J06.9", "not_severe", None),
    ("慢性阻塞性肺疾病", "J44.9", "not_severe", None),
    ("甲状腺结节", "", "not_severe", None),
    ("高血压病3级", "", "not_severe", None),
    ("胆囊结石", "", "not_severe", None),
    ("胃溃疡", "", "not_severe", None),
    ("社区获得性肺炎", "", "not_severe", None),
    # ── 业务裁定组（2026-11-11 拍板）──
    ("脑梗塞", "", "not_severe", None),                             # 裁定1：无"急性"不算（一年恢复期）
    ("脑梗死", "I63.9", "not_severe", None),                       # 裁定1：I63 刻意不入编码段表
    ("糖尿病", "E11.9", "not_severe", None),                       # 裁定2：未分型不算（严格）
    ("癌症待查", "C80.9", "severe", "deterministic"),               # 裁定3：算（编码段 C00-C99 + 别名双保险）
    # ── 安全方向 unknown 可接受 ──
    ("腰痛", "M54.5", "not_severe|unknown", None),
    ("颈椎病", "", "not_severe|unknown", None),
]


def call(name: str, code: str) -> dict:
    body = json.dumps({"name": name, "code": code}, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        f"{BASE}/ai/severity", data=body,
        headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req, timeout=TIMEOUT) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main() -> int:
    print(f"== AI 重病判定冒烟 @ {BASE} ({len(CASES)} 例) ==")
    passed = failed = 0
    for name, code, want, want_src in CASES:
        t0 = time.monotonic()
        try:
            data = call(name, code)
        except (urllib.error.URLError, OSError, ValueError) as exc:
            failed += 1
            print(f"[FAIL] {name} {code} -> 请求失败: {exc}")
            continue
        ms = int((time.monotonic() - t0) * 1000)
        got = str(data.get("verdict", ""))
        ok = got in want.split("|")
        detail = f"verdict={got}"
        if want_src:
            src = str(data.get("source", ""))
            detail += f" source={src}"
            if src != want_src:
                ok = False
        if not str(data.get("policy_version", "")):
            ok = False
            detail += " policy_version=MISSING"
        if ok:
            passed += 1
            print(f"[PASS] {name} {code} -> {detail} ({ms}ms)"
                  + (f" matched={data.get('matched_item')}" if data.get("matched_item") else ""))
        else:
            failed += 1
            print(f"[FAIL] {name} {code} -> {detail} 期望={want}"
                  + (f"/{want_src}" if want_src else "")
                  + f" reason={str(data.get('reason', ''))[:80]} ({ms}ms)")
    print(f"== 结果: PASS {passed} / FAIL {failed} / 共 {len(CASES)} ==")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
