# NewCosmos OCR API 镜像（Xiaomi-OCR-0 FastAPI 薄封装）
# 基础镜像走国内镜像源（192.168.0.2 直连 docker.io 超时；1ms.run 源实测可用）
FROM docker.1ms.run/library/python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -i https://mirrors.aliyun.com/pypi/simple/ -r requirements.txt

COPY ocr_api.py .

# 8788 与现有容器端口无冲突（实测占用：22/80/443/888/3000/3001/3009/3180/3443/5140/5432/6099/6333/7979/8765/8790/9993/10000/46823）
EXPOSE 8788

CMD ["uvicorn", "ocr_api:app", "--host", "0.0.0.0", "--port", "8788"]
