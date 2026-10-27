-- 2026-10-26 行政区划加「省」级（真级联）—— 组织机构省级级联 + 《死亡情况核实说明》落款 {所在省}
--
-- 背景：区划原为 市→县→镇→村 四级，组织机构与文书落款都没有「省」。
--       2026-10-26 决策：建真级联省表（不做 province_name 文本字段）。
-- 设计要点：
--   ① 省表 province_code 用民政 2 位码；现有 city_code 为 4 位（前 2 位 = 省码）
--      → **回填零猜测**：UPDATE ... SET province_id = 省表按 LEFT(city_code,2) 命中
--   ② 不动 RegionService.SyncSchemaAsync 的表数组：该流程对 regions 各表 allowDrop=true 整表重建，
--      把省表塞进去会在同步时清空省数据；省表由本迁移 + 种子维护。
--   ③ 建序列 seq_nc_regions_provinces 并对齐 max(id)，与既有四表约定一致
--      （见 docs/migrations/20261025_region_sequences.sql）。
-- 说明：只做建表/加列/回填，无 DROP/TRUNCATE；可重复执行（幂等）。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261026_regions_provinces.sql

BEGIN;

-- ① 省级行政区表
CREATE TABLE IF NOT EXISTS nc_regions_provinces (
    id            integer       PRIMARY KEY,
    province_name varchar(50)   NOT NULL,
    province_code varchar(2),
    sort_order    integer       DEFAULT 0,
    is_active     boolean       DEFAULT true
);

COMMENT ON TABLE  nc_regions_provinces IS '省级行政区表（2026-10-26 新增；34个省级行政单位）';
COMMENT ON COLUMN nc_regions_provinces.province_code IS '民政2位省码，与 city_code 前2位对齐（用于回填与校验）';

CREATE INDEX IF NOT EXISTS idx_regions_provinces_code ON nc_regions_provinces (province_code);

-- ② 34 个省级行政单位（幂等）
INSERT INTO nc_regions_provinces (id, province_name, province_code, sort_order, is_active) VALUES
 (1,  '北京市',           '11',  1,  true),
 (2,  '天津市',           '12',  2,  true),
 (3,  '河北省',           '13',  3,  true),
 (4,  '山西省',           '14',  4,  true),
 (5,  '内蒙古自治区',      '15',  5,  true),
 (6,  '辽宁省',           '21',  6,  true),
 (7,  '吉林省',           '22',  7,  true),
 (8,  '黑龙江省',          '23',  8,  true),
 (9,  '上海市',           '31',  9,  true),
 (10, '江苏省',           '32', 10,  true),
 (11, '浙江省',           '33', 11,  true),
 (12, '安徽省',           '34', 12,  true),
 (13, '福建省',           '35', 13,  true),
 (14, '江西省',           '36', 14,  true),
 (15, '山东省',           '37', 15,  true),
 (16, '河南省',           '41', 16,  true),
 (17, '湖北省',           '42', 17,  true),
 (18, '湖南省',           '43', 18,  true),
 (19, '广东省',           '44', 19,  true),
 (20, '广西壮族自治区',    '45', 20,  true),
 (21, '海南省',           '46', 21,  true),
 (22, '重庆市',           '50', 22,  true),
 (23, '四川省',           '51', 23,  true),
 (24, '贵州省',           '52', 24,  true),
 (25, '云南省',           '53', 25,  true),
 (26, '西藏自治区',        '54', 26,  true),
 (27, '陕西省',           '61', 27,  true),
 (28, '甘肃省',           '62', 28,  true),
 (29, '青海省',           '63', 29,  true),
 (30, '宁夏回族自治区',    '64', 30,  true),
 (31, '新疆维吾尔自治区',  '65', 31,  true),
 (32, '台湾省',           '71', 32,  true),
 (33, '香港特别行政区',    '81', 33,  true),
 (34, '澳门特别行政区',    '82', 34,  true)
ON CONFLICT (id) DO NOTHING;

-- ③ 序列对齐（与既有四表一致；RegionService.EnsureRegionObjectsAsync 亦会按 max(id) 对齐）
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_class WHERE relkind = 'S' AND relname = 'seq_nc_regions_provinces') THEN
        CREATE SEQUENCE seq_nc_regions_provinces OWNED BY nc_regions_provinces.id;
    END IF;
    PERFORM setval('seq_nc_regions_provinces', GREATEST((SELECT COALESCE(MAX(id), 0) FROM nc_regions_provinces), 1));
END $$;

-- ④ 地级市加省外键列（幂等）
ALTER TABLE nc_regions_cities ADD COLUMN IF NOT EXISTS province_id integer;
COMMENT ON COLUMN nc_regions_cities.province_id IS '所属省ID（2026-10-26 加，由 city_code 前2位回填）';

CREATE INDEX IF NOT EXISTS idx_regions_cities_province ON nc_regions_cities (province_id);

-- ⑤ 回填：city_code 前 2 位 = 省码（零猜测，不靠名称匹配）
UPDATE nc_regions_cities c
SET province_id = p.id
FROM nc_regions_provinces p
WHERE c.province_id IS NULL
  AND c.city_code IS NOT NULL
  AND length(c.city_code) >= 2
  AND p.province_code = left(c.city_code, 2);

COMMIT;

-- ⑥ 核对
\pset pager off
SELECT id, province_name, province_code FROM nc_regions_provinces ORDER BY id LIMIT 10;
SELECT count(*) AS province_total FROM nc_regions_provinces;
SELECT id, city_name, city_code, province_id FROM nc_regions_cities ORDER BY id;
SELECT count(*) AS city_unassigned FROM nc_regions_cities WHERE province_id IS NULL;
