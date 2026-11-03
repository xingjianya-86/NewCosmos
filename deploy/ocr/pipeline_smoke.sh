#!/usr/bin/env bash
# OCR 管线冒烟：合成图跑 ID_CARD + BANK_CARD 两类，看 fields 抽取与耗时。
set -euo pipefail
URL="${OCR_URL:-http://192.168.0.2:8788}"

# 测试脚本进容器（Dockerfile 未含测试资产）
docker cp /opt/newcosmos-ocr/make_test_image.py nc-ocr-api:/app/make_test_image.py
docker exec nc-ocr-api python /app/make_test_image.py
# 图生成在容器内，拷到宿主供 curl 上传
docker cp nc-ocr-api:/tmp/test_id.png /tmp/test_id.png
docker cp nc-ocr-api:/tmp/test_bank.png /tmp/test_bank.png

echo "== health =="
curl -sf "$URL/health" && echo

echo "== recognize ID_CARD =="
curl -sS -w "\n-- 耗时: %{time_total}s --\n" -F "file=@/tmp/test_id.png" -F "category=ID_CARD" "$URL/ocr/recognize"
echo

echo "== recognize BANK_CARD (纯银行卡图) =="
curl -sS -w "\n-- 耗时: %{time_total}s --\n" -F "file=@/tmp/test_bank.png" -F "category=BANK_CARD" "$URL/ocr/recognize"
echo

echo "== recognize PERSON_PHOTO (应拒绝 400) =="
curl -sS -o /dev/null -w "HTTP %{http_code}\n" -F "file=@/tmp/test_id.png" -F "category=PERSON_PHOTO" "$URL/ocr/recognize"
