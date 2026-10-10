-- 彩票模块数据库表
-- 执行方式: psql -h <host> -U new_cosmos -d new_cosmos -f create_tables.sql
--
-- 代码实际读写情况（2026-10 核对）：
--   nc_lottery_draws          ← 读写。开奖数据下载链路（fetch_history.py → LotteryDataService）落库；
--                               也是 LSTM 训练/预测的唯一数据源（lstm_entry.py 直读本表导出）。
--   nc_lottery_user_purchases ← 读写。预测/机选入库 + 开奖后验证（UserPurchaseService，奖级规则权威）。
--   nc_lottery_predictions    ✗ 已无任何代码读写（保留建表语句仅作历史记录，不 DROP 既有表）。
--   nc_lottery_statistics     ✗ 已无任何代码读写（统计 API 已删除，同上）。

-- 开奖记录表
CREATE TABLE IF NOT EXISTS nc_lottery_draws (
    id SERIAL PRIMARY KEY,
    lottery_type VARCHAR(10) NOT NULL,        -- 彩种类型: SSQ/DLT
    draw_number VARCHAR(20) NOT NULL,         -- 期号（如：2026100）
    draw_date DATE NOT NULL,                  -- 开奖日期
    red_numbers_json TEXT NOT NULL DEFAULT '[]', -- 红球/前区号码（JSON数组）
    blue_numbers_json TEXT NOT NULL DEFAULT '[]', -- 蓝球/后区号码（JSON数组）
    sales_amount DECIMAL(18,2) DEFAULT 0,     -- 销售额（元）
    pool_money DECIMAL(18,2) DEFAULT 0,       -- 奖池金额（元）
    prize_grades_json TEXT DEFAULT '[]',      -- 奖级明细（JSON）
    prize_description TEXT,                   -- 中奖详情文本
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(lottery_type, draw_number)
);

-- 预测记录表（已废弃：无代码读写，仅供历史追溯；不 DROP 既有表）
CREATE TABLE IF NOT EXISTS nc_lottery_predictions (
    id SERIAL PRIMARY KEY,
    lottery_type VARCHAR(10) NOT NULL,        -- 彩种类型
    draw_number VARCHAR(20) NOT NULL,         -- 预测目标期号
    red_numbers_json TEXT NOT NULL DEFAULT '[]', -- 预测红球（JSON数组）
    blue_numbers_json TEXT NOT NULL DEFAULT '[]', -- 预测蓝球（JSON数组）
    algorithm_version VARCHAR(50),            -- 算法版本标识
    algorithm_name VARCHAR(100),              -- 算法名称
    confidence_score DECIMAL(5,2) DEFAULT 0,  -- 置信度评分（0-100）
    is_hit_red BOOLEAN,                       -- 红球是否命中
    is_hit_blue BOOLEAN,                      -- 蓝球是否命中
    red_hit_count INTEGER,                    -- 红球命中个数
    blue_hit_count INTEGER,                   -- 蓝球命中个数
    prize_level INTEGER,                      -- 奖级（0=未中奖）
    prize_amount DECIMAL(12,2),               -- 奖金金额（元）
    analysis_json TEXT,                       -- 额外分析信息（JSON）
    created_at TIMESTAMP DEFAULT NOW()
);

-- 用户购彩记录表（预测/机选入库，开奖后验证命中）
CREATE TABLE IF NOT EXISTS nc_lottery_user_purchases (
    id SERIAL PRIMARY KEY,
    lottery_type VARCHAR(10) NOT NULL,        -- 彩种类型
    draw_number VARCHAR(20),                  -- 匹配的开奖期号（验证后写入）
    red_numbers TEXT NOT NULL DEFAULT '[]',   -- 购彩红球/前区（JSON数组）
    blue_numbers TEXT NOT NULL DEFAULT '[]',  -- 购彩蓝球/后区（JSON数组）
    algorithm VARCHAR(50),                    -- 生成算法名称
    purchase_date DATE NOT NULL DEFAULT CURRENT_DATE, -- 购彩日期
    is_verified BOOLEAN DEFAULT FALSE,        -- 是否已验证
    is_hit BOOLEAN DEFAULT FALSE,             -- 是否中奖
    hit_red_count INTEGER DEFAULT 0,          -- 红球命中个数
    hit_blue_count INTEGER DEFAULT 0,         -- 蓝球命中个数
    prize_level INTEGER DEFAULT 0,            -- 奖级（0=未中奖）
    prize_amount DECIMAL(12,2) DEFAULT 0,     -- 中奖金额（元，解析自开奖奖级明细）
    created_at TIMESTAMP DEFAULT NOW()
);

-- 统计缓存表（已废弃：统计 API 已删除，无代码读写；仅供历史追溯）
CREATE TABLE IF NOT EXISTS nc_lottery_statistics (
    id SERIAL PRIMARY KEY,
    lottery_type VARCHAR(10) NOT NULL,
    stat_type VARCHAR(50) NOT NULL,           -- frequency/hot/cold/missing
    number_value INTEGER NOT NULL,            -- 号码值
    period_count INTEGER,                     -- 统计期数范围
    frequency INTEGER DEFAULT 0,              -- 出现次数
    frequency_rate DECIMAL(5,2) DEFAULT 0,    -- 出现频率
    current_missing INTEGER DEFAULT 0,        -- 当前遗漏次数
    average_missing DECIMAL(8,2) DEFAULT 0,   -- 平均遗漏
    max_missing INTEGER DEFAULT 0,            -- 最大遗漏
    last_appear_period VARCHAR(20),           -- 最后出现期号
    recent_frequency INTEGER DEFAULT 0,       -- 近30期出现次数
    updated_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(lottery_type, stat_type, number_value, period_count)
);

-- 索引
CREATE INDEX IF NOT EXISTS idx_draws_type_date ON nc_lottery_draws(lottery_type, draw_date DESC);
CREATE INDEX IF NOT EXISTS idx_draws_type_number ON nc_lottery_draws(lottery_type, draw_number);
CREATE INDEX IF NOT EXISTS idx_predictions_type ON nc_lottery_predictions(lottery_type, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_predictions_draw ON nc_lottery_predictions(lottery_type, draw_number);
CREATE INDEX IF NOT EXISTS idx_statistics_type ON nc_lottery_statistics(lottery_type, stat_type);
CREATE INDEX IF NOT EXISTS idx_user_purchases_type_date ON nc_lottery_user_purchases(lottery_type, purchase_date DESC);
CREATE INDEX IF NOT EXISTS idx_user_purchases_unverified ON nc_lottery_user_purchases(lottery_type, is_verified) WHERE is_verified = false;

-- 注释
COMMENT ON TABLE nc_lottery_draws IS '彩票开奖记录表';
COMMENT ON TABLE nc_lottery_predictions IS '彩票预测记录表';
COMMENT ON TABLE nc_lottery_statistics IS '彩票统计缓存表';
COMMENT ON TABLE nc_lottery_user_purchases IS '用户购彩记录表';
