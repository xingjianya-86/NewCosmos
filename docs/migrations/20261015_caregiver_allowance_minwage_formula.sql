-- 20261015_caregiver_allowance_minwage_formula.sql
-- 照料护理费改为「当地最低工资 ÷ 10 / ÷ 6 / ÷ 4（四舍五入到元）」公式计算后的存量配置对齐。
--   依据：《牡丹江市低收入人口认定实施细则》第四十五条 + 黑政规〔2025〕1号（林口县第三档 1910 元/月）。
--   变更后运行时计算优先（ClassificationService.CalculateCareAllowanceAsync），
--   CaregiverAllowance* 三行仅作最低工资缺配置时的回退值——本脚本把回退值对齐公式结果：
--     1910÷10 = 191（不变）；1910÷6 = 318.33 → 318（原 319 与公式不符）；1910÷4 = 477.5 → 478（不变）。
--   说明：只改回退标准配置行，不触碰任何已落库的保障金/护理费业务数据（AGENTS §9.7 禁止自动重算）。
-- 幂等：WHERE 条件保证重复执行不重复修改。
-- 回滚：UPDATE ... SET standard_value = 319.00 WHERE standard_type = 'CaregiverAllowancePartial';
-- ============================================================================

BEGIN;

-- 备份将被改动的既有行（重复执行时先清掉上次的备份表）
DROP TABLE IF EXISTS backup_20261015_caregiver_allowance_rows;
CREATE TABLE backup_20261015_caregiver_allowance_rows AS
SELECT * FROM nc_config_standards WHERE standard_type IN
    ('CaregiverAllowanceFull', 'CaregiverAllowancePartial', 'CaregiverAllowanceNone');

-- 1. 半自理档回退值 319 → 318（1910÷6 四舍五入到元）
UPDATE nc_config_standards
SET standard_value = 318.00,
    description    = '特困人员照料护理费-半自理（回退值：当地最低工资÷6，1910÷6=318）',
    updated_at     = NOW()
WHERE standard_type = 'CaregiverAllowancePartial'
  AND standard_value = 319.00;

-- 2. 三档描述标注公式口径（幂等追加：已有标注的行不再追加）
UPDATE nc_config_standards
SET description    = CASE standard_type
        WHEN 'CaregiverAllowanceFull'  THEN '特困人员照料护理费-全自理（回退值：当地最低工资÷10）'
        WHEN 'CaregiverAllowanceNone'  THEN '特困人员照料护理费-无法自理（回退值：当地最低工资÷4）'
        ELSE description END,
    updated_at     = NOW()
WHERE standard_type IN ('CaregiverAllowanceFull', 'CaregiverAllowanceNone')
  AND (description IS NULL OR description NOT LIKE '%回退值%');

COMMIT;

-- 校验：三档回退值应为 191 / 318 / 478；最低工资三档应为 2270 / 2010 / 1910
SELECT standard_type, standard_name, standard_value, unit, is_active, description
FROM nc_config_standards
WHERE standard_type IN ('CaregiverAllowanceFull', 'CaregiverAllowancePartial', 'CaregiverAllowanceNone',
                        'MinWageLevel1', 'MinWageLevel2', 'MinWageLevel3')
ORDER BY standard_type;
