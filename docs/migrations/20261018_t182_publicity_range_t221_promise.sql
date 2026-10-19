-- 2026-10-18  模板 config 口径统一（B 线公示起止 / 特困照料服务协议诚信承诺时间）
--
-- T182 档案_公示文件：{公示日期}  由 REPORT_DATE(CURRENT_DATE=打印当天, format=yyyy年MM月)
--                                 换绑为 PUBLICITY_RANGE（"公示起 至 公示止"合成串，代码内已中文格式化，format 置空）。
--                                 与 {公示开始时间}/{公示结束时间}(PUBLICITY_START/END_DATE) 同源，补打不再随打印当天漂移。
-- T221 档案_照料服务协议_特困人员：{诚信承诺时间} 由 CARE_AGREEMENT_PROMISE_TIME(生成日, 我方 DateTime.Now)
--                                 换绑为 PROMISE_DATE（现有口径：首次审批日，未审批兜底申请日、已批钳下限公示起），
--                                 与 174/178/196/200 同源；PROMISE_DATE 值为 ISO，故补 format=yyyy年MM月dd日。
--
-- 依赖代码同批：FieldKeys.PUBLICITY_RANGE 新增 + ArchiveProductionViewModel 两处 _fieldData 供 PUBLICITY_RANGE（BuildPublicityRange）；
--              FieldKeys.CARE_AGREEMENT_PROMISE_TIME 常量与两处供给已删。
-- 回滚：backup\20261018_194416_T182公示起止_T221承诺换绑\rollback_T182_T221_config.sql

BEGIN;

UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
    SELECT jsonb_agg(
             CASE WHEN e->>'fieldKey' = 'REPORT_DATE'
                  THEN jsonb_set(jsonb_set(e, '{fieldKey}', '"PUBLICITY_RANGE"'), '{format}', '""')
                  ELSE e END
             ORDER BY ord)
    FROM jsonb_array_elements(config_json->'fields') WITH ORDINALITY AS a(e, ord)))
WHERE id = 182;

UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{fields}', (
    SELECT jsonb_agg(
             CASE WHEN e->>'fieldKey' = 'CARE_AGREEMENT_PROMISE_TIME'
                  THEN jsonb_set(jsonb_set(e, '{fieldKey}', '"PROMISE_DATE"'), '{format}', to_jsonb('yyyy年MM月dd日'::text))
                  ELSE e END
             ORDER BY ord)
    FROM jsonb_array_elements(config_json->'fields') WITH ORDINALITY AS a(e, ord)))
WHERE id = 221;

-- 校验（应各返回 1 行）
SELECT t.id, t.name, f->>'fieldKey' AS field_key, f->>'placeholder' AS placeholder, f->>'format' AS fmt
FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
WHERE (t.id = 182 AND f->>'fieldKey' = 'PUBLICITY_RANGE')
   OR (t.id = 221 AND f->>'fieldKey' = 'PROMISE_DATE')
ORDER BY t.id;

COMMIT;
