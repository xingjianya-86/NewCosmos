# NewCosmos 内网 OCR 服务（Xiaomi-OCR-0）

内网部署的文档识别服务：App 上传照片 + 照片类别 → Xiaomi-OCR-0（0.8B 文档理解模型）
按类别抽取字段/文本行 → 客户端定向回填。**图片仅走内网（LAN/ZeroTier），不出外网。**

公开模型库：[xiaomi-research/Xiaomi-OCR-0](https://github.com/xiaomi-research/Xiaomi-OCR-0)（推理管线对齐说明见 `ocr_api.py` 文件头注释）。

## 部署（192.168.0.2，Debian 13 + Docker）

```bash
# 1. 上传目录到服务器（本机执行）
scp -r deploy/ocr root@192.168.0.2:/opt/newcosmos-ocr

# 2. 启动容器（首启后执行模型初始化，拉取 ≈1.8GB）
ssh root@192.168.0.2
cd /opt/newcosmos-ocr
docker compose up -d --build
bash setup_model.sh          # 拉模型 + 派生 num_ctx=8192 + 健康检查

# 3. 冒烟：真实身份证样例（在服务器上执行，样例图片自备）
bash smoke_test.sh /tmp/idcard_sample.jpg ID_CARD
```

端口绑定：`ocr-api` 只绑 `192.168.0.2`（LAN）与 `10.110.0.2`（ZeroTier）；`ollama` 不发布宿主端口，
仅容器内网可达。若 ZeroTier 网卡未就绪，容器重启策略会自动重试。

## 接口契约（客户端 `Constants/OcrFieldKeys.cs` 必须与此一致）

### `POST /ocr/recognize`（multipart: `file` + `category`）

| category | mode | fields（JSON） | lines |
|---|---|---|---|
| `ID_CARD` | kie | `name`, `id_card` | KIE 原文行 |
| `HOUSEHOLD_REGISTER` | kie | `head_name`, `hukou_address` | 同上 |
| `BANK_CARD` | kie | `bank_name`, `card_number` | 同上 |
| `MEDICAL_RECORD` | kie | `diagnosis`, `hospital`, `date` | 同上 |
| `DISABILITY_CERT` | kie | `cert_no`, `disability_type`, `disability_level` | 同上 |
| `INCOME_PROOF` | kie | `employer`, `amount`, `period` | 同上 |
| `PUBLICITY_CHECK` | text | `—` | 全文行 |
| `OTHER_CERT` | text | `—` | 全文行 |
| `PERSON_PHOTO` | **拒绝 400** | — | 纯照片无需识别 |

- KIE 空结果自动兜底一次全文识别（`mode=kie_fallback`），lines 交客户端兜底解析。
- 响应：`{ok, category, mode, fields, lines, raw, model, usage, elapsed_ms}`
- 错误：400 类别不识别 / 422 图片解码失败 / 413 图片过大（25MB）/ 502 模型不可用。

### `GET /health`

`{service, model, backend, model_available, error?}`——`service=ok` 且 `model_available=true` 为健康。

## 资源预算（宿主：i3-4170 4 线程 / 7.7G，另有 10 个业务容器）

| 项 | 限额 | 说明 |
|---|---|---|
| ollama | mem 3G | BF16 权重 1.6G + KV/缓冲；`KEEP_ALIVE=24h` 避免重载 |
| ocr-api | mem 300M | 纯转发 + PIL 预处理 |
| 识别延迟 | 5–15s/张 | CPU 推理；`OLLAMA_NUM_PARALLEL=1` 防抢业务容器的核 |

## 运维

```bash
docker logs -f nc-ocr-api        # API 日志（不含图片内容）
docker stats nc-ocr-ollama       # 内存/CPU 观测
docker exec nc-ocr-ollama ollama list   # 模型清单
docker compose down              # 停止（模型缓存保留在 volume）
```

升级模型：改 `ocr_api.py` / `Modelfile` 后 `docker compose up -d --build` + `bash setup_model.sh`。

## 安全边界

- 图片仅入内网；服务不鉴权（内网信任域，与 PostgreSQL 直连同信任级）。
- API 日志只记类别/耗时/状态，不落图片字节与识别文本（客户端侧同规则，§10）。
- 客户端密钥/地址配置：`config/network.ini` 的 `OcrServiceUrl`（见 `NetworkOptions.cs`）。
