-- 2026-11-08 死亡申报台账补列：规范化《死亡情况说明》五条目格式
--
-- 背景：文书格式改为规范五条目（死亡证明情况 / 遗体处置情况 / 惠民政策享受情况 /
--       家属情况及诉求 / 矛盾纠纷及信访隐患情况），其中条目一、二、四、五需要新采集字段；
--       条目三「惠民政策享受情况」由身份八源聚合（DeathIdentityAggregator）自动推导，
--       不新增落库列。结构化遗体处置采用火化分支 / 安葬分支二选一（表单互斥显隐），故两组列并存。
-- 权威定义：新列结构以 Resources/Schema/death_declarations/database.yaml 为单一事实来源，
--           本文件为部署期幂等副本（与 YAML 同步维护）。
-- 口径：全部可空——存量行与「只出文书」场景兼容；必填约束由应用层校验
--       （DeathDeclareValidator：矛盾纠纷必选、有则说明必填）。
-- 说明：只做加列/改注释，无 DROP/TRUNCATE；可重复执行（幂等）。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261108_death_statement_standard_format.sql

BEGIN;

-- ① 条目一：死亡证明情况
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS cert_type varchar(50);
COMMENT ON COLUMN nc_biz_death_declarations.cert_type IS '死亡证明类型（死亡医学证明 / 非正常死亡证明，模板《{证明类型}》）';

ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS cert_issue_org varchar(200);
COMMENT ON COLUMN nc_biz_death_declarations.cert_issue_org IS '死亡证明出具机构（模板「已由XX出具」）';

-- ② 条目二：遗体处置情况（火化分支 / 安葬分支二选一）
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS cremation_date date;
COMMENT ON COLUMN nc_biz_death_declarations.cremation_date IS '火化日期（火化分支）';

ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS cremation_place varchar(200);
COMMENT ON COLUMN nc_biz_death_declarations.cremation_place IS '火化地点/殡仪馆（火化分支）';

ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS cremation_cert_no varchar(50);
COMMENT ON COLUMN nc_biz_death_declarations.cremation_cert_no IS '火化证明编号（火化分支）';

ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS burial_date date;
COMMENT ON COLUMN nc_biz_death_declarations.burial_date IS '安葬日期（已安葬或拟安葬，安葬分支）';

ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS burial_place varchar(200);
COMMENT ON COLUMN nc_biz_death_declarations.burial_place IS '安葬地点/公益性墓地（安葬分支）';

-- ③ 条目四 / 条目五：家属诉求与矛盾纠纷
ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS family_situation text;
COMMENT ON COLUMN nc_biz_death_declarations.family_situation IS '家属情况及诉求（模板条目四，长文本）';

ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS dispute_status varchar(20);
COMMENT ON COLUMN nc_biz_death_declarations.dispute_status IS '矛盾纠纷及信访隐患（无 / 有，模板条目五勾选）';

ALTER TABLE nc_biz_death_declarations ADD COLUMN IF NOT EXISTS dispute_desc text;
COMMENT ON COLUMN nc_biz_death_declarations.dispute_desc IS '矛盾纠纷说明（dispute_status=有时必填，另附说明）';

COMMIT;

-- ④ 核对
\pset pager off
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'nc_biz_death_declarations'
  AND column_name IN ('cert_type', 'cert_issue_org', 'cremation_date', 'cremation_place',
                      'cremation_cert_no', 'burial_date', 'burial_place',
                      'family_situation', 'dispute_status', 'dispute_desc')
ORDER BY column_name;
