#!/usr/bin/env bash
# NewCosmos OCR 冒烟测试：逐类别跑样例图片，人工核对 fields/lines 精度。
# 用法: ./smoke_test.sh <图片路径> [类别]      类别默认 ID_CARD
# 类别: ID_CARD | HOUSEHOLD_REGISTER | BANK_CARD | MEDICAL_RECORD |
#       DISABILITY_CERT | INCOME_PROOF | PUBLICITY_CHECK | OTHER_CERT
set -euo pipefail

IMG="${1:?用法: ./smoke_test.sh <图片路径> [类别]}"
CAT="${2:-ID_CARD}"
URL="${OCR_URL:-http://127.0.0.1:8788}"

echo "== health =="
curl -sf "$URL/health" && echo

echo "== recognize ($CAT) =="
# -w 输出耗时（含模型预热）；-sS 隐藏进度但显示错误
curl -sS -w "\n-- 总耗时: %{time_total}s --\n" \
  -F "file=@$IMG" -F "category=$CAT" \
  "$URL/ocr/recognize"
echo
