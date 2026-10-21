-- 20261021 临时救助「审核审批表」大额版更名
-- ---------------------------------------------------------------------------
-- 背景：新增小额版审核审批表「临时救助_小额救助_审核审批表」（分类 临时救助/小额，
--       xlsx 版式含成员表 4 行 + 工作单位/年收入真值列，经 Scripts\import_templates.ps1 导入），
--       原大额版「临时救助_审核审批表2」更名为「临时救助_大额救助_审核审批表」以示区分。
--
-- 变更范围：**仅 name 一列**。分类（{临时救助/大额}）、copies=2、config_json、file_data、sort_order 均不变。
-- 幂等：按旧名 + 大额分类定位，重复执行为 no-op；改名后不再命中旧名条件。
--
-- ⚠️ 连带口径提示（无需回改，仅记录）：
--   · docs\migrations\20261020_temp_relief_audit_copies_2.sql 用 name LIKE '临时救助_审核审批表%'
--     命中两张表，改名后两类新名都不再匹配该前缀——该迁移已执行过（份数=2），不需重跑；
--     今后如再按名字筛选，应改用 categories 或 name LIKE '临时救助_%审核审批表'。
--   · 代码侧手机推送打印选模板（PrintJobFactory.PickPushPrintTemplate）原按前缀
--     「临时救助_审核审批表」匹配，改名后已同步改为「名称含“审核审批表”」（同版次代码提交）。
--
-- 执行：psql -w -v ON_ERROR_STOP=1 -f docs\migrations\20261021_temp_relief_audit_approval_rename.sql

BEGIN;

\pset pager off
\pset border 2

-- ── ① 执行前核对 ────────────────────────────────────────────────────────────
SELECT 'BEFORE' AS phase, id, name, categories::text, sort_order,
       (config_json ->> 'copies')::int AS copies, octet_length(file_data) AS bytes
FROM nc_biz_templates
WHERE name IN ('临时救助_审核审批表2', '临时救助_大额救助_审核审批表')
ORDER BY id;

-- ── ② 更名（幂等：仅当大额版仍是旧名、且目标名未被占用时执行）───────────────
UPDATE nc_biz_templates
   SET name = '临时救助_大额救助_审核审批表',
       updated_at = NOW()
 WHERE name = '临时救助_审核审批表2'
   AND categories @> ARRAY['临时救助/大额']
   AND NOT EXISTS (
        SELECT 1 FROM nc_biz_templates t
         WHERE t.name = '临时救助_大额救助_审核审批表'
   );

-- ── ③ 执行后核对（应为两行：大额=新名、小额=新导入）─────────────────────────
SELECT 'AFTER' AS phase, id, name, categories::text, sort_order,
       (config_json ->> 'copies')::int AS copies, octet_length(file_data) AS bytes
FROM nc_biz_templates
WHERE name LIKE '临时救助_%审核审批表%'
   OR name IN ('临时救助_审核审批表2', '临时救助_大额救助_审核审批表')
ORDER BY id;

COMMIT;
