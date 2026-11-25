-- 20261118_template138_date_format.sql
-- 一事一议申报表（T138 档案_乡镇社会救助一事一议申报表）日期格式统一为中文年月日。
-- 背景（2026-11-18 用户反馈）：申报表内所有日期格式错误，应为「2026年11月18日」式样。
--   档案输出/打印链路（PrintExecuteService.ApplyFormatOrDefault）：T138 config 的 AUDIT_DATE 条目
--   （{审核日期} / {审核确认时间} 两条，20261016_batch5 由 SPECIAL_APPROVAL_AUDIT_DATE 迁来）
--   未配 format → ApplyFormat 空 format 直返原值 → 输出 dict A/B 的 tl.AuditDate ISO 串（yyyy-MM-dd）。
-- 修法：与 T222 同款（见 20261016_batch5_solo_keys.sql 步骤2，按合并原则 R3 加 config format）：
--   jsonb_set 给 T138 fields 中 fieldKey='AUDIT_DATE' 的两条目补 format="yyyy年M月d日"。
--   ApplyFormat 识别 'y' 开头格式串 → DateTime.TryParse(ISO) → dt.ToString("yyyy年M月d日")，输出 = 2026年11月18日。
-- 另两条代码链路（不在本迁移范围，同批修复）：
--   SpecialApprovalFormPage.xaml 两个 DatePicker 加 Format="yyyy年M月d日"；
--   SpecialApprovalFormService.BuildFieldDataAsync confirmTime/auditDate 改 yyyy年M月d日（按钮直生成路径）。
-- 幂等：VALUES 式定位 + EXISTS 守卫，重复执行同值覆盖为 no-op。
-- 回滚：
--   UPDATE nc_biz_templates t SET config_json = jsonb_set(t.config_json, '{fields}', (
--     SELECT jsonb_agg(CASE WHEN e.value->>'fieldKey' = 'AUDIT_DATE'
--                           THEN (e.value - 'format') ELSE e.value END ORDER BY e.ord)
--     FROM jsonb_array_elements(t.config_json -> 'fields') WITH ORDINALITY AS e(value, ord)), true)
--   WHERE t.id = 138;
-- ============================================================================

BEGIN;

-- 1. T138 AUDIT_DATE 两条目（{审核日期}、{审核确认时间}）补 format
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE WHEN e.value->>'fieldKey' = 'AUDIT_DATE'
                        THEN jsonb_set(e.value, '{format}', '"yyyy年M月d日"')
                        ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id = 138
  AND EXISTS (SELECT 1 FROM jsonb_array_elements(config_json -> 'fields') x
              WHERE x->>'fieldKey' = 'AUDIT_DATE');

-- 2. 验收：期望 2 行，format 均为 yyyy年M月d日
SELECT f->>'fieldKey' AS field_key, f->>'placeholder' AS placeholder, f->>'format' AS format
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f
WHERE id = 138 AND f->>'fieldKey' = 'AUDIT_DATE';

COMMIT;
