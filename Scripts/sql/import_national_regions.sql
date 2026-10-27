-- ============================================================================
-- 全国行政区划导入（按码合并，保留现有 id）—— 数据源：adyliu/china_area（国家统计局 2023-06-30 版）
-- CSV 结构：code(12位),name,level(1..5),pcode,category
--
-- 为什么「按码合并」而不是全量重建：
--   库中 towns 1,703 / villages 14,451 的 code 填充率 100%，且**业务表已引用现有 id**：
--     nc_sys_organizations.town_id → 柳树镇 11197
--     nc_biz_applications.hukou_village_id → 4 个村
--   全量 TRUNCATE 重建会把这些外键全部打成孤儿。
--
-- 合并规则：
--   省：province_code(2位) 命中 → 只更新名称（现有 34 行保留 id）
--   市：city_code(4位) 命中 → 更新名称；未命中 → INSERT 新 id
--   县：county_code(6位) 命中 → 更新名称+归属；未命中 → INSERT（挂 LEFT(code,4) 市）
--   镇：town_code(9位) 命中 → 更新名称+归属；未命中 → INSERT（挂 LEFT(code,6) 县）
--   村：village_code(12位) 命中 → 更新名称+归属；未命中 → INSERT（挂 LEFT(code,9) 镇）
--   兜底：码不匹配但「同父级下同名」视为同一单位（防止历史码格式差异导致重复）
--
-- 幂等：重复执行只会 UPDATE 名称（无变化时 0 行），不会重复插入。
-- 说明：只做 UPDATE/INSERT，无 DROP/TRUNCATE；末尾对齐取号序列。
-- ============================================================================

BEGIN;

-- ① 原始数据落地（临时表，事务结束自动删）
CREATE TEMP TABLE nc_area_raw (
    code     varchar(12)  NOT NULL,
    name     varchar(100) NOT NULL,
    level    int          NOT NULL,
    pcode    varchar(12),
    category varchar(16)
) ON COMMIT DROP;

\copy nc_area_raw FROM '__CSV_PATH__' WITH (FORMAT csv, ENCODING 'UTF8')

-- 装载量核对（各级条数）
SELECT level, count(*) AS rows FROM nc_area_raw GROUP BY level ORDER BY level;

-- ② 省（34 行已存在，只校名）
UPDATE nc_regions_provinces p
SET province_name = r.name
FROM nc_area_raw r
WHERE r.level = 1
  AND p.province_code = LEFT(r.code, 2)
  AND p.province_name <> r.name;

-- ③ 市
UPDATE nc_regions_cities c
SET city_name = r.name
FROM nc_area_raw r
WHERE r.level = 2
  AND c.city_code = LEFT(r.code, 4)
  AND c.city_name <> r.name;

INSERT INTO nc_regions_cities (id, city_name, city_code, province_id, sort_order, is_active)
SELECT (SELECT COALESCE(MAX(id), 0) FROM nc_regions_cities) + ROW_NUMBER() OVER (ORDER BY r.code),
       r.name, LEFT(r.code, 4),
       (SELECT id FROM nc_regions_provinces WHERE province_code = LEFT(r.code, 2)),
       2000 + ROW_NUMBER() OVER (ORDER BY r.code), true
FROM nc_area_raw r
WHERE r.level = 2
  AND NOT EXISTS (
        SELECT 1 FROM nc_regions_cities c
        WHERE c.city_code = LEFT(r.code, 4)
           OR (c.city_name = r.name
               AND c.province_id = (SELECT id FROM nc_regions_provinces WHERE province_code = LEFT(r.code, 2))));

-- ④ 县（挂 LEFT(code,4) 市）
UPDATE nc_regions_counties co
SET county_name = r.name,
    city_name   = (SELECT c.city_name FROM nc_regions_cities c WHERE c.city_code = LEFT(r.code, 4))
FROM nc_area_raw r
WHERE r.level = 3
  AND co.county_code = LEFT(r.code, 6)
  AND (co.county_name <> r.name
       OR co.city_name IS DISTINCT FROM (SELECT c.city_name FROM nc_regions_cities c WHERE c.city_code = LEFT(r.code, 4)));

INSERT INTO nc_regions_counties (id, county_name, county_code, city_name, sort_order, is_active)
SELECT (SELECT COALESCE(MAX(id), 0) FROM nc_regions_counties) + ROW_NUMBER() OVER (ORDER BY r.code),
       r.name, LEFT(r.code, 6),
       COALESCE((SELECT c.city_name FROM nc_regions_cities c WHERE c.city_code = LEFT(r.code, 4)), ''),
       2000 + ROW_NUMBER() OVER (ORDER BY r.code), true
FROM nc_area_raw r
WHERE r.level = 3
  AND NOT EXISTS (
        SELECT 1 FROM nc_regions_counties co
        WHERE co.county_code = LEFT(r.code, 6)
           OR (co.county_name = r.name
               AND co.city_name = COALESCE((SELECT c.city_name FROM nc_regions_cities c WHERE c.city_code = LEFT(r.code, 4)), '')));

-- ④b 占位县（直筒子市：东莞 4419 / 中山 4420 / 儋州 4604 —— 市下直接是街道、镇，
--     CSV 无县级行；若不补占位县，这 77 个镇的 county_id 为 NULL 会撞非空约束。
--     占位县码 = 镇码 LEFT(6)（如 441900），县名 = 市名，符合国家统计局码规）
INSERT INTO nc_regions_counties (id, county_name, county_code, city_name, sort_order, is_active)
SELECT (SELECT COALESCE(MAX(id), 0) FROM nc_regions_counties) + ROW_NUMBER() OVER (ORDER BY t.ccode),
       COALESCE((SELECT c.city_name FROM nc_regions_cities c WHERE c.city_code = LEFT(t.ccode, 4)),
                (SELECT s.name FROM nc_area_raw s WHERE s.level = 2 AND s.code = LEFT(t.ccode, 4) || '00000000'),
                t.ccode),
       t.ccode,
       COALESCE((SELECT c.city_name FROM nc_regions_cities c WHERE c.city_code = LEFT(t.ccode, 4)),
                (SELECT s.name FROM nc_area_raw s WHERE s.level = 2 AND s.code = LEFT(t.ccode, 4) || '00000000'),
                t.ccode),
       3000 + ROW_NUMBER() OVER (ORDER BY t.ccode), true
FROM (SELECT DISTINCT LEFT(r.code, 6) AS ccode
      FROM nc_area_raw r
      WHERE r.level = 4
        AND NOT EXISTS (SELECT 1 FROM nc_area_raw c
                        WHERE c.level = 3 AND c.code = LEFT(r.code, 6) || '000000')
        AND NOT EXISTS (SELECT 1 FROM nc_regions_counties co
                        WHERE co.county_code = LEFT(r.code, 6))) t;

-- ⑤ 镇（挂 LEFT(code,6) 县；town_type 按名称判定：街道/乡/镇）
UPDATE nc_regions_towns t
SET town_name = r.name
FROM nc_area_raw r
WHERE r.level = 4
  AND t.town_code = LEFT(r.code, 9)
  AND t.town_name <> r.name;

INSERT INTO nc_regions_towns (id, county_id, town_name, town_code, town_type, sort_order, is_active)
SELECT (SELECT COALESCE(MAX(id), 0) FROM nc_regions_towns) + ROW_NUMBER() OVER (ORDER BY r.code),
       (SELECT co.id FROM nc_regions_counties co WHERE co.county_code = LEFT(r.code, 6)),
       r.name, LEFT(r.code, 9),
       CASE WHEN r.name LIKE '%街道%' THEN '街道'
            WHEN r.name LIKE '%乡%'   THEN '乡'
            ELSE '镇' END,
       2000 + ROW_NUMBER() OVER (ORDER BY r.code), true
FROM nc_area_raw r
WHERE r.level = 4
  AND NOT EXISTS (
        SELECT 1 FROM nc_regions_towns t
        WHERE t.town_code = LEFT(r.code, 9)
           OR (t.town_name = r.name
               AND t.county_id = (SELECT co.id FROM nc_regions_counties co WHERE co.county_code = LEFT(r.code, 6))));

-- ⑥ 村（挂 LEFT(code,9) 镇；village_type 按名称判定）
UPDATE nc_regions_villages v
SET village_name = r.name
FROM nc_area_raw r
WHERE r.level = 5
  AND v.village_code = r.code
  AND v.village_name <> r.name;

INSERT INTO nc_regions_villages (id, town_id, village_name, village_code, village_type, sort_order, is_active)
SELECT (SELECT COALESCE(MAX(id), 0) FROM nc_regions_villages) + ROW_NUMBER() OVER (ORDER BY r.code),
       (SELECT t.id FROM nc_regions_towns t WHERE t.town_code = LEFT(r.code, 9)),
       r.name, r.code,
       CASE WHEN r.name LIKE '%社区%' OR r.name LIKE '%居委会%' THEN '居委' ELSE '村委会' END,
       2000 + ROW_NUMBER() OVER (ORDER BY r.code), true
FROM nc_area_raw r
WHERE r.level = 5
  AND NOT EXISTS (
        SELECT 1 FROM nc_regions_villages v
        WHERE v.village_code = r.code
           OR (v.village_name = r.name
               AND v.town_id = (SELECT t.id FROM nc_regions_towns t WHERE t.town_code = LEFT(r.code, 9))));

-- ⑦ 取号序列对齐（新增行后必须，否则下次取号撞主键）
--    序列由 RegionService 幂等创建，本库可能尚未跑过地区页 → 这里先 IF NOT EXISTS 补建；
--    对齐口径与 RegionService.SyncSequenceSql / docs/migrations/20261025_region_sequences.sql 一致：
--    GREATEST(表内最大 ID, 序列已发到的值, 基线) → 只前进不回退，下一个 nextval = 该值 + 1。
--    **不设列 DEFAULT**：所有 INSERT 仍显式传 id，序列只承担"下一个 ID"分配。
DO $$
BEGIN
    CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_cities;
    CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_counties;
    CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_towns;
    CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_villages;

    PERFORM setval('seq_nc_regions_cities', GREATEST(
        COALESCE((SELECT MAX(id) FROM nc_regions_cities), 0),
        COALESCE((SELECT last_value FROM pg_sequences WHERE schemaname='public' AND sequencename='seq_nc_regions_cities'), 0),
        0));
    PERFORM setval('seq_nc_regions_counties', GREATEST(
        COALESCE((SELECT MAX(id) FROM nc_regions_counties), 0),
        COALESCE((SELECT last_value FROM pg_sequences WHERE schemaname='public' AND sequencename='seq_nc_regions_counties'), 0),
        9000));
    PERFORM setval('seq_nc_regions_towns', GREATEST(
        COALESCE((SELECT MAX(id) FROM nc_regions_towns), 0),
        COALESCE((SELECT last_value FROM pg_sequences WHERE schemaname='public' AND sequencename='seq_nc_regions_towns'), 0),
        90000));
    PERFORM setval('seq_nc_regions_villages', GREATEST(
        COALESCE((SELECT MAX(id) FROM nc_regions_villages), 0),
        COALESCE((SELECT last_value FROM pg_sequences WHERE schemaname='public' AND sequencename='seq_nc_regions_villages'), 0),
        900000));
END $$;

COMMIT;

-- ⑧ 结果核对
\pset pager off
SELECT 'provinces' AS lvl, count(*) AS total FROM nc_regions_provinces
UNION ALL SELECT 'cities',   count(*) FROM nc_regions_cities
UNION ALL SELECT 'counties', count(*) FROM nc_regions_counties
UNION ALL SELECT 'towns',    count(*) FROM nc_regions_towns
UNION ALL SELECT 'villages', count(*) FROM nc_regions_villages;

-- 未挂上级的孤儿行（应为 0）
SELECT 'city_orphan'    AS issue, count(*) FROM nc_regions_cities   WHERE province_id IS NULL
UNION ALL SELECT 'town_orphan',    count(*) FROM nc_regions_towns    WHERE county_id IS NULL
UNION ALL SELECT 'village_orphan', count(*) FROM nc_regions_villages WHERE town_id IS NULL;

-- 占位县（直筒子市）应为 3 行：441900 东莞 / 442000 中山 / 460400 儋州
SELECT 'pseudo_county' AS issue, count(*) AS cnt FROM nc_regions_counties
WHERE county_code IN ('441900', '442000', '460400');

-- 取号序列已建且对齐（last_value 应 ≥ 各表 max(id)；只读，不消耗取号）
SELECT sequencename AS issue, last_value
FROM pg_sequences
WHERE schemaname = 'public' AND sequencename LIKE 'seq_nc_regions_%'
ORDER BY sequencename;
