-- 2026-11-19 重复家庭成员行清理（软删冗余行）
--
-- 背景：历史导入/补录在同一档案内产生同一身份证的多条活跃成员行（app 38/40/44/64/68/82）。
--       重复行会虚增"非赡养成员数"，并让一档出现多个 is_applicant（户主）候选。
-- 取舍：优先保留被业务表引用的行（死亡记录 member_id、收入明细 member_id），
--       其次保留带户主标记/规范 member_category 的行；被删行均为无任何业务表引用（已逐 id 核对）。
-- 说明：只软删（deleted_at），物理数据保留可回滚；幂等可重复执行。
-- 执行顺序：本文件先执行，再执行 20261119_member_head_flag_normalize.sql。

BEGIN;

WITH redundant(id, reason) AS (
    VALUES
      (341::bigint, 'app38 佟淑华 重复户主行（保留 is_applicant 的 340）'),
      (350::bigint, 'app40 贺传波 重复户主行（保留死亡记录引用的 352）'),
      (353::bigint, 'app40 李建英 重复成员行（保留带成员类别的 351）'),
      (400::bigint, 'app44 解维平 重复成员行（保留户主行 401）'),
      (677::bigint, 'app64 赵成海 重复户主行（保留户主行 684）'),
      (679::bigint, 'app64 赵成海 重复户主行（保留户主行 684）'),
      (678::bigint, 'app64 韩云梅 重复成员行（保留死亡记录引用的 680）'),
      (708::bigint, 'app68 樊万河 重复户主行（保留死亡记录引用的 710）'),
      (711::bigint, 'app68 卢永华 重复成员行（保留收入明细引用的 709）'),
      (771::bigint, 'app82 贺满程 重复户主行（保留户主行 772）')
)
UPDATE nc_biz_family_members m
   SET deleted_at = NOW(), updated_at = NOW(), is_applicant = FALSE
  FROM redundant r
 WHERE m.id = r.id
   AND m.deleted_at IS NULL;

COMMIT;

-- 软删行不再保留户主标记（防漏 deleted_at 过滤的查询把历史行当现任户主）；幂等
UPDATE nc_biz_family_members
   SET is_applicant = FALSE, updated_at = NOW()
 WHERE deleted_at IS NOT NULL AND is_applicant = TRUE;

-- 核对：清理后是否仍有"同档案同身份证多条活跃行"（应为 0 行）
\pset pager off
SELECT application_id, id_card, COUNT(*) AS dup_rows
  FROM nc_biz_family_members
 WHERE deleted_at IS NULL AND COALESCE(id_card, '') <> ''
 GROUP BY application_id, id_card
HAVING COUNT(*) > 1
 ORDER BY application_id;
