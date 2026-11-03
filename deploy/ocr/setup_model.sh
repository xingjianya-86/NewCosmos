#!/usr/bin/env bash
# NewCosmos OCR 模型初始化（在 docker-compose up -d 之后执行一次）
# 1) 拉取官方对齐验证过的 BF16 包（含视觉投影器，≈1.8GB）
# 2) 派生 nc-ocr:ctx8192（num_ctx=8192，Ollama 默认上下文不够整图 token 预算）
# 3) 冒烟验证 /health
set -euo pipefail
cd "$(dirname "$0")"

echo "==> 拉取模型（≈1.8GB，视网络 2-10 分钟）"
docker exec nc-ocr-ollama ollama pull longwayxu/xiaomi-ocr-0:bf16

echo "==> 派生 num_ctx=8192 变体"
# docker exec 必须 -i 才能接住 stdin 里的 Modelfile（漏 -i 会报 "no FROM line"）
docker exec -i nc-ocr-ollama ollama create nc-ocr:ctx8192 -f /dev/stdin < Modelfile

echo "==> 模型列表（应含 longwayxu/xiaomi-ocr-0:bf16 与 nc-ocr:ctx8192）"
docker exec nc-ocr-ollama ollama list

echo "==> 健康检查"
curl -sf http://127.0.0.1:8788/health && echo

echo "==> 完成。可执行 smoke_test.sh <样例图片> 验证识别精度"
