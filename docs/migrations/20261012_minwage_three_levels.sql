-- ============================================================================
-- 20261012_minwage_three_levels.sql
--
-- 目的：新增黑龙江省最低工资标准三档配置（黑政规〔2025〕1号，2025-12-01 起执行，
--       有效期三年），供《档案_变更告知书》「有劳动力」拒保理由作事实性参照。
--
-- 政策原文（黑政规〔2025〕1号）：
--   第一档 2270 元/月：哈尔滨市区（呼兰区、阿城区、双城区除外），大庆市区
--   第二档 2010 元/月：哈尔滨市呼兰区、阿城区、双城区，齐齐哈尔市区、牡丹江市区及
--                      绥芬河市、佳木斯市区及抚远市、鸡西市区、双鸭山市区、七台河市区、
--                      鹤岗市区、绥化市区，大兴安岭地区及所辖县（市、区）
--   第三档 1910 元/月：伊春市区、黑河市区，全省其他各县（市）
--   小时档：21 / 18.8 / 18.2 元
--
-- 业务定位（重要，勿扩大使用范围）：
--   三档仅用于文书中说明「劳动能力对应的收入能力」与就业引导。
--   最低工资标准在《牡丹江市低收入人口认定实施细则》中仅用于
--   「就业必要成本扣减」（第十六条）与「特困照料护理标准」（第四十五条），
--   **不得**用于给有劳动能力未就业成员核定家庭收入。
--
-- 对既有行的处理：
--   原 MinWage = 1850（黑政规〔2024〕1号第二档）予以软停用（is_active = false），
--   不物理删除，保留历史留痕。其错误有二：
--     ① 数值为 2024 版第二档，2025-12-01 起已作废；
--     ② 即便按 2024 版，林口县属第三档（1750 元），1850 元是牡丹江市区的值——填错了档。
--
-- 幂等性：三档按 standard_type + version 唯一插入，重复执行不产生重复行；
--         MinWage 停用语句本身幂等。
-- ============================================================================

BEGIN;

-- ----------------------------------------------------------------------------
-- 0. 备份将被改动的既有行（便于回滚；重复执行时先清掉上次的备份表）
-- ----------------------------------------------------------------------------
DROP TABLE IF EXISTS backup_20261012_minwage_rows;
CREATE TABLE backup_20261012_minwage_rows AS
SELECT * FROM nc_config_standards WHERE standard_type = 'MinWage';

-- ----------------------------------------------------------------------------
-- 1. 新增三档（standard_type 独立，不复用 hukou_type / support_mode：
--    后两者在本表的既有语义是 Rural|Urban 与低保/特困救助方式，复用会污染语义）
-- ----------------------------------------------------------------------------
INSERT INTO nc_config_standards
    (standard_type, standard_name, hukou_type, support_mode,
     standard_value, unit, effective_start_date, effective_end_date,
     version, is_active, description, created_by, created_at, updated_at)
SELECT
    v.standard_type, v.standard_name, NULL, NULL,
    v.standard_value, '元/月', DATE '2025-12-01', NULL,
    1, TRUE, v.description, 'migrations/20261012', NOW(), NOW()
FROM (VALUES
    ('MinWageLevel1',
     '最低工资标准（第一档）',
     2270.00,
     '黑政规〔2025〕1号第一档：哈尔滨市区（呼兰区、阿城区、双城区除外）、大庆市区。2025-12-01起执行，有效期三年。仅作事实参照，不得用于核定家庭收入。'),
    ('MinWageLevel2',
     '最低工资标准（第二档）',
     2010.00,
     '黑政规〔2025〕1号第二档：哈尔滨市呼兰区、阿城区、双城区，齐齐哈尔市区、牡丹江市区及绥芬河市、佳木斯市区及抚远市、鸡西市区、双鸭山市区、七台河市区、鹤岗市区、绥化市区，大兴安岭地区及所辖县（市、区）。2025-12-01起执行，有效期三年。仅作事实参照，不得用于核定家庭收入。'),
    ('MinWageLevel3',
     '最低工资标准（第三档）',
     1910.00,
     '黑政规〔2025〕1号第三档：伊春市区、黑河市区，全省其他各县（市）——林口县属本档。2025-12-01起执行，有效期三年。仅作事实参照，不得用于核定家庭收入。')
) AS v(standard_type, standard_name, standard_value, description)
WHERE NOT EXISTS (
    SELECT 1 FROM nc_config_standards s
    WHERE s.standard_type = v.standard_type AND s.is_active = TRUE
);

-- ----------------------------------------------------------------------------
-- 2. 软停用旧 MinWage = 1850（保留行，仅置 is_active = false + 记录停用原因）
-- ----------------------------------------------------------------------------
UPDATE nc_config_standards
SET is_active        = FALSE,
    effective_end_date = DATE '2025-11-30',
    description      = COALESCE(description, '') ||
        ' [已停用 2026-10-12] 该值为黑政规〔2024〕1号第二档，自2025-12-01起被黑政规〔2025〕1号取代；' ||
        '且林口县在2024版中属第三档（1750元），1850元系牡丹江市区档位，填档有误。' ||
        '现行三档见 MinWageLevel1/2/3。',
    updated_at       = NOW()
WHERE standard_type = 'MinWage'
  AND is_active = TRUE
  AND standard_value = 1850.00;

COMMIT;

-- ----------------------------------------------------------------------------
-- 3. 校验
-- ----------------------------------------------------------------------------
SELECT standard_type, standard_name, standard_value, unit,
       effective_start_date, effective_end_date, version, is_active
FROM nc_config_standards
WHERE standard_type IN ('MinWage', 'MinWageLevel1', 'MinWageLevel2', 'MinWageLevel3')
ORDER BY standard_type;
