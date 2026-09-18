-- 软删除档案清理：物理删除已软删除且非停保、无变更记录、无子档案的档案及其从表；
-- 恢复停保旧档案下被误软删的成员；物理删除活档案下冗余软删成员。
-- 备份表见 nc_biz_*_bak_20260911。
-- 执行：psql -1 -f 20260911_purge_soft_deleted_archives.sql

BEGIN;

-- B1. 恢复「停保(Stopped)旧档案」下被软删的成员（旧档案须保留其成员）
UPDATE nc_biz_family_members fm
SET deleted_at = NULL
FROM nc_biz_applications a
WHERE fm.application_id = a.id
  AND fm.deleted_at IS NOT NULL
  AND a.deleted_at IS NULL
  AND a.status = 'Stopped';

-- B2. 物理删除「活档案(非停保)」下冗余软删成员
DELETE FROM nc_biz_family_members fm
USING nc_biz_applications a
WHERE fm.application_id = a.id
  AND fm.deleted_at IS NOT NULL
  AND a.deleted_at IS NULL
  AND a.status <> 'Stopped';

-- A. 物理删除待删档案（28 张树江/Draft、42 杨树贵/Approved、61 贺满程/Draft）的从表数据
DO $$
DECLARE t text;
  tbls text[] := ARRAY[
    'nc_biz_alimony_incomes','nc_biz_application_logs','nc_biz_archives','nc_biz_breeding_incomes',
    'nc_biz_business_incomes','nc_biz_capability_assessments','nc_biz_caregivers','nc_biz_change_records',
    'nc_biz_employment_costs','nc_biz_family_members','nc_biz_financial_assets','nc_biz_forest_lands',
    'nc_biz_grace_periods','nc_biz_guardians','nc_biz_household_surveys','nc_biz_kinship_filings',
    'nc_biz_labor_incomes','nc_biz_land_confirmations','nc_biz_land_incomes','nc_biz_land_registrations',
    'nc_biz_machineries','nc_biz_near_relative_links','nc_biz_other_incomes','nc_biz_properties',
    'nc_biz_property_incomes','nc_biz_rigid_expenditures','nc_biz_self_care_assessments','nc_biz_subsidies',
    'nc_biz_subsidy_incomes','nc_biz_supporters','nc_biz_transfer_incomes','nc_biz_vehicles'];
BEGIN
  FOREACH t IN ARRAY tbls LOOP
    EXECUTE format('DELETE FROM %I WHERE application_id = ANY(ARRAY[28,42,61]::bigint[])', t);
  END LOOP;
END $$;

DELETE FROM nc_biz_applications WHERE id IN (28,42,61);

COMMIT;

-- 校验
SELECT 'applications' AS k, count(*) AS v FROM nc_biz_applications
UNION ALL SELECT 'applications_soft_deleted', count(*) FROM nc_biz_applications WHERE deleted_at IS NOT NULL
UNION ALL SELECT 'members_soft_deleted', count(*) FROM nc_biz_family_members WHERE deleted_at IS NOT NULL
UNION ALL SELECT 'stopped_soft_deleted_members', count(*) FROM nc_biz_family_members fm JOIN nc_biz_applications a ON a.id=fm.application_id WHERE fm.deleted_at IS NOT NULL AND a.status='Stopped';
