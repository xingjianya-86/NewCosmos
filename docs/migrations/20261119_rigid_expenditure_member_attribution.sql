-- 2026-11-19 刚性支出落户到人（方案2）
--
-- 背景：nc_biz_rigid_expenditures 仅有 member_id（表单建档时恒 0，未记录身份），
--       刚性支出无法归属到具体人员，户主死亡时无法剔除已故者的医疗/教育/残疾康复支出。
-- 做法：新增 member_name / member_id_card 身份快照列（与其他收入明细表同款：成员行每次保存会重建，
--       仅存 member_id 会悬空）；存量按 member_id 关联成员表回填。
-- 幂等可重复执行；无 DROP/TRUNCATE。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261119_rigid_expenditure_member_attribution.sql

BEGIN;

ALTER TABLE nc_biz_rigid_expenditures ADD COLUMN IF NOT EXISTS member_name   VARCHAR(100);
ALTER TABLE nc_biz_rigid_expenditures ADD COLUMN IF NOT EXISTS member_id_card VARCHAR(18);

COMMENT ON COLUMN nc_biz_rigid_expenditures.member_name IS
    '支出人员姓名（身份快照，2026-11-19 起表单必填；死亡剔除按身份证匹配）';
COMMENT ON COLUMN nc_biz_rigid_expenditures.member_id_card IS
    '支出人员身份证号（身份快照，2026-11-19 起表单必填）';

-- 存量回填：按 member_id 关联未删除成员行（仅补空值，不覆盖后续人工修正）
UPDATE nc_biz_rigid_expenditures r
   SET member_name = m.name,
       member_id_card = m.id_card
  FROM nc_biz_family_members m
 WHERE r.member_id = m.id
   AND m.deleted_at IS NULL
   AND COALESCE(TRIM(r.member_name), '') = ''
   AND COALESCE(TRIM(r.member_id_card), '') = '';

COMMIT;

-- 核对：存量中仍无法归属到人的刚性支出行数（应为 0；新录入强制选人）
\pset pager off
SELECT COUNT(*) AS rigid_rows_without_member
  FROM nc_biz_rigid_expenditures
 WHERE deleted_at IS NULL
   AND (COALESCE(TRIM(member_name), '') = '' OR COALESCE(TRIM(member_id_card), '') = '');
