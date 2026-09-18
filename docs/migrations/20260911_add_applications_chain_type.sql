-- 档案链类型：nc_biz_applications.chain_type（记录 original_application_id 由来）
-- 执行：psql -h <host> -U new_cosmos -d new_cosmos -f 20260911_add_applications_chain_type.sql

ALTER TABLE nc_biz_applications ADD COLUMN IF NOT EXISTS chain_type varchar(30);
COMMENT ON COLUMN nc_biz_applications.chain_type IS
  '档案链类型：CategoryRebuild/HeadChange/HouseholdDeath/SingleRescue/ImportedArchive（original_application_id 由来）';
CREATE INDEX IF NOT EXISTS idx_applications_chain_type ON nc_biz_applications(chain_type);

-- 回填：仅子档案（original_application_id 非空）
UPDATE nc_biz_applications c
SET chain_type = CASE
    WHEN c.is_single_rescue THEN 'SingleRescue'
    WHEN EXISTS (SELECT 1 FROM nc_biz_change_records pcr
                 WHERE pcr.new_application_id = c.id AND pcr.change_type = 'HouseholdDeath') THEN 'HouseholdDeath'
    WHEN c.source_type = 'HouseholdDeath' THEN 'HouseholdDeath'
    WHEN EXISTS (SELECT 1 FROM nc_biz_change_records pcr
                 WHERE pcr.new_application_id = c.id AND pcr.change_type IN ('CategoryStop', 'CategoryAdd')) THEN 'CategoryRebuild'
    WHEN c.source_type = 'CategoryRebuild' THEN 'CategoryRebuild'
    ELSE c.chain_type
END
WHERE c.original_application_id IS NOT NULL AND c.original_application_id > 0;

-- 校验
SELECT id, applicant_name, status, original_application_id, is_single_rescue, source_type, chain_type
FROM nc_biz_applications
WHERE original_application_id IS NOT NULL AND original_application_id > 0
ORDER BY id;
