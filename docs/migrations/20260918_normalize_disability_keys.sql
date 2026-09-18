-- 残疾类型/等级：显示值归一为字典 key（nc_biz_family_members / nc_biz_applications）
-- 背景：FamilyMember.ParseDisabilityCertificate 原先把中文显示值写入 disability_type/disability_level，
--       与主表/导入路径的 key 存储不一致，导致分类认定、单人保、分类施保无法按 key 匹配三级智力/精神等。
-- 依据：nc_dict_items（category=DisabilityTypes / DisabilityLevels）反查 key，无硬编码映射；幂等，可重复执行。
-- 执行：psql -h <host> -U new_cosmos -d new_cosmos -f 20260918_normalize_disability_keys.sql

BEGIN;

UPDATE nc_biz_family_members fm
SET disability_type = d.item_key, updated_at = NOW()
FROM nc_dict_items d
WHERE d.category = 'DisabilityTypes'
  AND fm.disability_type = d.item_value
  AND fm.disability_type <> d.item_key;

UPDATE nc_biz_family_members fm
SET disability_level = d.item_key, updated_at = NOW()
FROM nc_dict_items d
WHERE d.category = 'DisabilityLevels'
  AND fm.disability_level = d.item_value
  AND fm.disability_level <> d.item_key;

UPDATE nc_biz_applications a
SET disability_type = d.item_key, updated_at = NOW()
FROM nc_dict_items d
WHERE d.category = 'DisabilityTypes'
  AND a.disability_type = d.item_value
  AND a.disability_type <> d.item_key;

UPDATE nc_biz_applications a
SET disability_level = d.item_key, updated_at = NOW()
FROM nc_dict_items d
WHERE d.category = 'DisabilityLevels'
  AND a.disability_level = d.item_value
  AND a.disability_level <> d.item_key;

COMMIT;

-- 校验：应无中文显示值残留（仅 key 或空值）
SELECT 'members' AS table_name, disability_type, disability_level, count(*)
FROM nc_biz_family_members
WHERE COALESCE(disability_type, '') <> '' OR COALESCE(disability_level, '') <> ''
GROUP BY 1, 2, 3
UNION ALL
SELECT 'applications', disability_type, disability_level, count(*)
FROM nc_biz_applications
WHERE COALESCE(disability_type, '') <> '' OR COALESCE(disability_level, '') <> ''
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;
