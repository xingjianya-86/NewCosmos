-- 资产核查PDF报告存储表
CREATE TABLE IF NOT EXISTS nc_biz_asset_check_reports (
    id              BIGSERIAL       PRIMARY KEY,
    check_id        BIGINT          NOT NULL REFERENCES nc_biz_asset_checks(id),
    batch_id        VARCHAR(50)     NOT NULL,
    applicant_name  VARCHAR(100)    NOT NULL,
    applicant_id_card VARCHAR(18)   NOT NULL,
    report_file_name VARCHAR(500)   NOT NULL,
    report_data     BYTEA           NOT NULL,
    file_hash       VARCHAR(64)     NOT NULL,
    is_valid        BOOLEAN         NOT NULL DEFAULT true,
    report_date     DATE            NOT NULL DEFAULT CURRENT_DATE,
    notes           VARCHAR(500),
    created_at      TIMESTAMP       NOT NULL DEFAULT NOW(),
    created_by      BIGINT
);

CREATE INDEX IF NOT EXISTS idx_check_reports_check_id ON nc_biz_asset_check_reports(check_id);
CREATE INDEX IF NOT EXISTS idx_check_reports_file_hash ON nc_biz_asset_check_reports(file_hash);
CREATE INDEX IF NOT EXISTS idx_check_reports_batch_id ON nc_biz_asset_check_reports(batch_id);

COMMENT ON TABLE nc_biz_asset_check_reports IS '资产核查PDF报告存储表';
COMMENT ON COLUMN nc_biz_asset_check_reports.check_id IS '关联核查记录ID';
COMMENT ON COLUMN nc_biz_asset_check_reports.file_hash IS 'SHA256文件哈希(去重用)';
COMMENT ON COLUMN nc_biz_asset_check_reports.is_valid IS '验证是否通过(文件名含姓名)';

-- nc_biz_pdf_verification_records 表废弃，数据合并到 nc_biz_asset_check_reports
-- DROP TABLE IF EXISTS nc_biz_pdf_verification_records;