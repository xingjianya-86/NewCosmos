#!/usr/bin/env python3
"""
彩票 ML 模型训练脚本
从 PostgreSQL 读取历史数据，使用 LSTM 网络训练预测模型
"""

import json
import os
import sys
import argparse
import numpy as np
from datetime import datetime
import tensorflow as tf


def load_history_from_db(db_host: str, db_port: int, db_name: str, db_user: str, db_password: str, lottery_type: str) -> list:
    """从 PostgreSQL 加载历史数据"""
    try:
        import psycopg2
    except ImportError:
        print("[ERROR] psycopg2 未安装，无法连接数据库", file=sys.stderr)
        return []

    try:
        conn = psycopg2.connect(
            host=db_host, port=db_port, dbname=db_name,
            user=db_user, password=db_password
        )
        cur = conn.cursor()

        # 查询开奖数据
        cur.execute("""
            SELECT red_numbers_json, blue_numbers_json, draw_number, draw_date
            FROM nc_lottery_draws
            WHERE lottery_type = %s
            ORDER BY draw_date ASC
        """, (lottery_type.upper(),))

        history = []
        for row in cur.fetchall():
            reds = json.loads(row[0])
            blues = json.loads(row[1])
            history.append({
                "red_numbers": reds,
                "blue_numbers": blues,
                "draw_number": row[2],
                "draw_date": str(row[3])
            })

        cur.close()
        conn.close()
        print(f"  从数据库加载 {len(history)} 期历史数据")
        return history

    except Exception as e:
        print(f"[ERROR] 数据库连接失败: {e}", file=sys.stderr)
        return []


def load_negative_samples(db_host: str, db_port: int, db_name: str, db_user: str, db_password: str, lottery_type: str) -> list:
    """从用户购彩记录中加载未命中的记录作为负样本"""
    try:
        import psycopg2
    except ImportError:
        return []

    try:
        conn = psycopg2.connect(
            host=db_host, port=db_port, dbname=db_name,
            user=db_user, password=db_password
        )
        cur = conn.cursor()
        cur.execute("""
            SELECT red_numbers, blue_numbers
            FROM nc_lottery_user_purchases
            WHERE lottery_type = %s
              AND is_verified = TRUE
              AND is_hit = FALSE
            ORDER BY created_at DESC
            LIMIT 500
        """, (lottery_type.upper(),))

        negatives = []
        for row in cur.fetchall():
            negatives.append({
                "red_numbers": json.loads(row[0]),
                "blue_numbers": json.loads(row[1]),
                "is_negative": True
            })

        cur.close()
        conn.close()
        if negatives:
            print(f"  加载 {len(negatives)} 条负样本（未命中的购彩记录）")
        return negatives

    except Exception as e:
        print(f"[WARN] 加载负样本失败: {e}", file=sys.stderr)
        return []


def prepare_data(history: list, config: dict, seq_length: int = 30, negative_samples: list = None):
    """准备训练数据（含负样本排斥学习）"""
    red_max = config["red_max"]
    blue_max = config["blue_max"]

    # 编码每期数据为 one-hot 向量
    encoded = []
    for draw in history:
        red_vec = [0] * red_max
        for num in draw.get("red_numbers", []):
            if 1 <= num <= red_max:
                red_vec[num - 1] = 1
        blue_vec = [0] * blue_max
        for num in draw.get("blue_numbers", []):
            if 1 <= num <= blue_max:
                blue_vec[num - 1] = 1
        encoded.append(red_vec + blue_vec)

    # 创建序列数据（正样本）
    X, y = [], []
    for i in range(len(encoded) - seq_length):
        X.append(encoded[i:i + seq_length])
        y.append(encoded[i + seq_length])

    # 添加负样本：将未命中的号码组合编码后作为训练数据
    # 负样本的标签设为全0（告诉模型"不要预测这些组合"）
    if negative_samples:
        neg_encoded = []
        for ns in negative_samples:
            red_vec = [0] * red_max
            for num in ns.get("red_numbers", []):
                if 1 <= num <= red_max:
                    red_vec[num - 1] = 1
            blue_vec = [0] * blue_max
            for num in ns.get("blue_numbers", []):
                if 1 <= num <= blue_max:
                    blue_vec[num - 1] = 1
            neg_encoded.append(red_vec + blue_vec)

        # 用最后 seq_length 个正样本序列作为上下文，附加负样本
        if len(encoded) >= seq_length:
            context = encoded[-seq_length:]
            for neg in neg_encoded:
                X.append(context)
                y.append([0] * (red_max + blue_max))  # 负样本标签全0
                context = context[1:] + [neg]  # 滑动窗口

        print(f"  负样本: {len(negative_samples)} 条（权重1x）")

    return np.array(X), np.array(y)


def build_model(seq_length: int, feature_size: int):
    """构建 LSTM 模型"""
    from tensorflow import keras

    model = keras.Sequential([
        keras.layers.Input(shape=(seq_length, feature_size)),
        keras.layers.LSTM(128, return_sequences=True),
        keras.layers.Dropout(0.2),
        keras.layers.LSTM(64),
        keras.layers.Dropout(0.2),
        keras.layers.Dense(128, activation='relu'),
        keras.layers.Dense(feature_size, activation='sigmoid')
    ])

    model.compile(
        optimizer='adam',
        loss='binary_crossentropy',
        metrics=['accuracy']
    )

    return model


def train_model(lottery_type: str, db_host: str, db_port: int, db_name: str,
                db_user: str, db_password: str, model_dir: str,
                epochs: int = 50, batch_size: int = 32, use_negative: bool = False):
    """训练模型"""
    configs = {
        "ssq": {
            "name": "双色球",
            "red_count": 6, "blue_count": 1,
            "red_max": 33, "blue_max": 16
        },
        "dlt": {
            "name": "大乐透",
            "red_count": 5, "blue_count": 2,
            "red_max": 35, "blue_max": 12
        }
    }

    config = configs.get(lottery_type)
    if not config:
        print(f"[ERROR] 不支持的彩种: {lottery_type}", file=sys.stderr)
        return False

    print(f"=== {config['name']} ML 模型训练 ===")
    print(f"彩种: {config['name']} ({lottery_type})")
    print(f"红球: {config['red_count']} 个 (1-{config['red_max']})")
    print(f"蓝球: {config['blue_count']} 个 (1-{config['blue_max']})")

    # 从数据库加载历史数据
    print(f"\n[1/4] 从数据库加载历史数据...")
    history = load_history_from_db(db_host, db_port, db_name, db_user, db_password, lottery_type)
    if not history:
        print("[ERROR] 无历史数据，无法训练", file=sys.stderr)
        return False

    # 准备训练数据
    print(f"\n[2/4] 准备训练数据...")
    seq_length = min(30, len(history) // 3)
    if seq_length < 5:
        print("[ERROR] 历史数据不足（至少需要 15 期）", file=sys.stderr)
        return False

    # 加载负样本（如果启用）
    negative_samples = None
    if use_negative:
        print(f"\n[2.5/4] 加载负样本...")
        negative_samples = load_negative_samples(db_host, db_port, db_name, db_user, db_password, lottery_type)

    X, y = prepare_data(history, config, seq_length, negative_samples)
    print(f"  序列长度: {seq_length}")
    print(f"  训练样本: {len(X)} 条")
    print(f"  特征维度: {X.shape[2]}")

    # 划分训练集/验证集
    split = int(len(X) * 0.85)
    X_train, X_val = X[:split], X[split:]
    y_train, y_val = y[:split], y[split:]
    print(f"  训练集: {len(X_train)} 条")
    print(f"  验证集: {len(X_val)} 条")

    # 构建模型
    print(f"\n[3/4] 构建 LSTM 模型...")
    model = build_model(seq_length, X.shape[2])
    model.summary()

    # 训练
    print(f"\n[4/4] 开始训练 (epochs={epochs}, batch_size={batch_size})...")
    history_train = model.fit(
        X_train, y_train,
        validation_data=(X_val, y_val),
        epochs=epochs,
        batch_size=batch_size,
        verbose=1,
        callbacks=[
            tf.keras.callbacks.EarlyStopping(
                monitor='val_loss',
                patience=5,
                restore_best_weights=True
            )
        ]
    )

    # 保存模型
    os.makedirs(model_dir, exist_ok=True)
    model_path = os.path.join(model_dir, f"{lottery_type}_model.keras")
    model.save(model_path)
    print(f"\n模型已保存: {model_path}")

    # 保存训练信息
    info = {
        "lottery_type": lottery_type,
        "train_date": datetime.now().isoformat(),
        "total_records": len(history),
        "seq_length": seq_length,
        "epochs_run": len(history_train.history['loss']),
        "final_loss": float(history_train.history['loss'][-1]),
        "final_val_loss": float(history_train.history['val_loss'][-1]),
        "final_accuracy": float(history_train.history['accuracy'][-1]),
        "config": config
    }
    info_path = os.path.join(model_dir, f"{lottery_type}_train_info.json")
    with open(info_path, 'w', encoding='utf-8') as f:
        json.dump(info, f, ensure_ascii=False, indent=2)
    print(f"训练信息已保存: {info_path}")

    print(f"\n=== 训练完成 ===")
    print(f"最终损失: {info['final_loss']:.4f}")
    print(f"最终准确率: {info['final_accuracy']:.4f}")
    return True


def main():
    parser = argparse.ArgumentParser(description="彩票 ML 模型训练（从 PostgreSQL 读取数据）")
    parser.add_argument("--type", choices=["ssq", "dlt", "all"], default="all", help="彩种")
    parser.add_argument("--db-host", default="192.168.0.2", help="数据库主机")
    parser.add_argument("--db-port", type=int, default=5432, help="数据库端口")
    parser.add_argument("--db-name", default="new_cosmos", help="数据库名称")
    parser.add_argument("--db-user", default="new_cosmos", help="数据库用户")
    parser.add_argument("--db-password", default=None, help="数据库密码（缺省读环境变量 PGPASSWORD）")
    parser.add_argument("--model-dir", default="./models", help="模型保存目录")
    parser.add_argument("--epochs", type=int, default=50, help="训练轮次")
    parser.add_argument("--batch-size", type=int, default=32, help="批大小")
    parser.add_argument("--use-user-purchases", action="store_true", help="使用用户购彩未命中记录作为负样本")
    args = parser.parse_args()

    db_password = args.db_password or os.environ.get("PGPASSWORD") or os.environ.get("NEWCOSMOS_DB_PASSWORD")
    if not db_password:
        print("[ERROR] 缺少数据库密码：请设置 PGPASSWORD 环境变量或用 --db-password 传入", file=sys.stderr)
        sys.exit(1)

    types = ["ssq", "dlt"] if args.type == "all" else [args.type]
    success = True
    for t in types:
        if not train_model(t, args.db_host, args.db_port, args.db_name,
                          args.db_user, db_password, args.model_dir,
                          args.epochs, args.batch_size, args.use_user_purchases):
            success = False

    if not success:
        print("[ERROR] 训练失败", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
