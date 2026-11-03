-- ============================================================================
-- 20261102_template_changenotice_leaf.sql
--
-- 目的：给模板 241《档案_变更告知书》补挂变动分类集叶子「档案_变更告知书」，
--       使其进入补打中心 / 输出文书「变动文书」的基础清单（不再只依赖按名追加门）。
--
-- 背景（奚德芳 231025197109015226，2026-11-02 实测）：
--   「变动文书」按 ArchiveCategoryResolver.DocumentOperationCategories（9 个变动分类）取模板，
--   而 241 的 categories 只有 5 个停保码 + 「新增」，与变动分类集零交集 → 基础清单永远取不到它，
--   只能靠 ArchiveOutputViewModel 的"按名追加门"补；追加门一旦因事实快照未取到而静默不成立，
--   清单里就没有告知书（当日日志：既无 GetByName 命中、也无任何告警，排查无从下手）。
--
-- 模板唯一性：全库只有这一个告知书模板；"停保 / 不予认定 / 终止 / 待遇调整"是同一模板的
--   不同方向设定，方向由 ArchiveProductionViewModel 装配端按事实填 NOTICE_*。
--   本迁移只影响"清单里出不出这个模板"，**不碰方向**。
--
-- 与同轮代码改动的配合：
--   - ChangeDocumentFacts.NoticeApplicable 仍是唯一事实口径（停保 / 归档终结拒保兜底 /
--     户主死亡整户停 / 待遇调整，含单人保排除），挂上叶子后由
--     ChangeDocumentApplicabilityService.IsTemplateApplicable（ChangeNotice => NoticeApplicable）
--     在清单侧承担同一过滤，业务规则不变；
--   - 事实快照查询失败 → 不做过滤（宁多打不漏打），与原追加门降级语义一致。
--
-- 影响面核对：
--   - 「变动文书」清单：多出 241（受 NoticeApplicable 过滤）；
--   - 整档路径（GetRecordCategories）：241 原本就靠停保码/「新增」命中，多一个叶子无重复
--     （ArchiveOutputViewModel 按 TemplateId 去重）；
--   - 批量「指定模板」下拉（GetReprintDomainCategories 已含变动分类集）：原本靠「新增」已能选到，无变化；
--   - 月报 / 高龄 / 临时救助 / 资产核查等域按各自前缀分类查询，零交集，不受影响。
--
-- 幂等性：逐值追加（缺失才追加），重复执行不产生重复项。
-- 回滚：  见文件末尔回滚段（备份表 backup_20261102_template_categories）。
-- ============================================================================

BEGIN;

-- ----------------------------------------------------------------------------
-- 0. 备份将被改动的行（重复执行时先清掉上次的备份表）
-- ----------------------------------------------------------------------------
DROP TABLE IF EXISTS backup_20261102_template_categories;
CREATE TABLE backup_20261102_template_categories AS
SELECT id, name, categories, updated_at
FROM nc_biz_templates
WHERE name = '档案_变更告知书';

-- ----------------------------------------------------------------------------
-- 1. 追加变动叶子（缺失才追加 → 幂等）
-- ----------------------------------------------------------------------------
UPDATE nc_biz_templates t
SET categories = t.categories || ARRAY['档案_变更告知书'],
    updated_at = NOW()
WHERE t.name = '档案_变更告知书'
  AND NOT (t.categories @> ARRAY['档案_变更告知书']);

COMMIT;

-- ----------------------------------------------------------------------------
-- 2. 校验：241 应为 5 个停保码 + 新增 + 档案_变更告知书（7 项）
-- ----------------------------------------------------------------------------
SELECT id, name, array_to_string(categories, ' | ') AS categories,
       array_length(categories, 1) AS cnt
FROM nc_biz_templates
WHERE name = '档案_变更告知书';

-- ----------------------------------------------------------------------------
-- 回滚段（仅手工执行）
-- ----------------------------------------------------------------------------
-- UPDATE nc_biz_templates t
-- SET categories = b.categories, updated_at = NOW()
-- FROM backup_20261102_template_categories b
-- WHERE t.id = b.id AND t.name = b.name;
