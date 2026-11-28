-- 2026-11-26 普惠高龄年度认证：批次、名单快照、认证结果、凭证、导入批次与行
-- 说明：Schema YAML 已同步维护 Resources\Schema\elderly_certification\database.yaml；
--      本脚本用于存量库幂等补表/补索引，可重复执行。

CREATE TABLE IF NOT EXISTS nc_biz_elderly_cert_campaigns (
    id BIGSERIAL PRIMARY KEY,
    campaign_no VARCHAR(30) NOT NULL,
    year INTEGER NOT NULL,
    round INTEGER NOT NULL DEFAULT 1,
    name VARCHAR(100) NOT NULL,
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    valid_until DATE,
    status VARCHAR(20) NOT NULL DEFAULT 'Draft',
    require_evidence BOOLEAN NOT NULL DEFAULT TRUE,
    deadline_stop_offset INTEGER NOT NULL DEFAULT 1,
    roster_generated_at TIMESTAMP,
    closed_at TIMESTAMP,
    closed_by VARCHAR(50),
    remark VARCHAR(500),
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    created_by VARCHAR(50),
    updated_by VARCHAR(50),
    deleted_at TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_elderly_cert_campaign_no
    ON nc_biz_elderly_cert_campaigns (campaign_no) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_elderly_cert_campaign_year_round
    ON nc_biz_elderly_cert_campaigns (year, round) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_elderly_cert_campaign_status
    ON nc_biz_elderly_cert_campaigns (status);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_campaign_period
    ON nc_biz_elderly_cert_campaigns (period_end);

CREATE TABLE IF NOT EXISTS nc_biz_elderly_cert_tasks (
    id BIGSERIAL PRIMARY KEY,
    campaign_id BIGINT NOT NULL,
    id_card VARCHAR(18) NOT NULL,
    name VARCHAR(50) NOT NULL,
    application_id BIGINT,
    application_no VARCHAR(50),
    source_kind VARCHAR(20) NOT NULL DEFAULT 'Application',
    gender VARCHAR(10),
    birth_date DATE,
    age_snapshot INTEGER,
    category VARCHAR(20),
    identity_flag VARCHAR(20),
    issue_amount NUMERIC(12,2),
    hukou_city_id INTEGER,
    hukou_county_id INTEGER,
    hukou_town_id INTEGER,
    hukou_village_id INTEGER,
    hukou_village VARCHAR(100),
    hukou_address VARCHAR(200),
    family_city_id INTEGER,
    family_county_id INTEGER,
    family_town_id INTEGER,
    family_village_id INTEGER,
    family_address VARCHAR(200),
    phone VARCHAR(20),
    bank_name VARCHAR(100),
    bank_account VARCHAR(50),
    agent_name VARCHAR(50),
    agent_relation VARCHAR(50),
    snapshot_at TIMESTAMP NOT NULL DEFAULT NOW(),
    cert_status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    verify_method VARCHAR(20),
    verified_at DATE,
    verified_by VARCHAR(50),
    verifier_org VARCHAR(200),
    verifier_phone VARCHAR(20),
    result_note VARCHAR(500),
    death_date DATE,
    expected_evidence_count INTEGER NOT NULL DEFAULT 0,
    evidence_count INTEGER NOT NULL DEFAULT 0,
    evidence_status VARCHAR(20) NOT NULL DEFAULT 'None',
    stop_decision VARCHAR(20) NOT NULL DEFAULT 'None',
    stop_reason VARCHAR(50),
    stop_note VARCHAR(500),
    follow_up_date DATE,
    due_stop_date DATE,
    actual_stop_date DATE,
    stop_application_id BIGINT,
    stopped_at TIMESTAMP,
    source_batch_no VARCHAR(50),
    source_file VARCHAR(200),
    source_row INTEGER,
    source_submitted_at TIMESTAMP,
    last_imported_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    deleted_at TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_elderly_cert_task_campaign_idcard
    ON nc_biz_elderly_cert_tasks (campaign_id, id_card) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_elderly_cert_task_campaign_status
    ON nc_biz_elderly_cert_tasks (campaign_id, cert_status);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_task_campaign_region
    ON nc_biz_elderly_cert_tasks (campaign_id, hukou_town_id, hukou_village_id);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_task_idcard
    ON nc_biz_elderly_cert_tasks (id_card);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_task_application
    ON nc_biz_elderly_cert_tasks (application_id);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_task_stop
    ON nc_biz_elderly_cert_tasks (stop_decision);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_task_evidence
    ON nc_biz_elderly_cert_tasks (evidence_status);

CREATE TABLE IF NOT EXISTS nc_biz_elderly_cert_evidence (
    id BIGSERIAL PRIMARY KEY,
    task_id BIGINT,
    batch_id BIGINT,
    seq INTEGER NOT NULL DEFAULT 1,
    evidence_type VARCHAR(30) DEFAULT 'OTHER',
    original_name VARCHAR(300),
    stored_name VARCHAR(300),
    display_name VARCHAR(500),
    source_zip VARCHAR(300),
    source_entry VARCHAR(1000),
    match_status VARCHAR(20) NOT NULL DEFAULT 'Unmatched',
    match_method VARCHAR(20),
    photo_data BYTEA NOT NULL,
    image_format VARCHAR(16) NOT NULL DEFAULT 'jpg',
    file_size BIGINT NOT NULL DEFAULT 0,
    sha256 VARCHAR(64),
    created_at TIMESTAMP DEFAULT NOW(),
    created_by VARCHAR(50),
    deleted_at TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_elderly_cert_evidence_task_seq
    ON nc_biz_elderly_cert_evidence (task_id, seq)
    WHERE task_id IS NOT NULL AND deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_elderly_cert_evidence_task_sha
    ON nc_biz_elderly_cert_evidence (task_id, sha256)
    WHERE task_id IS NOT NULL AND sha256 IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_elderly_cert_evidence_batch
    ON nc_biz_elderly_cert_evidence (batch_id);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_evidence_sha
    ON nc_biz_elderly_cert_evidence (sha256);

CREATE TABLE IF NOT EXISTS nc_biz_elderly_cert_import_batches (
    id BIGSERIAL PRIMARY KEY,
    campaign_id BIGINT NOT NULL,
    batch_no VARCHAR(40) NOT NULL,
    file_name VARCHAR(300),
    file_hash VARCHAR(64),
    file_size BIGINT,
    zip_name VARCHAR(300),
    zip_hash VARCHAR(64),
    zip_size BIGINT,
    total_rows INTEGER NOT NULL DEFAULT 0,
    matched_rows INTEGER NOT NULL DEFAULT 0,
    unknown_rows INTEGER NOT NULL DEFAULT 0,
    duplicate_rows INTEGER NOT NULL DEFAULT 0,
    name_mismatch_rows INTEGER NOT NULL DEFAULT 0,
    invalid_rows INTEGER NOT NULL DEFAULT 0,
    evidence_total INTEGER NOT NULL DEFAULT 0,
    evidence_matched INTEGER NOT NULL DEFAULT 0,
    evidence_missing INTEGER NOT NULL DEFAULT 0,
    evidence_unmatched INTEGER NOT NULL DEFAULT 0,
    applied_rows INTEGER NOT NULL DEFAULT 0,
    skipped_rows INTEGER NOT NULL DEFAULT 0,
    error_rows INTEGER NOT NULL DEFAULT 0,
    status VARCHAR(20) NOT NULL DEFAULT 'Previewed',
    applied_at TIMESTAMP,
    applied_by VARCHAR(50),
    remark VARCHAR(500),
    created_at TIMESTAMP DEFAULT NOW(),
    created_by VARCHAR(50)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_elderly_cert_import_batch_no
    ON nc_biz_elderly_cert_import_batches (batch_no);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_import_batch_campaign
    ON nc_biz_elderly_cert_import_batches (campaign_id, status);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_import_batch_hash
    ON nc_biz_elderly_cert_import_batches (file_hash);

CREATE TABLE IF NOT EXISTS nc_biz_elderly_cert_import_rows (
    id BIGSERIAL PRIMARY KEY,
    batch_id BIGINT NOT NULL,
    row_no INTEGER NOT NULL,
    raw_name VARCHAR(100),
    raw_id_card VARCHAR(30),
    raw_phone VARCHAR(30),
    raw_village VARCHAR(200),
    raw_method VARCHAR(50),
    raw_result VARCHAR(50),
    raw_verified_at VARCHAR(50),
    raw_submitted_at VARCHAR(50),
    raw_remark VARCHAR(1000),
    raw_file_names TEXT,
    raw_json TEXT,
    id_card VARCHAR(18),
    name VARCHAR(50),
    cert_status VARCHAR(20),
    verify_method VARCHAR(20),
    verified_at DATE,
    expected_evidence_count INTEGER NOT NULL DEFAULT 0,
    matched_evidence_count INTEGER NOT NULL DEFAULT 0,
    evidence_status VARCHAR(20) NOT NULL DEFAULT 'None',
    task_id BIGINT,
    match_status VARCHAR(30) NOT NULL DEFAULT 'Unknown',
    error_message VARCHAR(500),
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_elderly_cert_import_row_batch
    ON nc_biz_elderly_cert_import_rows (batch_id);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_import_row_idcard
    ON nc_biz_elderly_cert_import_rows (id_card);
CREATE INDEX IF NOT EXISTS idx_elderly_cert_import_row_status
    ON nc_biz_elderly_cert_import_rows (match_status);
