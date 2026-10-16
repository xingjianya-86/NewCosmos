-- 20261016_visit_records.sql
-- 回访记录表（清偿方案后续：咨询登记回访功能 · 2026-10-16 拍板执行）
--   拍板：认定通过后补录 / 申请详情内嵌 / T3 模板名不动；不动申请状态机、与入户调查表分离。
--   录入关联 application_id；打印桥 applicant_id_card（T3 资产核验补打链按户主身份证取最近一条）。
-- Schema 权威：Resources\Schema\social_assistance\visit_records.yaml（SchemaService 启动幂等兜底）
-- 本 SQL 供当前库立即建表（幂等 IF NOT EXISTS）。
-- 回滚：DROP TABLE IF EXISTS nc_biz_visit_records;（含数据时先备份该表）
-- ============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS nc_biz_visit_records (
    id                BIGSERIAL PRIMARY KEY,
    application_id    BIGINT        NOT NULL,
    applicant_id_card VARCHAR(18)   NOT NULL,
    visit_at          TIMESTAMP     NOT NULL,
    visit_by          VARCHAR(50)   NOT NULL,
    visit_result      VARCHAR(20)   NOT NULL,
    visit_remark      TEXT,
    created_by        VARCHAR(50),
    created_at        TIMESTAMP     NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMP     NOT NULL DEFAULT NOW(),
    deleted_at        TIMESTAMP
);

COMMENT ON TABLE nc_biz_visit_records IS '[流程] 回访记录表 - 低收入人口认定流程通过后补录回访，一户可多条，打印取最近';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_records_application') THEN
        ALTER TABLE nc_biz_visit_records
            ADD CONSTRAINT fk_visit_records_application
            FOREIGN KEY (application_id) REFERENCES nc_biz_applications (id) ON DELETE CASCADE;
    END IF;
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

CREATE INDEX IF NOT EXISTS idx_visit_records_application_id ON nc_biz_visit_records (application_id);
CREATE INDEX IF NOT EXISTS idx_visit_records_applicant_id_card ON nc_biz_visit_records (applicant_id_card);

-- 验收：11 列
SELECT column_name
FROM information_schema.columns
WHERE table_name = 'nc_biz_visit_records'
ORDER BY ordinal_position;

COMMIT;
