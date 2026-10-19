-- ============================================================================
-- 2026-10-17 近亲属备案模板更正（配套应用侧：FieldKeys 新增 NEAR_RELATIVE_SUMMARY_RESIDENCE；
--                    NearRelativePrintDataBuilder 增供给；打印/月报口径详见
--                    docs\20261017_近亲属备案档案联动与身份证带出.md §1.9）
--
-- 更正内容（模板 151~154 均为 xlsx）：
--   1) 152《档案_近亲属_救助对象信息表》：占位符"户主月保证金"→"户主月保障金"（config 两处 + xlsx 单元格）
--   2) 154《月报_近亲属_工作人员批量汇总表》：占位符"救助报账金额"→"救助保障金额"（config 18 处 + xlsx K 列 9 格）
--   3) 154："居住地址"列（H5:H13）复制粘贴错误——原挂 {救助身份证号N}（会打印身份证号）→ 改为 {救助居住地址N}；
--      config 新增 9 组 NEAR_RELATIVE_SUMMARY_RESIDENCE_N 映射
--
-- 执行情况：2026-10-17 已在 new_cosmos 库执行；
--   xlsx 二进制（file_data/file_hash）经本机脚本同步修正，更正后 SHA256：
--     152: ff4ae18a7594e591447823e978bf435a124feadc6611c587c1868ff2d3c50e9e
--     154: ad375a11714ed9146da6af4d00f0c7c572c707402468c757fa1e6cd38905d9a1
--   体检：Scripts\check_template_fields.ps1 通过（Unknown/NonAscii/MultiSource = 0）
-- ============================================================================

BEGIN;

-- 更正前快照（回滚依据；重复执行安全）
CREATE TABLE IF NOT EXISTS backup_20261017_nearrelative_templates AS
SELECT id, name, file_type, file_data, file_hash, config_json, updated_at, now() AS backed_up_at
FROM nc_biz_templates WHERE id IN (151, 152, 153, 154);

-- 1) 152 config 文案（幂等：改后不再命中源串）
UPDATE nc_biz_templates
SET config_json = replace(config_json::text, '{户主月保证金}', '{户主月保障金}')::jsonb,
    updated_at = now()
WHERE id = 152;

-- 2)+3) 154 config：金额文案 + 新增居住地址映射（幂等：已含 RESIDENCE 键则跳过）
WITH src AS (
  SELECT id, replace(config_json::text, '救助报账金额', '救助保障金额')::jsonb AS cfg
  FROM nc_biz_templates
  WHERE id = 154 AND config_json::text NOT LIKE '%NEAR_RELATIVE_SUMMARY_RESIDENCE%'
)
UPDATE nc_biz_templates t
SET config_json = jsonb_set(
      src.cfg,
      '{fields}',
      (src.cfg -> 'fields') || jsonb_build_array(
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_1','description','{救助居住地址1}','placeholder','{救助居住地址1}'),
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_2','description','{救助居住地址2}','placeholder','{救助居住地址2}'),
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_3','description','{救助居住地址3}','placeholder','{救助居住地址3}'),
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_4','description','{救助居住地址4}','placeholder','{救助居住地址4}'),
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_5','description','{救助居住地址5}','placeholder','{救助居住地址5}'),
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_6','description','{救助居住地址6}','placeholder','{救助居住地址6}'),
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_7','description','{救助居住地址7}','placeholder','{救助居住地址7}'),
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_8','description','{救助居住地址8}','placeholder','{救助居住地址8}'),
        jsonb_build_object('fieldKey','NEAR_RELATIVE_SUMMARY_RESIDENCE_9','description','{救助居住地址9}','placeholder','{救助居住地址9}')
      )
    ),
    updated_at = now()
FROM src
WHERE t.id = src.id;

COMMIT;

-- 复核查询（应为 0 / 2 / 0 / 18 / 9）：
--   SELECT id, regexp_count(config_json::text,'{户主月保证金}') AS a,
--             regexp_count(config_json::text,'{户主月保障金}') AS b,
--             regexp_count(config_json::text,'救助报账金额') AS c,
--             regexp_count(config_json::text,'救助保障金额') AS d,
--             regexp_count(config_json::text,'NEAR_RELATIVE_SUMMARY_RESIDENCE') AS e
--   FROM nc_biz_templates WHERE id IN (152,154) ORDER BY id;
--
-- 回滚（如需恢复更正前状态；xlsx 与 config 一并回写）：
--   UPDATE nc_biz_templates t
--   SET file_data = b.file_data, file_hash = b.file_hash, config_json = b.config_json, updated_at = b.updated_at
--   FROM backup_20261017_nearrelative_templates b WHERE t.id = b.id;
