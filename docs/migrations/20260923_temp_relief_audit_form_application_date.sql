-- ============================================================================
-- 2026-09-23 临时救助审核审批表 申请时间对齐入户调查表
--
-- 背景：审核审批表(nc_biz_templates id=235) 的 {申请日期} 原映射 TEMP_APPLY_DATE
--       （申请人填报的申请日），与入户调查表"时间规范"（TEMP_INVESTIGATION_DATE =
--       C线入户调查核实窗口首日）不一致。
-- 变更：将模板 235 中 placeholder={申请日期} 的 fieldKey 改为 TEMP_INVESTIGATION_DATE。
-- 幂等：仅重写该元素 fieldKey，其余元素与顺序不变；重复执行无副作用。
-- 执行：psql -v ON_ERROR_STOP=1 -f <本文件>
--
-- 注：审核审批表2(id=249) 曾同步加入 {申请日期} 占位符，后按要求回退，不在本迁移范围内。
-- ============================================================================

SET client_encoding TO 'UTF8';

BEGIN;

-- 变更前（{申请日期} 应为 TEMP_APPLY_DATE）
SELECT id, name, config_json FROM nc_biz_templates WHERE id = 235;

UPDATE nc_biz_templates t
SET config_json = jsonb_set(t.config_json, '{fields}',
      COALESCE((SELECT jsonb_agg(CASE WHEN e->>'placeholder' = '{申请日期}'
                    THEN jsonb_set(e, '{fieldKey}', to_jsonb('TEMP_INVESTIGATION_DATE'::text))
                    ELSE e END ORDER BY ord)
                FROM jsonb_array_elements(t.config_json->'fields') WITH ORDINALITY AS f(e, ord)), '[]'::jsonb)),
    updated_at = NOW()
WHERE t.id = 235 AND t.config_json->'fields' @> '[{"placeholder": "{申请日期}"}]';

-- 变更后（{申请日期} 应为 TEMP_INVESTIGATION_DATE）
SELECT id, name, config_json FROM nc_biz_templates WHERE id = 235;

COMMIT;
