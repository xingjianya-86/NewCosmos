-- 2026-10-26 死亡申报台账补「死亡情况核实说明」要素（7 列）
--
-- 背景：《死亡情况核实说明》模板需要 现场报告人 / 现场所见情况 / 遗体安置 / 业务办理人·事项 等
--       核实说明要素（见 docs/20261026_死亡情况说明与申报台账规范.md §6）。
--       这些是申报内容的一部分，必须落库：否则三联出件后无法补打、无法审计。
-- 口径：
--   · 报告人（reporter_*）与申报人（declared_by_*）**语义独立、分别手填**（2026-10-26 决策）；
--   · 全部可空：历史申报行与"只出文书不落库"场景兼容；新申报由表单必填策略控制；
--   · 不新增唯一约束，不改既有列。
-- 说明：只做加列，无 DROP/TRUNCATE；可重复执行（幂等）。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261026_death_declaration_verify_details.sql

BEGIN;

ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS reporter_name          varchar(100);
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS reporter_relation      varchar(50);
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS reporter_phone         varchar(20);
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS situation_desc         text;
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS remains_arrangement    text;
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS business_agent         varchar(100);
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS business_matter        varchar(200);

COMMENT ON COLUMN nc_biz_death_declarations.reporter_name       IS '发现（报告）人姓名——与申报人独立，模板{死者报告人}';
COMMENT ON COLUMN nc_biz_death_declarations.reporter_relation   IS '报告人与死者关系——模板{死者报告人与死者关系}';
COMMENT ON COLUMN nc_biz_death_declarations.reporter_phone      IS '报告人联系电话——模板{死者报告人联系方式}';
COMMENT ON COLUMN nc_biz_death_declarations.situation_desc      IS '现场所见情况描述（长文本）——模板{死者情况描述}';
COMMENT ON COLUMN nc_biz_death_declarations.remains_arrangement IS '遗体安置情况（长文本）——模板{死亡人遗体安置情况}';
COMMENT ON COLUMN nc_biz_death_declarations.business_agent      IS '业务办理人——模板{业务办理人}';
COMMENT ON COLUMN nc_biz_death_declarations.business_matter     IS '相关业务事项（如：申请居民死亡医学证明）——模板{相关业务事项}';

COMMIT;

-- ② 核对
\pset pager off
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'nc_biz_death_declarations'
  AND column_name IN ('reporter_name','reporter_relation','reporter_phone',
                      'situation_desc','remains_arrangement','business_agent','business_matter')
ORDER BY ordinal_position;
