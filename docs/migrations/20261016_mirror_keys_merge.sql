-- 20261016_mirror_keys_merge.sql
-- 批3：MIRROR 机制 5 规则 / 18 行 config_json->fields[].fieldKey 合并（只改名，不改值）。
-- 机制：TempReliefPrintDataBuilder.BuildSingleFields 与 ArchiveProductionViewModel(713/1803)
--   在同一字典内新旧键同值并存（逐对 RHS 已核：L42/L94、L45/L95、L47/L48、L49/L104、L713/714）
--   -> config 指向新键即可取到同值，打印值零变化。
-- 代码侧随后只删「无任何读取方」的冗余行：TEMP_FAMILY_ADDRESS / TEMP_HUKOU_ADDRESS / SURVEY_TIME；
--   TEMP_APPLICANT_NAME、TEMP_APPLICANT_ID_CARD 两行保留（ArchiveOutputViewModel:910/937/1059 读取，
--   用于档案题名与版权信息，删除会使题名回退「未知」）。
-- 影响面已核：5 个旧键全库 config 行数与工单一一吻合（7/3/3/3/2），删读取方=0。
-- 幂等：CASE 只匹配旧键，重复执行落 ELSE 分支 no-op。
-- 回滚：
--   UPDATE nc_biz_templates SET config_json = b.config_json, updated_at = NOW()
--   FROM backup_20261016_mirror_keys b WHERE nc_biz_templates.id = b.id;
--   （代码侧回滚：把删掉的 4 行按下方清单加回 TempReliefPrintDataBuilder / ArchiveProductionViewModel）
-- ============================================================================

BEGIN;

-- 0. 备份受影响模板的 config_json
DROP TABLE IF EXISTS backup_20261016_mirror_keys;
CREATE TABLE backup_20261016_mirror_keys AS
SELECT id, name, file_type, config_json, updated_at
FROM nc_biz_templates
WHERE id IN (226, 229, 230, 235, 236, 237, 238, 249, 254, 255);

-- 1. 合并 5 规则 / 18 行（模板 226, 229, 230, 235, 236, 237, 238, 249, 254, 255）
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE e.value->>'fieldKey'
                       WHEN 'SURVEY_TIME' THEN jsonb_set(e.value, '{fieldKey}', '"SURVEY_DATE"')
                       WHEN 'TEMP_APPLICANT_ID_CARD' THEN jsonb_set(e.value, '{fieldKey}', '"APPLICANT_ID_CARD"')
                       WHEN 'TEMP_APPLICANT_NAME' THEN jsonb_set(e.value, '{fieldKey}', '"APPLICANT_NAME"')
                       WHEN 'TEMP_FAMILY_ADDRESS' THEN jsonb_set(e.value, '{fieldKey}', '"FAMILY_ADDRESS"')
                       WHEN 'TEMP_HUKOU_ADDRESS' THEN jsonb_set(e.value, '{fieldKey}', '"HUKOU_ADDRESS"')
                       ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id IN (226, 229, 230, 235, 236, 237, 238, 249, 254, 255);

-- 2. 验收
-- 2a. 5 个旧键全库剩余行数（期望 0）
SELECT count(*) AS old_key_rows_left
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f
WHERE f->>'fieldKey' IN ('SURVEY_TIME','TEMP_APPLICANT_ID_CARD','TEMP_APPLICANT_NAME','TEMP_FAMILY_ADDRESS','TEMP_HUKOU_ADDRESS');

-- 2b. 非 ASCII 键（期望 0）与总行数（期望 3314）
SELECT count(*) FILTER (WHERE f->>'fieldKey' !~ '^[A-Z0-9_]+$') AS non_ascii,
       count(*) AS total_rows
FROM nc_biz_templates, jsonb_array_elements(config_json -> 'fields') f;

COMMIT;
