-- 2026-10-20 临时救助「审核审批表」默认打印份数 1 → 2
--
-- 份数口径唯一事实来源：Services\Templates\ITemplateEngine.cs → TemplateConfig.ResolveCopies
--     copiesByClassification[分类]（分类级，最高优先） → copies（模板级默认） → 1
-- 档案输出页（保存后打印 / 列表补打）与手机端推送打印（PrintJobFactory）共用这一条解析，
-- 故只需给模板 config_json 加 copies 键，两处同时生效。
--
-- 目标模板：249 临时救助_审核审批表2（分类：临时救助/大额、临时救助/小额）。
--   手机端推送打印同样打这一张（PrintJobFactory.EnqueueTempReliefAsync 固定取
--   TempReliefConstants.TemplateAuditApprovalPrefix 前缀模板，旧逻辑按 sort_order 会误取「信息公示」）。
--
-- 幂等：jsonb || 合并只写/覆盖 copies 键，fields / tables / compositeFields / copiesByClassification 原样保留。
-- 生效：TemplateService.GetByIdAsync 无缓存，写完即生效，无需重启应用。

BEGIN;

UPDATE nc_biz_templates
SET config_json = COALESCE(config_json, '{}'::jsonb) || jsonb_build_object('copies', 2),
    updated_at = NOW()
WHERE name LIKE '临时救助_审核审批表%'
  AND COALESCE(config_json ->> 'copies', '0') IS DISTINCT FROM '2';

-- 复核：期望 copies=2，且 copies_by_classification 仍为 NULL（未配置分类级份数）
SELECT id, name,
       config_json ->> 'copies'          AS copies,
       config_json -> 'copiesByClassification' AS copies_by_classification,
       updated_at
FROM nc_biz_templates
WHERE name LIKE '临时救助_审核审批表%';

COMMIT;
