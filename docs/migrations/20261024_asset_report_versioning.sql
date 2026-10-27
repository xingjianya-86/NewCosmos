-- 核查报告按户版本化（2026-10-24）
-- 口径：每户（head_id_card）只保留一条 is_current=TRUE 的当前报告；
--       旧报告保留为历史版本（is_current=FALSE + superseded_at），不物理删除。
-- 幂等：可重复执行；重复执行时版本/当前标记会按最新 created_at 重新归一。

BEGIN;

ALTER TABLE nc_biz_asset_check_reports
    ADD COLUMN IF NOT EXISTS head_id_card   VARCHAR(18),
    ADD COLUMN IF NOT EXISTS version        INTEGER NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS is_current     BOOLEAN NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS superseded_at  TIMESTAMP,
    ADD COLUMN IF NOT EXISTS report_size    INTEGER;

COMMENT ON COLUMN nc_biz_asset_check_reports.head_id_card  IS '户主身份证号（按户报告版本/读取的唯一键）';
COMMENT ON COLUMN nc_biz_asset_check_reports.version       IS '户内报告版本号（每户从1递增）';
COMMENT ON COLUMN nc_biz_asset_check_reports.is_current    IS '是否当前版本（每户只允许一条 TRUE）';
COMMENT ON COLUMN nc_biz_asset_check_reports.superseded_at IS '被新版本替换的时间';
COMMENT ON COLUMN nc_biz_asset_check_reports.report_size   IS 'PDF字节数（octet_length(report_data)，容量监控用）';

-- 1) 回填户主身份证
UPDATE nc_biz_asset_check_reports r
SET head_id_card = c.head_id_card
FROM nc_biz_asset_checks c
WHERE r.check_id = c.id
  AND r.head_id_card IS NULL;

-- 2) 回填 size
UPDATE nc_biz_asset_check_reports
SET report_size = octet_length(report_data)
WHERE report_size IS NULL;

-- 3) 每户按时间升序编版本（最新版本号最大），最新一条为当前版本
WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY head_id_card
               ORDER BY created_at ASC, id ASC
           ) AS version_asc,
           ROW_NUMBER() OVER (
               PARTITION BY head_id_card
               ORDER BY created_at DESC, id DESC
           ) AS rn_desc
    FROM nc_biz_asset_check_reports
    WHERE head_id_card IS NOT NULL
)
UPDATE nc_biz_asset_check_reports r
SET version = ranked.version_asc,
    is_current = (ranked.rn_desc = 1),
    superseded_at = CASE
        WHEN ranked.rn_desc = 1 THEN NULL
        ELSE COALESCE(r.superseded_at, NOW())
    END
FROM ranked
WHERE r.id = ranked.id;

-- 4) 每户只允许一条当前版本
CREATE UNIQUE INDEX IF NOT EXISTS ux_check_reports_current_per_head
    ON nc_biz_asset_check_reports (head_id_card)
    WHERE is_current AND head_id_card IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_check_reports_head_id_card
    ON nc_biz_asset_check_reports (head_id_card);

COMMIT;
