-- 2026-10-26 组织机构加「所属省」（真级联省，配合 docs/migrations/20261026_regions_provinces.sql）
--
-- 背景：《死亡情况核实说明》落款要出 {所在省}{所在市}{所在县区}{所在乡镇}{所在村} 五段，
--       而组织机构原本只有 city/county/town/village 四级，没有省。
-- 口径：
--   · 只存 province_id（整型外键语义，但**不加 FK 约束**：避免区划清理时把组织行卡死），
--     省名一律由 OrgSelect LEFT JOIN nc_regions_provinces 取（Single source）；
--   · 可空：存量组织未填省不影响任何既有功能，文书侧 {所在省} 输出为空并进数据提醒。
-- 说明：只做加列，无 DROP/TRUNCATE；可重复执行（幂等）。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261026_organization_province.sql

BEGIN;

ALTER TABLE nc_sys_organizations ADD COLUMN IF NOT EXISTS province_id integer;
COMMENT ON COLUMN nc_sys_organizations.province_id IS '所属省ID（2026-10-26 加，关联 nc_regions_provinces；省名由查询 JOIN 取得）';

-- ② 兜底回填：组织未填省但已填市 → 按 city 前2位省码回填（幂等）
UPDATE nc_sys_organizations o
SET province_id = c.province_id
FROM nc_regions_cities c
WHERE o.province_id IS NULL
  AND o.city_id IS NOT NULL
  AND c.id = o.city_id
  AND c.province_id IS NOT NULL;

COMMIT;

-- ③ 核对
\pset pager off
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'nc_sys_organizations' AND column_name = 'province_id';

SELECT o.id, o.name, p.province_name, c.city_name
FROM nc_sys_organizations o
     LEFT JOIN nc_regions_provinces p ON o.province_id = p.id
     LEFT JOIN nc_regions_cities    c ON o.city_id     = c.id
ORDER BY o.id;
