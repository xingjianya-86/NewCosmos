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
# ── AI 重病判定配置（POST /ai/severity，2026-11-11 P0）────────────────────
AI_MODEL = os.environ.get("AI_MODEL", "qwen2.5:3b").strip()
AI_MAX_TOKENS = int(os.environ.get("AI_MAX_TOKENS", "256"))
AI_KEEP_ALIVE = os.environ.get("AI_KEEP_ALIVE", "24h").strip()
AI_TIMEOUT = float(os.environ.get("AI_REQUEST_TIMEOUT_SECONDS", "90"))
AI_CATALOG_PATH = os.environ.get(
    "AI_CATALOG_PATH",
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "severe_disease_catalog.json"),
)


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
    # 身份证：正面姓名/证号 + 背面住址（2026-11-05 增加 address，客户端 OcrFieldKeys 同步）
    "ID_CARD": ["name", "id_card", "address"],
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

# ── AI 重病判定（P0）：清单加载 + 确定性层 + LLM 兜底 ─────────────────────
# 设计依据（2026-11-11 补测定稿）：
#   确定性层为主（毫秒级、离线、可审计），LLM 只兜语义变体；
#   只做单向包含（输入⊃清单项），禁止反向——业务裁定：脑梗塞无"急性"不算、
#   糖尿病未分型不算（反向包含会让这两个短输入误命中长清单项）。
_catalog_cache: dict = {"mtime": -1.0, "data": None, "policy_version": ""}


def get_catalog() -> dict:
    """加载重病清单（mtime 变化自动热载，改清单无需重启容器）。
    policy_version = 文件 sha256 前16位，客户端据此失效判定缓存。"""
    try:
        mtime = os.path.getmtime(AI_CATALOG_PATH)
    except OSError as exc:
        raise RuntimeError(f"severe disease catalog missing: {AI_CATALOG_PATH}") from exc
    if _catalog_cache["data"] is None or mtime != _catalog_cache["mtime"]:
        with open(AI_CATALOG_PATH, "rb") as fh:
            raw = fh.read()
        data = json.loads(raw.decode("utf-8"))
        _catalog_cache["mtime"] = mtime
        _catalog_cache["data"] = data
        _catalog_cache["policy_version"] = hashlib.sha256(raw).hexdigest()[:16]
    return _catalog_cache["data"]


_FULLWIDTH_MAP = str.maketrans({"（": "(", "）": ")", "：": ":", "，": ",", "－": "-", "　": ""})


def _norm_name(s: str) -> str:
    """名称规范化：去空白 + 全角转半角 + 小写（匹配专用，不改展示原文）。"""
    return re.sub(r"\s+", "", (s or "").strip()).translate(_FULLWIDTH_MAP).lower()


def deterministic_severity(name: str, code: str, catalog: dict) -> dict | None:
    """确定性层：① 精确（清单项/别名）② 单向包含（输入⊃清单项/别名，最长优先）
    ③ 编码段表。命中返回 {matched_item, reason}，未命中返回 None（交 LLM）。"""
    norm = _norm_name(name)
    if not norm:
        return None
    items = [str(x) for x in catalog.get("items", [])]
    aliases = {str(k): str(v) for k, v in (catalog.get("aliases") or {}).items()}

    for item in items:
        if norm == _norm_name(item):
            return {"matched_item": item, "reason": f"清单精确命中：{item}"}
    for alias, canonical in aliases.items():
        if norm == _norm_name(alias):
            return {"matched_item": canonical, "reason": f"清单别名命中：{alias}→{canonical}"}

    best_text, best_item, best_alias = "", "", ""
    for item in items:
        ni = _norm_name(item)
        if len(ni) >= 2 and ni in norm and len(ni) > len(best_text):
            best_text, best_item = ni, item
    for alias, canonical in aliases.items():
        na = _norm_name(alias)
        if len(na) >= 2 and na in norm and len(na) > len(best_text):
            best_text, best_item, best_alias = na, canonical, alias
    if best_item:
        reason = (f"清单别名包含命中：{best_alias}→{best_item}" if best_alias
                  else f"清单包含命中：{best_item}")
        return {"matched_item": best_item, "reason": reason}

    code_u = (code or "").strip().upper()
    m = re.match(r"^([A-Z])(\d{1,3})", code_u)
    if m:
        letter, num = m.group(1), int(m.group(2))
        for rng in catalog.get("code_ranges", []):
            if str(rng.get("letter", "")) == letter and int(rng["min"]) <= num <= int(rng["max"]):
                item = str(rng["item"])
                return {"matched_item": item, "reason": f"编码 {code_u} 命中 {item}"}
    return None


def build_severity_prompt(catalog: dict) -> str:
    """LLM 判定 prompt（2026-11-11 补测 v5 定稿：全目录 + 反向禁令 + 负例 few-shot）。"""
    items = "、".join(str(x) for x in catalog.get("items", []))
    return (
        "你是民政救助经办辅助助手。判断给定疾病是否属于重病。\n"
        f"重病病种目录（本地执行口径，以本清单为准）：{items}。\n"
        "规则：疾病属于清单任一项（含同义词、俗称、部位与术式变体）=severe；"
        "清单外=not_severe；无法判断=unknown。\n"
        "反向禁令（同样重要）：归入清单项必须语义相同，禁止把相近病种乱归——"
        "2型糖尿病不是I型糖尿病；陈旧性脑梗死不是急性脑梗塞；甲状腺结节不是甲状腺癌；"
        "普通肺炎不是耐多药肺结核。禁止用医学常识否决清单，也禁止因名字相似就归入清单。\n"
        '只输出一行纯JSON（禁止markdown代码块）：{"verdict":"severe|not_severe|unknown",'
        '"matched_item":"清单项或空字符串","confidence":0.0到1.0,"reason":"一句话依据"}\n'
        "示例：\n"
        '疾病：ICD-10编码 C16.9（胃恶性肿瘤） → {"verdict":"severe","matched_item":"胃癌",'
        '"confidence":0.95,"reason":"C16.9为胃恶性肿瘤，命中清单胃癌"}\n'
        '疾病：重度精神分裂症 → {"verdict":"severe","matched_item":"重症精神病",'
        '"confidence":0.95,"reason":"重度精神分裂症属重症精神病，命中清单"}\n'
        '疾病：慢性肾功能不全4期 → {"verdict":"severe","matched_item":"慢性肾脏病",'
        '"confidence":0.95,"reason":"慢性肾功能不全属慢性肾脏病，命中清单"}\n'
        '疾病：2型糖尿病 → {"verdict":"not_severe","matched_item":"","confidence":0.95,'
        '"reason":"清单仅含I型糖尿病，2型糖尿病不属清单"}\n'
        '疾病：陈旧性脑梗死 → {"verdict":"not_severe","matched_item":"","confidence":0.95,'
        '"reason":"清单仅含急性脑梗塞/急性脑梗死，陈旧性非急性期"}\n'
        '疾病：ICD-10编码 K21.9（胃食管反流病） → {"verdict":"not_severe","matched_item":"",'
        '"confidence":0.95,"reason":"胃食管反流病不在清单内"}\n'
        "疾病："
    )


async def chat_text(prompt: str, max_tokens: int) -> dict:
    """文本对话（/ai/severity 用）：temperature=0，无图、无视觉管线解码参数。"""
    payload: dict = {
        "model": AI_MODEL,
        "messages": [{"role": "user", "content": prompt}],
        "max_tokens": max_tokens,
        "temperature": 0,
    }
    if AI_KEEP_ALIVE:
        payload["keep_alive"] = AI_KEEP_ALIVE

    async with httpx.AsyncClient(timeout=AI_TIMEOUT) as client:
        try:
            resp = await client.post(f"{OCR_BASE}/chat/completions", json=payload)
        except httpx.HTTPError as exc:
            raise RuntimeError(f"Local model is unavailable at {OCR_BASE}: {exc}") from exc
    if resp.status_code != 200:
        raise RuntimeError(f"Local inference returned HTTP {resp.status_code}: {resp.text[:500]}")

    data = resp.json()
    message = data["choices"][0]["message"]["content"]
    if isinstance(message, list):
        message = "".join(p.get("text", "") if isinstance(p, dict) else str(p) for p in message)
    text = str(message or "").strip()
    if not text:
        raise RuntimeError("Local model returned an empty response")
    return {"text": text, "usage": data.get("usage") or {}}


def parse_severity(raw: str, catalog: dict) -> tuple[str, str, float, str]:
    """解析 LLM JSON 输出。坏格式降级 unknown（不猜）；matched_item 必须∈清单
    （含别名归一），否则置空——客户端凭 matched_item 非空才允许自动预勾选。"""
    verdict, matched, confidence, reason = "unknown", "", 0.0, ""
    match = re.search(r"\{[\s\S]*\}", raw)
    if match:
        try:
            data = json.loads(match.group(0))
        except json.JSONDecodeError:
            data = None
        if isinstance(data, dict):
            v = str(data.get("verdict", "")).strip().lower()
            if v in ("severe", "not_severe", "unknown"):
                verdict = v
            raw_item = str(data.get("matched_item", "")).strip()
            items = {str(x) for x in catalog.get("items", [])}
            aliases = {str(k): str(v) for k, v in (catalog.get("aliases") or {}).items()}
            if raw_item in items:
                matched = raw_item
            elif raw_item in aliases:
                matched = aliases[raw_item]
            try:
                confidence = max(0.0, min(1.0, float(data.get("confidence", 0))))
            except (TypeError, ValueError):
                confidence = 0.0
            reason = str(data.get("reason", "")).strip()[:300]
    if not reason:
        reason = "AI 输出无法解析，待人工判定"
    return verdict, matched, confidence, reason



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
class SeverityRequest(BaseModel):
    """重病判定请求：name 必填（客户端先用本地 ICD 字典把编码转成名称），code 可选作判别上下文。"""
    name: str = Field(default="", max_length=200)
    code: str = Field(default="", max_length=32)


@app.post("/ai/severity")
async def severity(req: SeverityRequest) -> dict:
    """重病判定：确定性层（清单精确/单向包含/编码段）→ 未命中走 LLM 兜底。
    auto_eligible = severe 且 matched_item∈清单——客户端仅在此为 true 时自动预勾选。
    响应带 policy_version（清单文件哈希），客户端据此失效本地判定缓存。"""
    started = time.monotonic()
    name = (req.name or "").strip()
    code = (req.code or "").strip()
    if not name:
        raise HTTPException(status_code=400,
                            detail="name is required (resolve ICD code to name client-side first)")
    try:
        catalog = get_catalog()
    except (RuntimeError, json.JSONDecodeError) as exc:
        raise HTTPException(status_code=500, detail=f"catalog unavailable: {exc}") from exc
    policy_version = str(_catalog_cache["policy_version"])

    det = deterministic_severity(name, code, catalog)
    if det:
        return {
            "ok": True, "verdict": "severe", "matched_item": det["matched_item"],
            "confidence": 1.0, "reason": det["reason"], "source": "deterministic",
            "auto_eligible": True, "policy_version": policy_version, "model": "",
            "usage": {}, "elapsed_ms": int((time.monotonic() - started) * 1000),
        }

    prompt = build_severity_prompt(catalog) + "\n" + name \
        + (f"（ICD-10：{code}）" if code else "") + "\n"
    try:
        result = await chat_text(prompt, AI_MAX_TOKENS)
    except RuntimeError as exc:
        # 显式失败：确定性层未命中且模型不可用，交客户端走"无建议"降级（§7 不吞异常）
        raise HTTPException(status_code=502, detail=str(exc)) from exc

    verdict, matched, confidence, reason = parse_severity(result["text"], catalog)
    return {
        "ok": True, "verdict": verdict, "matched_item": matched,
        "confidence": confidence, "reason": reason, "source": "llm",
        "auto_eligible": verdict == "severe" and bool(matched),
        "policy_version": policy_version, "model": AI_MODEL,
        "usage": result.get("usage") or {},
        "elapsed_ms": int((time.monotonic() - started) * 1000),
    }


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
