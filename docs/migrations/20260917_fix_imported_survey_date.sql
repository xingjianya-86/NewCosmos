-- ============================================================================
-- 2026-09-17 导入库建档档案入户调查日期修正
--
-- A) 赵某某户（档案 64、65）：survey_date 由 2025-01-01 改为纳入时间 2021-12-01；
-- B) 525（导入库建档）与其派生档案 526：补建入户调查记录，survey_date = 2019-01-01
--    （525 的纳入日期 = 建档时间 created_at；派生档案随链根）。
--
-- 幂等：A 仅当前值=2025-01-01 时更新；B 仅无未删调查行时插入。
-- 执行：psql -v ON_ERROR_STOP=1 -f <本文件>
-- ============================================================================

BEGIN;

-- A) 赵某某户：改为纳入时间
UPDATE nc_biz_household_surveys s
SET survey_date = DATE '2021-12-01',
    updated_at = NOW()
WHERE s.application_id IN (64, 65)
  AND s.deleted_at IS NULL
  AND s.survey_date = DATE '2025-01-01';

-- B) 525/526 补建调查行（调查人取档案 updated_by）
INSERT INTO nc_biz_household_surveys
    (application_id, survey_date, surveyor_name, created_at, updated_at)
SELECT a.id, DATE '2019-01-01', COALESCE(NULLIF(a.updated_by, ''), ''), NOW(), NOW()
FROM nc_biz_applications a
WHERE a.id IN (525, 526)
  AND NOT EXISTS (
      SELECT 1 FROM nc_biz_household_surveys s
      WHERE s.application_id = a.id AND s.deleted_at IS NULL);

-- 验证输出
SELECT a.id AS application_id, a.applicant_name, s.survey_date, s.surveyor_name
FROM nc_biz_applications a
LEFT JOIN nc_biz_household_surveys s ON s.application_id = a.id AND s.deleted_at IS NULL
WHERE a.id IN (64, 65, 525, 526)
ORDER BY a.id;

COMMIT;
