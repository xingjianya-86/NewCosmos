-- 20261112 重病费用口径（黑龙政办法第六十八条）：费用字段 + 阈值配置
-- 数据可迁移，也可重复执行；SchemaService 也会建表/列（Resources\Schema\*\database.yaml 为权威）：
--   1) 费用字段：nc_biz_family_members / nc_biz_applications 各加 medical_self_paid_12m
--      （"申请之日前12个月个人累计负担"——住院+门诊慢特病+高值药品，经办人手工录入，元）
--   2) 阈值配置：nc_config_standards 两行 SevereDiseaseCostThreshold（Rural/Urban，
--      2025 年居民人均可支配收入 29381 / 41757 元）——与 Resources\Seed\config_standards\seed.yaml 同源，
--      标准配置管理页可编辑（"重病费用口径阈值"分类）；年度更新时改配置即可，勿改代码（§11）。
-- 规则单点：Services\Domain\SevereDisease\SevereDiseaseByCostRule（超过=严格大于；阈值缺失=不判定）。
-- 含数据操作（INSERT，幂等 WHERE NOT EXISTS），check_migrations 归人工审阅档。

BEGIN;

ALTER TABLE nc_biz_family_members ADD COLUMN IF NOT EXISTS medical_self_paid_12m NUMERIC(18,2);
ALTER TABLE nc_biz_applications  ADD COLUMN IF NOT EXISTS medical_self_paid_12m NUMERIC(18,2);

COMMENT ON COLUMN nc_biz_family_members.medical_self_paid_12m IS '申请前12个月个人医疗自付累计（元）：费用口径重病判定输入（第六十八条）';
COMMENT ON COLUMN nc_biz_applications.medical_self_paid_12m IS '申请前12个月个人医疗自付累计（元）：费用口径重病判定输入（第六十八条）';

INSERT INTO nc_config_standards
    (standard_type, standard_name, hukou_type, support_mode, standard_value, unit,
     effective_start_date, effective_end_date, is_active, description, created_at, updated_at)
SELECT 'SevereDiseaseCostThreshold', '重病费用口径阈值（上年度农村居民人均可支配收入）', 'Rural', NULL,
       29381.00, '元/年', DATE '2026-01-01', NULL, TRUE,
       '费用口径重病判定阈值-农村档：2025年全年人均可支配收入29381元；申请前12个月个人累计自付（住院+门诊慢特病+高值药品）超过本值即认定重病患者',
       NOW(), NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM nc_config_standards
    WHERE standard_type = 'SevereDiseaseCostThreshold' AND hukou_type = 'Rural'
);

INSERT INTO nc_config_standards
    (standard_type, standard_name, hukou_type, support_mode, standard_value, unit,
     effective_start_date, effective_end_date, is_active, description, created_at, updated_at)
SELECT 'SevereDiseaseCostThreshold', '重病费用口径阈值（上年度城镇居民人均可支配收入）', 'Urban', NULL,
       41757.00, '元/年', DATE '2026-01-01', NULL, TRUE,
       '费用口径重病判定阈值-城镇档：2025年全年人均可支配收入41757元；申请前12个月个人累计自付（住院+门诊慢特病+高值药品）超过本值即认定重病患者',
       NOW(), NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM nc_config_standards
    WHERE standard_type = 'SevereDiseaseCostThreshold' AND hukou_type = 'Urban'
);

COMMIT;
