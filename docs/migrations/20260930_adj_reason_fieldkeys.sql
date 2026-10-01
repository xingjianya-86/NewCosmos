-- ============================================================================
-- 2026-09-30 档案_增员减员调整表 增减原因逐行独立
--
-- 背景：模板 255 中 {增原因1/2/3} 与 {减原因1/2/3} 六个占位符共用
--       ADJ_ADD_REASON / ADJ_REMOVE_REASON 两个 fieldKey（历史"3行同值"设计），
--       导致打印时 6 格显示同一段 change_reason 原文（含长句与英文码）。
-- 变更：按 placeholder 精确拆分为 ADJ_ADD_REASON_1/2/3、ADJ_REMOVE_REASON_1/2/3；
--       代码侧 FillAdjustSlot 逐行写入该行人员的短原因（≤6 字）。
-- 幂等：@> 守卫仅当 fieldKey 仍是旧值时重写，重复执行无副作用。
-- 执行：psql -v ON_ERROR_STOP=1 -f <本文件>（开发库先执行，随发版脚本带出）
-- ============================================================================

SET client_encoding TO 'UTF8';

BEGIN;

-- 变更前（6 个原因占位符应全部指向 2 个旧 fieldKey）
SELECT t.id, e.ord, e.e->>'placeholder' AS placeholder, e.e->>'fieldKey' AS field_key
FROM nc_biz_templates t,
     jsonb_array_elements(t.config_json->'fields') WITH ORDINALITY AS e(e, ord)
WHERE t.id = 255 AND e.e->>'fieldKey' IN ('ADJ_ADD_REASON', 'ADJ_REMOVE_REASON')
ORDER BY e.ord;

UPDATE nc_biz_templates t
SET config_json = jsonb_set(t.config_json, '{fields}',
      COALESCE((SELECT jsonb_agg(
                    CASE
                      WHEN e->>'placeholder' = '{增原因1}' THEN jsonb_set(e, '{fieldKey}', to_jsonb('ADJ_ADD_REASON_1'::text)) || '{"description": "增员原因第1行"}'::jsonb
                      WHEN e->>'placeholder' = '{增原因2}' THEN jsonb_set(e, '{fieldKey}', to_jsonb('ADJ_ADD_REASON_2'::text)) || '{"description": "增员原因第2行"}'::jsonb
                      WHEN e->>'placeholder' = '{增原因3}' THEN jsonb_set(e, '{fieldKey}', to_jsonb('ADJ_ADD_REASON_3'::text)) || '{"description": "增员原因第3行"}'::jsonb
                      WHEN e->>'placeholder' = '{减原因1}' THEN jsonb_set(e, '{fieldKey}', to_jsonb('ADJ_REMOVE_REASON_1'::text)) || '{"description": "减员原因第1行"}'::jsonb
                      WHEN e->>'placeholder' = '{减原因2}' THEN jsonb_set(e, '{fieldKey}', to_jsonb('ADJ_REMOVE_REASON_2'::text)) || '{"description": "减员原因第2行"}'::jsonb
                      WHEN e->>'placeholder' = '{减原因3}' THEN jsonb_set(e, '{fieldKey}', to_jsonb('ADJ_REMOVE_REASON_3'::text)) || '{"description": "减员原因第3行"}'::jsonb
                      ELSE e END ORDER BY ord)
                  FROM jsonb_array_elements(t.config_json->'fields') WITH ORDINALITY AS f(e, ord)), '[]'::jsonb)),
    updated_at = NOW()
WHERE t.id = 255
  AND t.config_json->'fields' @> '[{"placeholder": "{增原因1}", "fieldKey": "ADJ_ADD_REASON"}]';

-- 变更后（6 个原因占位符应各指向独立 fieldKey）
SELECT t.id, e.ord, e.e->>'placeholder' AS placeholder, e.e->>'fieldKey' AS field_key
FROM nc_biz_templates t,
     jsonb_array_elements(t.config_json->'fields') WITH ORDINALITY AS e(e, ord)
WHERE t.id = 255 AND e.e->>'placeholder' IN ('{增原因1}', '{增原因2}', '{增原因3}', '{减原因1}', '{减原因2}', '{减原因3}')
ORDER BY e.ord;

COMMIT;
