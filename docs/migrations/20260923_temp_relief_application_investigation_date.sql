-- ============================================================================
-- 2026-09-23 临时救助申请书时间口径对齐入户调查表
--
-- 背景：临时救助_申请书（nc_biz_templates id=238）的 {申请时间} 原映射
--       TEMP_APPLY_DATE（申请人填报的申请日），与临时救助_入户调查表（id=226）
--       的"时间规范"（{申请日期}/{入户调查日期} 均取 TEMP_INVESTIGATION_DATE
--       = C线入户调查核实窗口首日）不一致，同一档案两份文书日期打架。
--       例：某档案 申请书=2026年9月22日，入户调查表=2026年9月28日。
-- 变更：将模板 238 中 placeholder={申请时间} 的 fieldKey 改为 TEMP_INVESTIGATION_DATE。
-- 幂等：仅重写该元素 fieldKey，其余元素与顺序不变；重复执行无副作用。
-- 执行：psql -v ON_ERROR_STOP=1 -f <本文件>
-- ============================================================================

SET client_encoding TO 'UTF8';

BEGIN;

-- 变更前（{申请时间} 应为 TEMP_APPLY_DATE）
SELECT id, name, config_json FROM nc_biz_templates WHERE id = 238;

UPDATE nc_biz_templates t
SET config_json = jsonb_set(
        t.config_json,
        '{fields}',
        COALESCE((
            SELECT jsonb_agg(
                       CASE WHEN e->>'placeholder' = '{申请时间}'
                            THEN jsonb_set(e, '{fieldKey}', to_jsonb('TEMP_INVESTIGATION_DATE'::text))
                            ELSE e
                       END
                       ORDER BY ord)
            FROM jsonb_array_elements(t.config_json->'fields') WITH ORDINALITY AS f(e, ord)
        ), '[]'::jsonb)),
    updated_at = NOW()
WHERE t.id = 238
  AND t.config_json->'fields' @> '[{"placeholder": "{申请时间}"}]';

-- 变更后（{申请时间} 应为 TEMP_INVESTIGATION_DATE）
SELECT id, name, config_json FROM nc_biz_templates WHERE id = 238;

COMMIT;
