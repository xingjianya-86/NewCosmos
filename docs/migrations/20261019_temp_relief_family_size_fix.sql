-- 2026-10-19 临时救助「家庭人口」口径订正（存量数据）
--
-- 背景：nc_biz_temp_relief_applications.family_size 原为"选定申请人那一刻"从来源台账抓取的一次性快照，
--       之后在表单增删家庭成员（或别处补全家庭信息）都不会回流，导致打印「审核审批表 / 审核审批表2」
--       的 {家庭人口} 与逐行家庭成员不一致（实例：王全光 LZ202609090001，family_size=1，实际两口人）。
-- 口径：家庭人口 = 户主(1 人) + 有效家庭成员行数（成员姓名非空）；
--       赡养/抚养/扶养义务人不计入（它们不属于 nc_biz_temp_relief_members）。
--       唯一实现见 Constants/TempReliefConstants.ResolveFamilySize，代码侧已改为保存/打印时按此重算。
-- 影响：本库仅 1 条记录不一致（id=10 王全光），其余 15 条已满足该口径，本脚本对其为 no-op。
-- 说明：本脚本只做「先备份再订正」，不做任何 DROP/TRUNCATE；可重复执行（幂等）。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261019_temp_relief_family_size_fix.sql

BEGIN;

-- ① 备份将被修改的行（幂等：已存在则保留首次备份）
CREATE TABLE IF NOT EXISTS nc_biz_temp_relief_applications_bak_20261019_family_size (
    id            bigint      PRIMARY KEY,
    family_size   integer,
    updated_at    timestamp,
    backed_up_at  timestamp   NOT NULL DEFAULT NOW()
);

INSERT INTO nc_biz_temp_relief_applications_bak_20261019_family_size (id, family_size, updated_at)
SELECT t.id, t.family_size, t.updated_at
FROM nc_biz_temp_relief_applications t
WHERE t.deleted_at IS NULL
  AND (SELECT count(*) FROM nc_biz_temp_relief_members m
       WHERE m.application_id = t.id AND m.deleted_at IS NULL
         AND COALESCE(m.member_name, '') <> '') > 0
  AND t.family_size IS DISTINCT FROM ((SELECT count(*) FROM nc_biz_temp_relief_members m
       WHERE m.application_id = t.id AND m.deleted_at IS NULL
         AND COALESCE(m.member_name, '') <> '') + 1)
ON CONFLICT (id) DO NOTHING;

-- ② 按统一口径订正：family_size = 有效家庭成员行数 + 1（户主）
UPDATE nc_biz_temp_relief_applications t
SET family_size = sub.member_count + 1,
    updated_at  = NOW()
FROM (
    SELECT application_id, count(*) AS member_count
    FROM nc_biz_temp_relief_members
    WHERE deleted_at IS NULL
      AND COALESCE(member_name, '') <> ''
    GROUP BY application_id
) sub
WHERE sub.application_id = t.id
  AND t.deleted_at IS NULL
  AND t.family_size IS DISTINCT FROM sub.member_count + 1;

COMMIT;

-- ③ 核对：应无输出（说明已全部满足口径）
\pset pager off
SELECT t.id, t.application_no, t.applicant_name, t.family_size,
       (SELECT count(*) FROM nc_biz_temp_relief_members m
        WHERE m.application_id = t.id AND m.deleted_at IS NULL
          AND COALESCE(m.member_name, '') <> '') + 1 AS expected_family_size
FROM nc_biz_temp_relief_applications t
WHERE t.deleted_at IS NULL
  AND (SELECT count(*) FROM nc_biz_temp_relief_members m
       WHERE m.application_id = t.id AND m.deleted_at IS NULL
         AND COALESCE(m.member_name, '') <> '') > 0
  AND t.family_size IS DISTINCT FROM ((SELECT count(*) FROM nc_biz_temp_relief_members m
       WHERE m.application_id = t.id AND m.deleted_at IS NULL
         AND COALESCE(m.member_name, '') <> '') + 1)
ORDER BY t.id;
