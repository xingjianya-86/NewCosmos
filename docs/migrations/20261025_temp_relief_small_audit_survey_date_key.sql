-- 20261025 临时救助_小额救助_审核审批表：{申请救助时间} 规则改回 {入户调查时间} 并改绑动态调查日
-- ---------------------------------------------------------------------------
-- 背景：2026-10-25 16:33 重传模板 xlsx（单元格占位符 = {入户调查时间}，业务确认以此为准），
--       但模板管理「上传」会连同编辑器里的 config_json 一并写回，导致 config 侧仍是
--       {申请救助时间} → TEMP_APPLY_DATE：占位符与单元格对不上（打印会留字面量 {入户调查时间}），
--       且键指申请日（历史打印 10-22/10-24/10-25 均为申请日），业务要的是 C 线动态调查日
--       （杨忠士这单：clamp(今日最近工作日, [验收日11-13−5天=11-08周日→顺延11-09, 公示起11-10]) = 2026年11月9日）。
--
-- 变更：fields[] 中 placeholder='{申请救助时间}' 且 fieldKey='TEMP_APPLY_DATE' 的一条 →
--       placeholder/description='{入户调查时间}'、fieldKey='TEMP_INVESTIGATION_DATE'
--       （= TempReliefPrintDataBuilder:132 供给的动态调查日；与模板 226 {申请日期}、238 {申请时间} 同键同口径）。
--
-- 不变：fields 条数 35、copies=2、categories={临时救助/小额}、sort_order=234、file_data。
--       TEMP_APPLY_DATE 常量与 Builder:120 的供给保留（改后本模板不再引用，但删了会牵动其它域，勿删）。
--
-- 幂等：仅旧状态命中（重复执行 = no-op）；转换前断言 {入户调查时间} 尚无条目，防同占位符双键后写覆盖（MultiSource）。
-- 执行：psql -w -v ON_ERROR_STOP=1 -f docs\migrations\20261025_temp_relief_small_audit_survey_date_key.sql

BEGIN;

\pset pager off
\pset border 2

-- ── ① 改前核对 ────────────────────────────────────────────────────────────
SELECT 'BEFORE' AS phase, t.id,
       jsonb_array_length(t.config_json->'fields')                    AS n_fields,
       (t.config_json->>'copies')::int                                AS copies,
       t.categories::text                                             AS categories,
       t.sort_order,
       octet_length(t.file_data)                                      AS file_bytes,
       (SELECT count(*) FROM jsonb_array_elements(t.config_json->'fields') f
         WHERE f->>'placeholder' = '{申请救助时间}')                    AS ph_apply_cnt,
       (SELECT count(*) FROM jsonb_array_elements(t.config_json->'fields') f
         WHERE f->>'placeholder' = '{入户调查时间}')                    AS ph_survey_cnt
  FROM nc_biz_templates t
 WHERE t.id = 262;

-- ── ② 断言 + 改写（单事务内，任一断言失败即整体回滚）─────────────────────
DO $$
DECLARE
    v_old  int;
    v_dup  int;
    v_rows int;
BEGIN
    -- 待转换的旧规则条数
    SELECT count(*) INTO v_old
      FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
     WHERE t.id = 262
       AND f->>'placeholder' = '{申请救助时间}'
       AND f->>'fieldKey'    = 'TEMP_APPLY_DATE';

    IF v_old = 0 THEN
        RAISE NOTICE 'SKIP: 目标规则不存在或已是新状态（幂等跳过，未做任何修改）';
    ELSE
        -- 转换前 {入户调查时间} 必须为 0 条，否则同占位符双键会后写覆盖、值会串
        SELECT count(*) INTO v_dup
          FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
         WHERE t.id = 262 AND f->>'placeholder' = '{入户调查时间}';

        IF v_dup > 0 THEN
            RAISE EXCEPTION '断言失败：待转换时 {入户调查时间} 已存在 % 条（MultiSource，同占位符多键）', v_dup;
        END IF;

        IF v_old > 1 THEN
            RAISE EXCEPTION '断言失败：{申请救助时间} 规则 % 条（应恰好 1 条）', v_old;
        END IF;

        UPDATE nc_biz_templates t
           SET config_json = jsonb_set(
                   t.config_json,
                   '{fields}',
                   (SELECT jsonb_agg(
                               CASE WHEN f->>'placeholder' = '{申请救助时间}'
                                     AND f->>'fieldKey'    = 'TEMP_APPLY_DATE'
                                    THEN jsonb_set(
                                            jsonb_set(
                                                jsonb_set(f, '{placeholder}', to_jsonb('{入户调查时间}'::text)),
                                                '{fieldKey}',    to_jsonb('TEMP_INVESTIGATION_DATE'::text)),
                                            '{description}', to_jsonb('{入户调查时间}'::text),
                                            true)
                                    ELSE f END
                               ORDER BY u.ord)
                      FROM jsonb_array_elements(t.config_json->'fields')
                           WITH ORDINALITY AS u(f, ord)),
                   true),
               updated_at = NOW()
         WHERE t.id = 262;

        GET DIAGNOSTICS v_rows = ROW_COUNT;
        RAISE NOTICE 'UPDATED: % 行', v_rows;
    END IF;

    -- ── 改后硬校验（不满足即抛异常回滚）──────────────────────────────────
    IF (SELECT jsonb_array_length(config_json->'fields') FROM nc_biz_templates WHERE id = 262) <> 35 THEN
        RAISE EXCEPTION '校验失败：fields 条数不再是 35';
    END IF;

    IF (SELECT count(*) FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
         WHERE t.id = 262 AND f->>'placeholder' = '{入户调查时间}' AND f->>'fieldKey' = 'TEMP_INVESTIGATION_DATE') <> 1 THEN
        RAISE EXCEPTION '校验失败：{入户调查时间} → TEMP_INVESTIGATION_DATE 不是恰好 1 条';
    END IF;

    IF (SELECT count(*) FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
         WHERE t.id = 262 AND f->>'placeholder' = '{申请救助时间}') <> 0 THEN
        RAISE EXCEPTION '校验失败：{申请救助时间} 残留（应为 0 条）';
    END IF;

    IF (SELECT (config_json->>'copies')::int FROM nc_biz_templates WHERE id = 262) <> 2 THEN
        RAISE EXCEPTION '校验失败：copies 不再是 2';
    END IF;

    IF (SELECT sort_order FROM nc_biz_templates WHERE id = 262) <> 234 THEN
        RAISE EXCEPTION '校验失败：sort_order 不再是 234';
    END IF;

    IF (SELECT categories @> ARRAY['临时救助/小额'] FROM nc_biz_templates WHERE id = 262) IS NOT TRUE THEN
        RAISE EXCEPTION '校验失败：categories 丢失 临时救助/小额';
    END IF;
END $$;

-- ── ③ 改后核对 ────────────────────────────────────────────────────────────
SELECT 'AFTER' AS phase, t.id,
       jsonb_array_length(t.config_json->'fields')                    AS n_fields,
       (t.config_json->>'copies')::int                                AS copies,
       t.categories::text                                             AS categories,
       t.sort_order,
       octet_length(t.file_data)                                      AS file_bytes,
       t.updated_at::timestamp(0)                                     AS updated_at
  FROM nc_biz_templates t
 WHERE t.id = 262;

SELECT 'RULE' AS phase,
       (f->>'placeholder')   AS placeholder,
       (f->>'fieldKey')      AS fieldKey,
       (f->>'description')   AS description
  FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') f
 WHERE t.id = 262
   AND ((f->>'placeholder') IN ('{入户调查时间}', '{申请救助时间}')
        OR (f->>'fieldKey') IN ('TEMP_INVESTIGATION_DATE', 'TEMP_APPLY_DATE'));

COMMIT;
