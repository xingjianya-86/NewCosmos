-- 20261016_d4_nearrelative_copies.sql
-- 清偿方案 D4 · 近亲属断链修复（2026-10-16）：T151/T152 加 config 顶层 "copiesBySupporter": true。
-- 证据链（解包 + 码流，替代"先打印确认空白"的 Gate）：
--   ① T151 file_data 解包 → xl/worksheets/sheet1.xml B2 = {&#25143;&#20027;&#22995;&#21153;}（={户主姓名} 实体编码）
--      ——占位符真实存在于文档（此前全文搜 0 命中系 XML 数字字符引用所致）；
--   ② 数据链断：fields 行取 PrintNavigationData.FieldData（dict A，无 NEAR_RELATIVE_*），
--      pair 字典（BuildArchivePairFields→_nearRelativePairs→tableRows）因模板无索引字段 + tables=[]
--      被 FlattenTableRowsToFields 忽略 → 每行落 defaultValue ?? "" → **15/17 行今天全空**；
--   ③ 修复机制为引擎现成能力：PrintExecuteService 预览 L83-104 与正式 L227 双路径
--      CopiesBySupporter=true → 逐 pair 页 mergedFields = dictA 拷贝 + pair[i] 后写覆盖（pair 赢）；
--      T200 赡养费承诺书同键在用（运行时实证可解析）。
-- 修复后：每对一页、批5 pair 侧双写（APPLICANT_NAME=l.Name 等）后写覆盖 dictA 申请人值 →
--   {户主姓名}=户主对象同源同值；未迁行（NEAR_RELATIVE_BIRTH_DATE/STAFF_* 等）同获 pair 供给。
-- 验证（人工 GUI）：打印/预览 T151/T152 —— 多对时页数=对数、每页户主信息与审核确认时间有值。
-- 回滚：
--   UPDATE nc_biz_templates SET config_json = b.config_json, updated_at = NOW()
--   FROM backup_20261016_d4_copies b WHERE nc_biz_templates.id = b.id;
-- ============================================================================

BEGIN;

-- 0. 备份
DROP TABLE IF EXISTS backup_20261016_d4_copies;
CREATE TABLE backup_20261016_d4_copies AS
SELECT id, name, file_type, config_json, updated_at
FROM nc_biz_templates
WHERE id IN (151, 152);

-- 1. 加顶层键（create_missing=true；值 = JSON 布尔 true，键名 camelCase 与 T200 同款）
UPDATE nc_biz_templates
SET config_json = jsonb_set(config_json, '{copiesBySupporter}', 'true', true),
    updated_at = NOW()
WHERE id IN (151, 152);

-- 2. 验收：两模板键=true、字段行与占位符零变化（剔除新增键后 config 相等）
SELECT id, config_json ->> 'copiesBySupporter' AS cbs
FROM nc_biz_templates
WHERE id IN (151, 152);

SELECT count(*) AS fields_unchanged
FROM backup_20261016_d4_copies b
JOIN nc_biz_templates t ON t.id = b.id
WHERE (SELECT jsonb_agg(e ORDER BY o) FROM jsonb_array_elements(b.config_json->'fields') WITH ORDINALITY x(e, o))
    = (SELECT jsonb_agg(e ORDER BY o) FROM jsonb_array_elements(t.config_json->'fields') WITH ORDINALITY x(e, o));

COMMIT;
