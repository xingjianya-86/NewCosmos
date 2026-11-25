-- 2026-11-19 户主成员标记归一化（一档一主）
--
-- 背景：户主权威在主表 nc_biz_applications.applicant_id_card，成员表 is_applicant 是镜像标记。
--       历史户主变更/导入未清理旧标记，出现一档多 is_applicant（app 38：佟淑华 340 + 已故原户主吴克学 388），
--       以及户主行存在但 is_applicant=false（app 37、52：category=HouseholdHead 但未标记）。
--       影响：月报户主关系、打印户主列、资产核验、户主死亡定位（可能取到已故者）。
-- 做法：① 每档把"身份证 = 主表申请人"的活跃行置为唯一户主（is_applicant=true、HouseholdHead、本人/户主）；
--       ② 其余活跃行清除 is_applicant。
-- 说明：幂等可重复执行；只动标记/类别/关系三列，不删行、不改人员信息。
-- 执行顺序：先执行 20261119_member_duplicate_cleanup.sql（避免重复行抢到"最小 id"）。

BEGIN;

WITH head AS (
    SELECT DISTINCT ON (a.id) a.id AS application_id, m.id AS member_id
      FROM nc_biz_applications a
      JOIN nc_biz_family_members m
        ON m.application_id = a.id
       AND m.deleted_at IS NULL
     WHERE a.deleted_at IS NULL
       AND COALESCE(TRIM(a.applicant_id_card), '') <> ''
       AND TRIM(COALESCE(m.id_card, '')) = TRIM(a.applicant_id_card)
     ORDER BY a.id, m.id
)
UPDATE nc_biz_family_members m
   SET is_applicant = TRUE,
       member_category = 'HouseholdHead',
       relationship_to_head = '本人/户主',
       updated_at = NOW()
  FROM head
 WHERE m.id = head.member_id
   AND (COALESCE(m.is_applicant, FALSE) = FALSE
        OR COALESCE(m.member_category, '') <> 'HouseholdHead'
        OR COALESCE(m.relationship_to_head, '') <> '本人/户主');

UPDATE nc_biz_family_members m
   SET is_applicant = FALSE, updated_at = NOW()
  FROM nc_biz_applications a
 WHERE m.application_id = a.id
   AND a.deleted_at IS NULL
   AND m.deleted_at IS NULL
   AND COALESCE(TRIM(a.applicant_id_card), '') <> ''
   AND COALESCE(m.is_applicant, FALSE) = TRUE
   AND TRIM(COALESCE(m.id_card, '')) <> TRIM(a.applicant_id_card);

COMMIT;

-- 核对：一档多户主（应为 0 行）
\pset pager off
SELECT application_id, COUNT(*) AS head_rows
  FROM nc_biz_family_members
 WHERE deleted_at IS NULL AND is_applicant
 GROUP BY application_id
HAVING COUNT(*) > 1
 ORDER BY application_id;

-- 核对：有申请人身份证但无户主标记的档案（应为 0 行）
SELECT a.id AS application_id, a.applicant_name
  FROM nc_biz_applications a
 WHERE a.deleted_at IS NULL
   AND COALESCE(TRIM(a.applicant_id_card), '') <> ''
   AND NOT EXISTS (
       SELECT 1 FROM nc_biz_family_members m
        WHERE m.application_id = a.id AND m.deleted_at IS NULL AND m.is_applicant)
 ORDER BY a.id;
