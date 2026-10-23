-- ============================================================================
-- 20261021 年度种植补贴四合一：存量全历史转换（一次性脚本，旧表 DROP 后勿重跑）
--
-- 背景：移除 农业补贴(地力)/种植补贴/大豆补贴(品种)/轮作补贴 四个导入入口，
--       统一为「年度种植补贴」长表 nc_biz_annual_planting_subsidy（按 data_year 分年段）。
--
-- 阶段一（本文件 1~4 节）：建表 + 全历史转换 + 对账核验 —— 可重复执行（ON CONFLICT DO NOTHING）。
-- 阶段二（第 5 节，默认注释）：核验通过并经人工确认后，DROP 旧 7 张表（先 detail 后 person）。
--
-- 表结构权威：Resources/Schema/subsidy_imports/annual_planting_subsidy.yaml（本文件 DDL 与其保持一致）。
-- 备份：执行前 pg_dump 7 张旧表到 backup\<时间戳>_四合一导入重构_旧表备份\（见执行记录）。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ----------------------------------------------------------------------------
-- 1. 新表（幂等；与 yaml 完全一致）
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nc_biz_annual_planting_subsidy (
    id           BIGSERIAL PRIMARY KEY,
    data_year    INTEGER      NOT NULL,
    name         VARCHAR(100) NOT NULL,
    id_card      VARCHAR(20)  NOT NULL,
    phone        VARCHAR(20),
    address      VARCHAR(255),
    category     VARCHAR(50)  NOT NULL,
    area         NUMERIC(18,4),
    amount       NUMERIC(18,2),
    source_type  VARCHAR(50)  NOT NULL DEFAULT '',
    source_file  VARCHAR(500),
    imported_at  TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
    created_at   TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
    updated_at   TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uk_annual_planting_year_card_category_source
        UNIQUE (data_year, id_card, category, source_type)
);

CREATE INDEX IF NOT EXISTS idx_annual_planting_id_card   ON nc_biz_annual_planting_subsidy (id_card);
CREATE INDEX IF NOT EXISTS idx_annual_planting_data_year ON nc_biz_annual_planting_subsidy (data_year);
CREATE INDEX IF NOT EXISTS idx_annual_planting_year_card ON nc_biz_annual_planting_subsidy (data_year, id_card);

COMMENT ON TABLE  nc_biz_annual_planting_subsidy IS '[导入台账] 年度种植补贴表 - 长表：一年×一人×一个分类（玉米/大豆/水稻/地力/轮作/大豆品种）';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.data_year   IS '数据年份（年段）';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.name        IS '姓名';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.id_card     IS '身份证号';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.phone       IS '联系电话';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.address     IS '家庭地址（村/屯，可从文件名解析）';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.category    IS '补贴分类（与申请表单 SubsidyType 同名）：玉米补贴/大豆补贴/地表水水稻/地下水水稻/地力补贴/轮作补贴/大豆品种补贴';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.area        IS '面积（亩）- 存量转换来的面积型数据；金额型导入行为 NULL';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.amount      IS '补贴金额（元）- 新模板按金额导入；存量面积型数据为 NULL';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.source_type IS '文件来源标记（非社保卡/社保卡/高油/高蛋白，其余为空串）；空串参与唯一键';
COMMENT ON COLUMN nc_biz_annual_planting_subsidy.source_file IS '原始文件名（审计用，幂等靠 data_year+证件+分类+来源标记）';

-- ----------------------------------------------------------------------------
-- 2. 存量转换（全历史，按 data_year 逐段）
--    口径：金额型 → amount（area 空）；面积型 → area（amount 空）；二者读取侧按分类求和合并。
--    source_type：地力/轮作沿用旧「社保卡/非社保卡」标记，大豆品种记 高油/高蛋白，种植补贴为空串。
-- ----------------------------------------------------------------------------

-- 2.1 种植补贴 → 玉米/大豆/地表水水稻/地下水水稻（面积型，宽表 4 列转 4 行）
INSERT INTO nc_biz_annual_planting_subsidy
    (data_year, name, id_card, phone, address, category, area, amount, source_type, source_file, imported_at)
SELECT COALESCE(p.data_year,
                EXTRACT(YEAR FROM COALESCE(p.imported_at, p.created_at))::int),
       p.name, p.id_card, p.phone, p.address,
       v.category, v.area, NULL,
       '', '转换:nc_biz_planting_subsidy',
       COALESCE(p.imported_at, p.created_at, CURRENT_TIMESTAMP)
  FROM nc_biz_planting_subsidy p
  CROSS JOIN LATERAL (VALUES
        ('玉米补贴',   p.corn_acreage),
        ('大豆补贴',   p.soybean_acreage),
        ('地表水水稻', p.rice_surface_water_acreage),
        ('地下水水稻', p.rice_groundwater_acreage)
  ) AS v(category, area)
 WHERE COALESCE(v.area, 0) > 0
   AND COALESCE(p.name, '') <> ''
   AND COALESCE(p.id_card, '') <> ''
ON CONFLICT (data_year, id_card, category, source_type) DO NOTHING;

-- 2.2 地力补贴 → 地力补贴（金额型；社保卡/非社保卡 两个来源分别保留，读取侧求和）
INSERT INTO nc_biz_annual_planting_subsidy
    (data_year, name, id_card, phone, address, category, area, amount, source_type, source_file, imported_at)
SELECT COALESCE(s.data_year,
                EXTRACT(YEAR FROM COALESCE(s.imported_at, s.created_at))::int),
       s.name, s.id_card, s.phone, s.address,
       '地力补贴', NULL, s.subsidy_amount,
       COALESCE(s.subsidy_type, ''), '转换:nc_biz_soil_subsidy',
       COALESCE(s.imported_at, s.created_at, CURRENT_TIMESTAMP)
  FROM nc_biz_soil_subsidy s
 WHERE COALESCE(s.subsidy_amount, 0) <> 0
   AND COALESCE(s.name, '') <> ''
   AND COALESCE(s.id_card, '') <> ''
ON CONFLICT (data_year, id_card, category, source_type) DO NOTHING;

-- 2.3 轮作补贴 → 轮作补贴（面积 + 金额并存；社保卡/非社保卡 分别保留）
INSERT INTO nc_biz_annual_planting_subsidy
    (data_year, name, id_card, phone, address, category, area, amount, source_type, source_file, imported_at)
SELECT COALESCE(r.data_year,
                EXTRACT(YEAR FROM COALESCE(r.imported_at, r.created_at))::int),
       r.name, r.id_card, r.phone, r.address,
       '轮作补贴', r.rotation_area, r.subsidy_amount,
       COALESCE(r.subsidy_type, ''), '转换:nc_biz_rotation_subsidy',
       COALESCE(r.imported_at, r.created_at, CURRENT_TIMESTAMP)
  FROM nc_biz_rotation_subsidy r
 WHERE (COALESCE(r.subsidy_amount, 0) <> 0 OR COALESCE(r.rotation_area, 0) <> 0)
   AND COALESCE(r.name, '') <> ''
   AND COALESCE(r.id_card, '') <> ''
ON CONFLICT (data_year, id_card, category, source_type) DO NOTHING;

-- 2.4 高油大豆 → 大豆品种补贴（面积型；明细补贴面积求和，无明细用人员总种植面积）
INSERT INTO nc_biz_annual_planting_subsidy
    (data_year, name, id_card, phone, address, category, area, amount, source_type, source_file, imported_at)
SELECT COALESCE(p.data_year,
                EXTRACT(YEAR FROM COALESCE(p.imported_at, p.created_at))::int),
       p.name, p.id_card, p.phone, p.address,
       '大豆品种补贴', COALESCE(d.area, p.total_area), NULL,
       '高油', '转换:nc_biz_high_oil_soybean_person',
       COALESCE(p.imported_at, p.created_at, CURRENT_TIMESTAMP)
  FROM nc_biz_high_oil_soybean_person p
  LEFT JOIN (SELECT person_id, SUM(COALESCE(subsidy_area, 0)) AS area
               FROM nc_biz_high_oil_soybean_detail GROUP BY person_id) d
         ON d.person_id = p.id
 WHERE COALESCE(COALESCE(d.area, p.total_area), 0) > 0
   AND COALESCE(p.name, '') <> ''
   AND COALESCE(p.id_card, '') <> ''
ON CONFLICT (data_year, id_card, category, source_type) DO NOTHING;

-- 2.5 高蛋白大豆 → 大豆品种补贴（同上，source_type='高蛋白'）
INSERT INTO nc_biz_annual_planting_subsidy
    (data_year, name, id_card, phone, address, category, area, amount, source_type, source_file, imported_at)
SELECT COALESCE(p.data_year,
                EXTRACT(YEAR FROM COALESCE(p.imported_at, p.created_at))::int),
       p.name, p.id_card, p.phone, p.address,
       '大豆品种补贴', COALESCE(d.area, p.total_area), NULL,
       '高蛋白', '转换:nc_biz_high_protein_soybean_person',
       COALESCE(p.imported_at, p.created_at, CURRENT_TIMESTAMP)
  FROM nc_biz_high_protein_soybean_person p
  LEFT JOIN (SELECT person_id, SUM(COALESCE(subsidy_area, 0)) AS area
               FROM nc_biz_high_protein_soybean_detail GROUP BY person_id) d
         ON d.person_id = p.id
 WHERE COALESCE(COALESCE(d.area, p.total_area), 0) > 0
   AND COALESCE(p.name, '') <> ''
   AND COALESCE(p.id_card, '') <> ''
ON CONFLICT (data_year, id_card, category, source_type) DO NOTHING;

COMMIT;

-- ----------------------------------------------------------------------------
-- 3. 对账核验（只读）：新表按分类统计 vs 旧表源统计
-- ----------------------------------------------------------------------------
\echo '===== 阶段一核验：新表按 年度×分类 统计 ====='
SELECT data_year, category,
       COUNT(*)                          AS rows_cnt,
       COUNT(DISTINCT id_card)           AS persons,
       ROUND(COALESCE(SUM(area), 0), 2)   AS area_total,
       ROUND(COALESCE(SUM(amount), 0), 2) AS amount_total
  FROM nc_biz_annual_planting_subsidy
 GROUP BY data_year, category
 ORDER BY data_year, category;

\echo '===== 旧表源统计（应与上表逐类对应）====='
\echo '-- 种植补贴：应等于新表 玉米/大豆/地表水/地下水 四类的行数与面积'
SELECT 'planting' AS src, COUNT(*) AS person_rows,
       ROUND(SUM(COALESCE(corn_acreage,0)),2) AS corn,
       ROUND(SUM(COALESCE(soybean_acreage,0)),2) AS soy,
       ROUND(SUM(COALESCE(rice_surface_water_acreage,0)),2) AS rice_s,
       ROUND(SUM(COALESCE(rice_groundwater_acreage,0)),2) AS rice_g
  FROM nc_biz_planting_subsidy;
\echo '-- 地力补贴：金额合计'
SELECT 'soil' AS src, COUNT(*) AS rows_cnt, ROUND(COALESCE(SUM(subsidy_amount),0),2) AS amount_total
  FROM nc_biz_soil_subsidy;
\echo '-- 轮作补贴：面积与金额合计'
SELECT 'rotation' AS src, COUNT(*) AS rows_cnt,
       ROUND(COALESCE(SUM(rotation_area),0),2) AS area_total,
       ROUND(COALESCE(SUM(subsidy_amount),0),2) AS amount_total
  FROM nc_biz_rotation_subsidy;
\echo '-- 大豆品种（高油/高蛋白）：面积合计（明细求和，无明细取人员总面积）'
SELECT 'soybean' AS src, COUNT(*) AS person_rows,
       ROUND(SUM(COALESCE(d.area, p.total_area)),2) AS area_total
  FROM nc_biz_high_oil_soybean_person p
  LEFT JOIN (SELECT person_id, SUM(COALESCE(subsidy_area,0)) AS area
               FROM nc_biz_high_oil_soybean_detail GROUP BY person_id) d ON d.person_id = p.id;
SELECT 'soybean' AS src, COUNT(*) AS person_rows,
       ROUND(SUM(COALESCE(d.area, p.total_area)),2) AS area_total
  FROM nc_biz_high_protein_soybean_person p
  LEFT JOIN (SELECT person_id, SUM(COALESCE(subsidy_area,0)) AS area
               FROM nc_biz_high_protein_soybean_detail GROUP BY person_id) d ON d.person_id = p.id;

\echo '===== 覆盖核验：旧表证件是否都能在新表找到（应为 0）====='
SELECT COUNT(*) AS missing_persons FROM (
    SELECT DISTINCT id_card FROM nc_biz_planting_subsidy
    UNION SELECT DISTINCT id_card FROM nc_biz_soil_subsidy
    UNION SELECT DISTINCT id_card FROM nc_biz_rotation_subsidy
    UNION SELECT DISTINCT id_card FROM nc_biz_high_oil_soybean_person
    UNION SELECT DISTINCT id_card FROM nc_biz_high_protein_soybean_person
) old_cards
WHERE id_card NOT IN (SELECT DISTINCT id_card FROM nc_biz_annual_planting_subsidy);

-- ----------------------------------------------------------------------------
-- 4. （备用）阶段一回滚：仅清空新表，不动旧表
-- ----------------------------------------------------------------------------
-- TRUNCATE TABLE nc_biz_annual_planting_subsidy RESTART IDENTITY CASCADE;

-- ----------------------------------------------------------------------------
-- 5. 阶段二：DROP 旧 7 张表（**默认注释**，核验通过 + 人工确认后手工放开执行**）
--    旧表已 pg_dump 到 backup\<时间戳>_四合一导入重构_旧表备份\，删除不可逆，仅可从备份恢复。
-- ----------------------------------------------------------------------------
-- BEGIN;
-- DROP TABLE IF EXISTS nc_biz_high_oil_soybean_detail;
-- DROP TABLE IF EXISTS nc_biz_high_oil_soybean_person;
-- DROP TABLE IF EXISTS nc_biz_high_protein_soybean_detail;
-- DROP TABLE IF EXISTS nc_biz_high_protein_soybean_person;
-- DROP TABLE IF EXISTS nc_biz_planting_subsidy;
-- DROP TABLE IF EXISTS nc_biz_rotation_subsidy;
-- DROP TABLE IF EXISTS nc_biz_soil_subsidy;
-- COMMIT;
