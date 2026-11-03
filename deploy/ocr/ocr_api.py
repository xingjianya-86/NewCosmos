#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""NewCosmos 内网 OCR 识别服务（Xiaomi-OCR-0 封装）。

职责：接收 App 上传的图片 + 照片类别，按类别走 KIE 字段抽取或全文识别，
返回 {fields, lines} 供客户端定向回填。

⚠️ 推理管线对齐说明（改此文件前必读）：
图像预处理、提示词、解码参数**逐项复刻官方管线**，缺一不可（官方 README 明确：
只拉模型不配这些 = 不可用/精度漂移）：
  - 源：github.com/xiaomi-research/Xiaomi-OCR-0
    · skills/xiaomi-ocr/mcp_ocr_server.py → _image_png / _chat / _schema_prompt
    · evaluation_pipeline/cropping.py     → fit_image（MAX_IMAGE_EDGE=2048 LANCZOS）
  - 图像：PIL → RGB → thumbnail(2048,2048,LANCZOS) → PNG → base64 data-url
  - 解码：temperature=0, top_p=0.8, presence_penalty=0.0, repetition_penalty=1.0,
          chat_template_kwargs={"enable_thinking": False}
  - KIE 提示词：官方 _schema_prompt 逐字复制（英文模板 + 字段 schema JSON）
  - JSON 提取：正则 \\{[\\s\\S]*\\}（官方 kie_image 同款）

接口：
  GET  /health                  → 服务与模型状态
  POST /ocr/recognize           → multipart: file + category
      category ∈ PhotoCategoryConstants 9 类（PERSON_PHOTO 不识别，服务端拒绝）
      响应 {ok, category, mode, fields, lines, raw, model, usage, elapsed_ms}
"""
from __future__ import annotations

import asyncio
import base64
import io
import json
import os
import re
import time

import httpx
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from PIL import Image

# ── 服务配置（环境变量覆盖，容器注入）─────────────────────────────────────
OCR_BASE = os.environ.get("OLLAMA_URL", "http://ollama:11434/v1").strip().rstrip("/")
OCR_MODEL = os.environ.get("OLLAMA_MODEL", "longwayxu/xiaomi-ocr-0:bf16").strip()
MAX_TOKENS = int(os.environ.get("OCR_MAX_TOKENS", "4096"))
KEEP_ALIVE = os.environ.get("OCR_KEEP_ALIVE", "4h").strip()
REQUEST_TIMEOUT = float(os.environ.get("OCR_REQUEST_TIMEOUT_SECONDS", "300"))
MAX_IMAGE_BYTES = int(os.environ.get("OCR_MAX_IMAGE_BYTES", str(25 * 1024 * 1024)))
DEFAULT_MAX_TOKENS_KIE = int(os.environ.get("OCR_KIE_MAX_TOKENS", "1024"))

# ── 官方管线常量（勿改，改 = 与官方对齐基线漂移）─────────────────────────
MAX_IMAGE_EDGE = 2048

# 官方 mcp_ocr_server.py PROMPTS["document"] / PROMPTS["text"] 逐字复制
PROMPT_DOCUMENT = (
    "Extract all information from the main body of the document image and represent it "
    "in markdown format, ignoring headers and footers. Tables should be expressed in OTSL "
    "format, formulas in the document should be represented using LATEX format, and the "
    "parsing should be organized according to the reading order."
)
PROMPT_TEXT = "Extract the text in the image."

# 类别 → KIE 字段 schema（客户端 Constants\OcrFieldKeys.cs 必须与此一致）。
# 空列表 = 全文文本模式（lines）；不在表内 = 拒绝（如 PERSON_PHOTO 纯照片）。
CATEGORY_FIELDS: dict[str, list[str]] = {
    "ID_CARD": ["name", "id_card"],
    "HOUSEHOLD_REGISTER": ["head_name", "hukou_address"],
    "BANK_CARD": ["bank_name", "card_number"],
    "MEDICAL_RECORD": ["diagnosis", "hospital", "date"],
    "DISABILITY_CERT": ["cert_no", "disability_type", "disability_level"],
    "INCOME_PROOF": ["employer", "amount", "period"],
    "PUBLICITY_CHECK": [],
    "OTHER_CERT": [],
}
SKIP_CATEGORIES = {"PERSON_PHOTO"}


def fit_image(image: Image.Image, max_edge: int = MAX_IMAGE_EDGE) -> Image.Image:
    """官方 evaluation_pipeline/cropping.py fit_image 逐字复刻（2048 长边 LANCZOS 等比缩放）。"""
    if image.width > max_edge or image.height > max_edge:
        image.thumbnail((max_edge, max_edge), Image.Resampling.LANCZOS)
    return image


def image_png(raw: bytes) -> bytes:
    """官方 _image_png 复刻：PIL → RGB → fit_image → PNG 字节。"""
    try:
        image = Image.open(io.BytesIO(raw)).convert("RGB")
        fit_image(image)
        out = io.BytesIO()
        image.save(out, format="PNG")
        return out.getvalue()
    except Exception as exc:  # noqa: BLE001 —— 对齐官方：解码失败统一 RuntimeError
        raise RuntimeError(f"Cannot decode image: {exc}") from exc


def schema_prompt(fields: list[str]) -> str:
    """官方 mcp_ocr_server.py _schema_prompt 逐字复刻。"""
    if not fields:
        return ("Extract the key information from the document image as a JSON object. "
                "Use concise field names. Return JSON only; use an empty string for missing values.")
    schema = {str(field): "" for field in fields}
    return ("Extract key information in the image\n\n"
            "Please output the key information in JSON format according to the following schema:\n"
            + json.dumps(schema, ensure_ascii=False, indent=4))


async def chat(image_png_bytes: bytes, prompt: str, max_tokens: int) -> dict:
    """官方 _chat 复刻：同一解码参数 + enable_thinking=false + PNG data-url 单图输入。"""
    encoded = base64.b64encode(image_png_bytes).decode()
    payload = {
        "model": OCR_MODEL,
        "messages": [{"role": "user", "content": [
            {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{encoded}"}},
            {"type": "text", "text": prompt},
        ]}],
        "max_tokens": max_tokens,
        "temperature": 0,
        "top_p": 0.8,
        "presence_penalty": 0.0,
        "repetition_penalty": 1.0,
        "chat_template_kwargs": {"enable_thinking": False},
    }
    # Ollama 专有：保持模型常驻（BF16 1.8GB，重载一次约 10-20 秒）
    if KEEP_ALIVE:
        payload["keep_alive"] = KEEP_ALIVE

    async with httpx.AsyncClient(timeout=REQUEST_TIMEOUT) as client:
        try:
            resp = await client.post(f"{OCR_BASE}/chat/completions", json=payload)
        except httpx.HTTPError as exc:
            raise RuntimeError(f"Local model is unavailable at {OCR_BASE}: {exc}") from exc

    if resp.status_code != 200:
        body = resp.text[:500]
        if resp.status_code == 400 and "maximum context length" in body.lower():
            raise RuntimeError("The image/prompt plus requested output exceeds the model context. "
                               "Reduce max_tokens and retry.")
        raise RuntimeError(f"Local inference returned HTTP {resp.status_code}: {body}")

    data = resp.json()
    message = data["choices"][0]["message"]["content"]
    if isinstance(message, list):
        message = "".join(p.get("text", "") if isinstance(p, dict) else str(p) for p in message)
    text = str(message or "").strip()
    if not text:
        raise RuntimeError("Local model returned an empty response")
    if data["choices"][0].get("finish_reason") == "length":
        raise RuntimeError("Model output was truncated; increase the output budget or use smaller inputs")
    return {"text": text, "usage": data.get("usage") or {}}


def extract_json_fields(raw: str, fields: list[str]) -> dict[str, str]:
    """官方 kie_image 提取逻辑复刻：正则取 JSON 块 → 解析 → 按 schema 过滤。"""
    match = re.search(r"\{[\s\S]*\}", raw)
    if not match:
        return {}
    try:
        data = json.loads(match.group(0))
    except json.JSONDecodeError:
        return {}
    if not isinstance(data, dict):
        return {}
    return {field: str(data.get(field, "") or "") for field in fields}


app = FastAPI(title="newcosmos-ocr-api", docs_url=None, redoc_url=None)


@app.get("/health")
async def health() -> dict:
    """健康检查：Ollama 可达性 + 目标模型是否已加载（附推理服务版本）。"""
    status: dict = {"service": "ok", "model": OCR_MODEL, "backend": OCR_BASE}
    try:
        async with httpx.AsyncClient(timeout=5) as client:
            resp = await client.get(f"{OCR_BASE.rsplit('/v1', 1)[0]}/api/tags")
            tags = [m.get("name", "") for m in resp.json().get("models", [])]
            status["model_available"] = any(OCR_MODEL in t for t in tags)
    except Exception as exc:  # noqa: BLE001 —— 健康检查失败不抛 500，降级为状态字段
        status["service"] = "degraded"
        status["error"] = f"ollama unreachable: {exc}"
    return status


@app.post("/ocr/recognize")
async def recognize(file: UploadFile = File(...), category: str = Form(...)) -> dict:
    """按照片类别识别。KIE 类别返回 fields；文本类别返回 lines；PERSON_PHOTO 拒绝。"""
    started = time.monotonic()
    category = (category or "").strip().upper()
    if category in SKIP_CATEGORIES:
        raise HTTPException(status_code=400, detail=f"category {category} does not need recognition")
    if category not in CATEGORY_FIELDS:
        raise HTTPException(status_code=400, detail=f"unknown category: {category}")

    raw_bytes = await file.read()
    if not raw_bytes:
        raise HTTPException(status_code=422, detail="empty image file")
    if len(raw_bytes) > MAX_IMAGE_BYTES:
        raise HTTPException(status_code=413, detail=f"image exceeds {MAX_IMAGE_BYTES} bytes")

    try:
        png = image_png(raw_bytes)
    except RuntimeError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc

    fields_spec = CATEGORY_FIELDS[category]

    if fields_spec:
        # KIE 模式：官方 _schema_prompt + JSON 过滤
        try:
            result = await chat(png, schema_prompt(fields_spec), DEFAULT_MAX_TOKENS_KIE)
        except RuntimeError as exc:
            raise HTTPException(status_code=502, detail=str(exc)) from exc
        fields = extract_json_fields(result["text"], fields_spec)
        lines = [ln for ln in result["text"].splitlines() if ln.strip()]
        if not any(fields.values()):
            # KIE 空结果兜底：官方文本提示词再试一次，lines 交客户端 IdCardOcrParser 等兜底解析
            try:
                fallback = await chat(png, PROMPT_TEXT, MAX_TOKENS)
                lines = [ln for ln in fallback["text"].splitlines() if ln.strip()]
                return _response(category, "kie_fallback", fields, lines, fallback["text"],
                                 fallback.get("usage"), started)
            except RuntimeError:
                pass  # 兜底失败保留原 KIE 输出
        return _response(category, "kie", fields, lines, result["text"], result.get("usage"), started)

    # 文本模式（PUBLICITY_CHECK / OTHER_CERT）：官方文本提示词
    try:
        result = await chat(png, PROMPT_TEXT, MAX_TOKENS)
    except RuntimeError as exc:
        raise HTTPException(status_code=502, detail=str(exc)) from exc
    lines = [ln for ln in result["text"].splitlines() if ln.strip()]
    return _response(category, "text", {}, lines, result["text"], result.get("usage"), started)


def _response(category: str, mode: str, fields: dict, lines: list[str], raw: str,
              usage: dict | None, started: float) -> dict:
    return {
        "ok": True,
        "category": category,
        "mode": mode,
        "fields": fields,
        "lines": lines,
        "raw": raw,
        "model": OCR_MODEL,
        "usage": usage or {},
        "elapsed_ms": int((time.monotonic() - started) * 1000),
    }


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8788, workers=1)
