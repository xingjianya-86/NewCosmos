-- 20261016_template_keys_ascii.sql
-- 批2：T240/T252 的 config_json->fields[].fieldKey 由中文迁为 ASCII（= Constants\FieldKeys.Recovery.cs 常量值）。
-- 配套代码侧：RECOVERY_* 10 个 + GP_* 15 个常量值同批改 ASCII；常量名与全部供给点表达式未动。
-- 只改 fieldKey；placeholder（模板中文占位符）/defaultValue/format 一律不动 -> 打印值零变化。
-- 迁移前核过的影响面：20 个中文键全库仅此 20 行（每键恰 1 行、仅属 240/252）；
--   config_json 的 compositeFields / tables 两容器均为空数组；
--   代码侧无中文字面量字典键、无 {"..."} 占位符直键绕过（逐键 grep 全 .cs 核过）。
-- 幂等：CASE 只匹配中文旧键 -> 重复执行时已 ASCII 落 ELSE 分支，no-op。
-- 回滚：
--   UPDATE nc_biz_templates SET config_json = b.config_json, updated_at = NOW()
--   FROM backup_20261016_template_keys b WHERE nc_biz_templates.id = b.id;
--   代码侧回滚文件：backup/20261016_163129_Batch2_中文键值迁移/FieldKeys.Recovery.cs
-- ============================================================================

BEGIN;

-- 0. 备份将被改动的两行 config_json（重复执行时先清旧备份表）
DROP TABLE IF EXISTS backup_20261016_template_keys;
CREATE TABLE backup_20261016_template_keys AS
SELECT id, name, file_type, config_json, updated_at
FROM nc_biz_templates
WHERE id IN (240, 252);

-- 240 档案_追缴资金办理单：10 个 fieldKey 中文->ASCII
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE e.value->>'fieldKey'
                       WHEN '编号' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_SEQ"')
                       WHEN '户主身份证号' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_ID_CARD"')
                       WHEN '户主享受分类' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_SOURCE_TYPE"')
                       WHEN '户主姓名' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_PERSON_NAME"')
                       WHEN '上报时间' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_REPORT_DATE"')
                       WHEN '实追缴金额' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERED_AMOUNT"')
                       WHEN '应追缴金额' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_AMOUNT"')
                       WHEN '追缴编码' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_CODE"')
                       WHEN '追缴单位' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_UNIT"')
                       WHEN '追缴起止时间' THEN jsonb_set(e.value, '{fieldKey}', '"RECOVERY_RANGE"')
                       ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id = 240;

-- 252 档案_渐退期审批表：10 个 fieldKey 中文->ASCII
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
        SELECT jsonb_agg(
                   CASE e.value->>'fieldKey'
                       WHEN '渐退期启止时间' THEN jsonb_set(e.value, '{fieldKey}', '"GP_PERIOD_RANGE"')
                       WHEN '渐退期长度' THEN jsonb_set(e.value, '{fieldKey}', '"GP_PERIOD_MONTHS"')
                       WHEN '退出渐退期情况' THEN jsonb_set(e.value, '{fieldKey}', '"GP_EXIT_SITUATION"')
                       WHEN '详细说明享受对象家庭人员、收入、财产状况变动情况' THEN jsonb_set(e.value, '{fieldKey}', '"GP_CHANGE_DETAIL"')
                       WHEN '新电话号码' THEN jsonb_set(e.value, '{fieldKey}', '"GP_PHONE"')
                       WHEN '新户主身份证号' THEN jsonb_set(e.value, '{fieldKey}', '"GP_HEAD_ID_CARD"')
                       WHEN '新户主性别' THEN jsonb_set(e.value, '{fieldKey}', '"GP_HEAD_GENDER"')
                       WHEN '新户主姓名' THEN jsonb_set(e.value, '{fieldKey}', '"GP_HEAD_NAME"')
                       WHEN '新家庭人口' THEN jsonb_set(e.value, '{fieldKey}', '"GP_FAMILY_SIZE"')
                       WHEN '新家庭住址' THEN jsonb_set(e.value, '{fieldKey}', '"GP_ADDRESS"')
                       ELSE e.value
                   END
                   ORDER BY e.ord)
        FROM jsonb_array_elements(config_json -> 'fields') WITH ORDINALITY AS e(value, ord)
    ), true),
    updated_at = NOW()
WHERE id = 252;

-- 1. 验收（迁移后应为 0；迁移前 240=10 / 252=10）
SELECT id,
       jsonb_array_length(config_json -> 'fields') AS fields_n,
       (SELECT count(*) FROM jsonb_array_elements(config_json -> 'fields') x
        WHERE x->>'fieldKey' !~ '^[A-Z0-9_]+$') AS non_ascii_n
FROM nc_biz_templates
WHERE id IN (240, 252);

COMMIT;
