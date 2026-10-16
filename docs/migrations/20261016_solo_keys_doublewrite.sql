-- 20261016_solo_keys_doublewrite.sql
-- 批4：SOLO 类「先双写后迁移」—— 7 规则 / 12 行（A 类 11 SOLO + 1 条 A/MIXED 折入，见下）。
-- 前置（铁律4：先代码后迁 DB）：以下双写已于同日完成并 dotnet build 0 警告 0 错误：
--   DynamicManagementRecordService.cs  +CURRENT_DATE / +APPLICANT_NAME / +HUKOU_ADDRESS（DM_ 旧键旁）
--   ElderlyPrintDataBuilder.cs         +HUKOU_ADDRESS（ELDERLY_HUKOU_ADDRESS 旁）
--   ArchiveProductionViewModel.cs      +COMMUNITY ×2（NOTICE_VILLAGE 两分支旁）
--   TempReliefPrintDataBuilder.cs      +FAMILY_SIZE（TEMP_FAMILY_SIZE 旁，覆盖 T229/230/235/237/249）
-- 每处双写的 RHS 与旧键同源同值（逐处已核：DM_CURRENT_DATE/CURRENT_DATE 同格式串、
-- DM_HEAD_NAME/APPLICANT_NAME 同 app.ApplicantName、DM_HEAD_HUKOU/HUKOU_ADDRESS 同 app.HukouAddress、
-- ELDERLY_HUKOU_ADDRESS/HUKOU_ADDRESS 同 app.HukouAddress、NOTICE_VILLAGE/COMMUNITY 同 app.Community、
-- TEMP_FAMILY_SIZE/FAMILY_SIZE 同 app.FamilySize），迁移后打印值零变化。
-- T222 FAMILY_VILLAGE→COMMUNITY（原判 A/MIXED）折入本批：其服务字典为 ArchiveProductionViewModel
--   dict A（LoadApplicationDataAsync L678-993）或 dict B（L1777-1935），两者本就含 COMMUNITY
--   （L684/L1783 同源同值；HUKOU_TYPE_DISPLAY L932 + SCATTERED_AUDIT_TIME L975 证明 dict A 服务 T222）
--   -> 零代码依赖，仅迁 config。计划原批4 计数为 11 行，此处 +1 行为偏离留痕（A 类必须清零）。
-- 影响面已核：7 个旧键全库恰 12 行、分布 10 模板，与工单 CSV 一一吻合，无遗漏模板/误伤他行。
-- 幂等：CASE 只匹配旧键，重复执行落 ELSE 分支 no-op。
-- 回滚：
--   UPDATE nc_biz_templates SET config_json = b.config_json, updated_at = NOW()
--   FROM backup_20261016_solo_keys b WHERE nc_biz_templates.id = b.id;
--   （代码侧回滚：删去 4 个文件中标注「批4 双写」的 7 行）
-- ============================================================================

BEGIN;

-- 0. 备份受影响模板的 config_json
DROP TABLE IF EXISTS backup_20261016_solo_keys;
CREATE TABLE backup_20261016_solo_keys AS
SELECT id, name, file_type, config_json, updated_at
FROM nc_biz_templates
WHERE id IN (140, 143, 222, 229, 230, 235, 237, 241, 248, 249);

-- 1. 迁移 7 规则 / 12 行（按模板限定，双保险：即便将来旧键出现在别的模板也不受影响）
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE
                       WHEN id = 248 AND e.value->>'fieldKey' = 'DM_CURRENT_DATE'
                           THEN jsonb_set(e.value, '{fieldKey}', '"CURRENT_DATE"')
                       WHEN id = 248 AND e.value->>'fieldKey' = 'DM_HEAD_NAME'
                           THEN jsonb_set(e.value, '{fieldKey}', '"APPLICANT_NAME"')
                       WHEN id = 248 AND e.value->>'fieldKey' = 'DM_HEAD_HUKOU'
                           THEN jsonb_set(e.value, '{fieldKey}', '"HUKOU_ADDRESS"')
                       WHEN id IN (140, 143) AND e.value->>'fieldKey' = 'ELDERLY_HUKOU_ADDRESS'
                           THEN jsonb_set(e.value, '{fieldKey}', '"HUKOU_ADDRESS"')
                       WHEN id = 241 AND e.value->>'fieldKey' = 'NOTICE_VILLAGE'
                           THEN jsonb_set(e.value, '{fieldKey}', '"COMMUNITY"')
                       WHEN id IN (229, 230, 235, 237, 249) AND e.value->>'fieldKey' = 'TEMP_FAMILY_SIZE'
                           THEN jsonb_set(e.value, '{fieldKey}', '"FAMILY_SIZE"')
                       WHEN id = 222 AND e.value->>'fieldKey' = 'FAMILY_VILLAGE'
                           THEN jsonb_set(e.value, '{fieldKey}', '"COMMUNITY"')
                       ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id IN (140, 143, 222, 229, 230, 235, 237, 241, 248, 249);

-- 2. 验收
-- 2a. 7 个旧键全库剩余行数（期望 0）
SELECT count(*) AS old_key_rows_left
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f
WHERE f->>'fieldKey' IN ('DM_CURRENT_DATE','DM_HEAD_NAME','DM_HEAD_HUKOU',
                         'ELDERLY_HUKOU_ADDRESS','NOTICE_VILLAGE','TEMP_FAMILY_SIZE','FAMILY_VILLAGE');

-- 2b. 非 ASCII 键（期望 0）与总行数（期望 3314）
SELECT count(*) FILTER (WHERE f->>'fieldKey' !~ '^[A-Z0-9_]+$') AS non_ascii,
       count(*) AS total_rows
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f;

-- 2c. 迁移后受影响模板中新增的同键别名（同 fieldKey 多 placeholder，属预期，仅报告）
SELECT t.id, f->>'fieldKey' AS dup_key, count(*) AS rows_n
FROM nc_biz_templates t, jsonb_array_elements(t.config_json -> 'fields') f
WHERE t.id IN (140, 143, 222, 229, 230, 235, 237, 241, 248, 249)
GROUP BY t.id, f->>'fieldKey'
HAVING count(*) > 1
ORDER BY t.id, 2;

COMMIT;
