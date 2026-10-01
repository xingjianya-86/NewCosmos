-- 20260926 临时救助模板"申请人"字段改为户主信息
--
-- 背景：临时救助数据模型铁律「户主恒为 applicant_*，不得变更」
-- （Models/Entities/TempReliefEntities.cs 注释、Resources/Schema/temp_relief/database.yaml）。
-- 但两个模板的 config_json 把 {申请人姓名}/{申请人性别}/{申请人年龄} 映射到了
-- 救助对象键 TEMP_BENEFICIARY_*（可能是家庭成员），打印结果不是户主。
--
-- 修复：按 placeholder 改指户主键（TempReliefPrintDataBuilder 已产出，无需改代码）：
--   id=226 临时救助_入户调查表：姓名/性别/年龄 → TEMP_APPLICANT_NAME/GENDER/AGE
--   id=230 临时救助_验收报告：  {申请人姓名}   → TEMP_APPLICANT_NAME
--         （该模板 {申请人年龄} 本就是 TEMP_APPLICANT_AGE，改后自洽）
--
-- 不动：{申请人家庭住址}=TEMP_FAMILY_ADDRESS 等其余字段；
--       id=229 临时救助_信息公示 {救助者姓名}=TEMP_BENEFICIARY_NAME（语义即救助对象）。
--
-- 幂等：EXISTS 仅当存在仍指向 TEMP_BENEFICIARY_% 的目标占位符才更新；
--       CASE 按 placeholder 匹配，重复执行重写同值。
-- 回滚：backup/<yyyyMMdd_HHmmss>_入户调查表申请人字段改户主/{226,230}_before.json
--       按原文 UPDATE config_json::jsonb = '<before.json>'::jsonb 即可。

UPDATE nc_biz_templates t
SET config_json = jsonb_set(
  t.config_json,
  '{fields}',
  (
    SELECT jsonb_agg(
      CASE
        WHEN elem->>'placeholder' = '{申请人姓名}' THEN jsonb_set(elem, '{fieldKey}', '"TEMP_APPLICANT_NAME"'::jsonb)
        WHEN elem->>'placeholder' = '{申请人性别}' THEN jsonb_set(elem, '{fieldKey}', '"TEMP_APPLICANT_GENDER"'::jsonb)
        WHEN elem->>'placeholder' = '{申请人年龄}' THEN jsonb_set(elem, '{fieldKey}', '"TEMP_APPLICANT_AGE"'::jsonb)
        ELSE elem
      END
      ORDER BY ord
    )
    FROM jsonb_array_elements(t.config_json->'fields') WITH ORDINALITY AS u(elem, ord)
  )
)
WHERE t.id IN (226, 230)
  AND EXISTS (
    SELECT 1 FROM jsonb_array_elements(t.config_json->'fields') e
    WHERE e->>'placeholder' IN ('{申请人姓名}','{申请人性别}','{申请人年龄}')
      AND e->>'fieldKey' LIKE 'TEMP_BENEFICIARY_%'
  );

-- 验证：
-- SELECT id, elem->>'placeholder', elem->>'fieldKey'
-- FROM nc_biz_templates t, jsonb_array_elements(t.config_json->'fields') elem
-- WHERE t.id IN (226,230) AND elem->>'placeholder' LIKE '{申请人%';
