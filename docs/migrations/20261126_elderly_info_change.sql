-- 2026-11-26 普惠高龄年度认证：信息变更单 + 字段明细
-- 说明：Schema YAML 已同步维护 Resources\Schema\elderly_info_change\database.yaml；
--      本脚本用于存量库幂等补表/补索引，可重复执行。

CREATE TABLE IF NOT EXISTS nc_biz_elderly_info_change_records (
    id BIGSERIAL PRIMARY KEY,
    change_no VARCHAR(40) NOT NULL,
    campaign_id BIGINT,
    task_id BIGINT,
    application_id BIGINT,
    id_card VARCHAR(18) NOT NULL,
    name VARCHAR(50) NOT NULL,
    change_type VARCHAR(20) NOT NULL,
    reason VARCHAR(500),
    status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    source_type VARCHAR(30),
    source_file VARCHAR(200),
    source_row INTEGER,
    effective_month VARCHAR(7),
    applied_at TIMESTAMP,
    applied_by VARCHAR(50),
    reviewed_at TIMESTAMP,
    reviewed_by VARCHAR(50),
    review_opinion VARCHAR(500),
    created_at TIMESTAMP DEFAULT NOW(),
    created_by VARCHAR(50),
    updated_at TIMESTAMP DEFAULT NOW(),
    deleted_at TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_elderly_info_change_no
    ON nc_biz_elderly_info_change_records (change_no);
CREATE INDEX IF NOT EXISTS idx_elderly_info_change_idcard
    ON nc_biz_elderly_info_change_records (id_card);
CREATE INDEX IF NOT EXISTS idx_elderly_info_change_campaign_status
    ON nc_biz_elderly_info_change_records (campaign_id, status);
CREATE INDEX IF NOT EXISTS idx_elderly_info_change_application
    ON nc_biz_elderly_info_change_records (application_id);
CREATE INDEX IF NOT EXISTS idx_elderly_info_change_task
    ON nc_biz_elderly_info_change_records (task_id);

CREATE TABLE IF NOT EXISTS nc_biz_elderly_info_change_details (
    id BIGSERIAL PRIMARY KEY,
    change_id BIGINT NOT NULL,
    field_key VARCHAR(50) NOT NULL,
    field_label VARCHAR(100),
    old_value TEXT,
    new_value TEXT,
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_elderly_info_change_detail_change
    ON nc_biz_elderly_info_change_details (change_id);
