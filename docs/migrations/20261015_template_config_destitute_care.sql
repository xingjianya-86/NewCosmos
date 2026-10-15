-- 20261015_template_config_destitute_care.sql
-- 特困三张模板的 config_json 字段映射修正（占位符 → FieldKey），配合本批代码侧字段装配改造：
--
--   [216 档案_特困入户调查表_特困人员.xlsx]
--     {保障类型}     SURVEY_CLASSIFICATION → DESTITUTE_SUPPORT_TYPE
--                    （勾选式两值「☑分散供养 □集中供养」，原映射取分类简称，与需求不符）
--     {自理能力情况} DESTITUTE_SELF_CARE_ABILITY → DESTITUTE_SELF_CARE_DETAIL
--                    （能力鉴定六项逐项勾选明细，原为三档概要）
--
--   [221 档案_照料服务协议_特困人员.docx]
--     {经办人}       新增映射 → OPERATOR_NAME
--                    （文件中存在该占位符但 config_json 缺映射，导致原样残留——用户报"未能成功读取"）
--     {协议期起}     SUPPORT_START_DATE → CARE_AGREEMENT_START_DATE（生成日，原为集中供养协议键）
--     {协议期止}     SUPPORT_END_DATE → CARE_AGREEMENT_END_DATE（本年12月31日，协议按自然年限制在年内）
--     {诚信承诺时间} AUDIT_DATE → CARE_AGREEMENT_PROMISE_TIME（生成日 yyyy年M月d日，承诺时间=签署当日）
--     注：{所在县}{所在镇} 仍走 DISTRICT/TOWN；其未替换根因是占位符位于文本框（w:txbxContent），
--         已由 WordEngine StoryRanges 遍历修复，不改映射。
--
--   [222 档案_分散供养协议_特困人员.docx]
--     {审核确认时间} AUDIT_DATE → SCATTERED_AUDIT_TIME（业务审核确认日，格式 2026年10月15日，
--                    原映射输出 yyyy-MM-dd，与需求格式不符）
--
-- 幂等：CASE 改键可重复执行；{经办人} 追加带 NOT EXISTS 守卫。
-- 回滚：UPDATE nc_biz_templates SET config_json = b.config_json
--       FROM backup_20261015_templates_config b WHERE nc_biz_templates.id = b.id;
-- ============================================================================

BEGIN;

-- 0. 备份将被改动的三行 config_json（重复执行时先清掉上次的备份表）
DROP TABLE IF EXISTS backup_20261015_templates_config;
CREATE TABLE backup_20261015_templates_config AS
SELECT id, name, file_type, config_json, updated_at
FROM nc_biz_templates
WHERE id IN (216, 221, 222);

-- 1. [216] 保障类型 → 勾选式供养方式；自理能力情况 → 六项明细
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE e.value->>'placeholder'
                       WHEN '{保障类型}'     THEN jsonb_set(e.value, '{fieldKey}', '"DESTITUTE_SUPPORT_TYPE"')
                       WHEN '{自理能力情况}' THEN jsonb_set(e.value, '{fieldKey}', '"DESTITUTE_SELF_CARE_DETAIL"')
                       ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id = 216;

-- 2. [221] 协议期起/止改走自然年键；诚信承诺时间改走生成日键
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE e.value->>'placeholder'
                       WHEN '{协议期起}'     THEN jsonb_set(e.value, '{fieldKey}', '"CARE_AGREEMENT_START_DATE"')
                       WHEN '{协议期止}'     THEN jsonb_set(e.value, '{fieldKey}', '"CARE_AGREEMENT_END_DATE"')
                       WHEN '{诚信承诺时间}' THEN jsonb_set(e.value, '{fieldKey}', '"CARE_AGREEMENT_PROMISE_TIME"')
                       ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id = 221;

-- 2b. [221] 新增 {经办人} → OPERATOR_NAME（文件内存在、映射缺失的占位符）
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}',
        (config_json -> 'fields') ||
        '[{"fieldKey": "OPERATOR_NAME", "description": "经办人", "placeholder": "{经办人}"}]'::jsonb,
        true),
    updated_at = NOW()
WHERE id = 221
  AND NOT (config_json -> 'fields' @> '[{"placeholder": "{经办人}"}]'::jsonb);

-- 3. [222] 审核确认时间 → 中文年月日格式键
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE e.value->>'placeholder'
                       WHEN '{审核确认时间}' THEN jsonb_set(e.value, '{fieldKey}', '"SCATTERED_AUDIT_TIME"')
                       ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id = 222;

COMMIT;

-- 校验：应看到 DESTITUTE_SUPPORT_TYPE / DESTITUTE_SELF_CARE_DETAIL / CARE_AGREEMENT_* /
--       OPERATOR_NAME({经办人}) / SCATTERED_AUDIT_TIME
SELECT id, e.value->>'fieldKey' AS field_key, e.value->>'placeholder' AS placeholder
FROM nc_biz_templates,
     jsonb_array_elements(config_json -> 'fields') AS e(value)
WHERE id IN (216, 221, 222)
  AND e.value->>'fieldKey' IN ('DESTITUTE_SUPPORT_TYPE', 'DESTITUTE_SELF_CARE_DETAIL',
                               'CARE_AGREEMENT_START_DATE', 'CARE_AGREEMENT_END_DATE',
                               'CARE_AGREEMENT_PROMISE_TIME', 'SCATTERED_AUDIT_TIME', 'OPERATOR_NAME')
ORDER BY id, e.value->>'placeholder';
