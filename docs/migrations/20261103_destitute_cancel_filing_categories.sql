-- ============================================================================
-- 20261103_destitute_cancel_filing_categories.sql
--
-- 目的：特困供养人员取消（停止）情况表（退出/停发时专用）模板分类落码兜底——
--       确保 nc_biz_templates 中「特困供养人员取消备案表」的 categories
--       含唯一叶子「档案_特困取消备案表」+「停止」位。
--
-- 背景：该模板经 Scripts\templates_manifest.json（categories=["档案_特困取消备案表","停止"]）
--       用 Scripts\import_templates.ps1 导入（同名先删后插，categories 随导入写入），
--       或经「模板管理」页手工上传并勾分类。本迁移是**幂等兜底**：无论模板由哪条路入的库，
--       只要分类缺漏即补齐（如早期手工上传漏勾分类、或 manifest categories 被改动后重导）。
--
-- 命中机制（与代码侧一致，勿改此处分类值）：
--   - 退出直出走 TemplateFilter 强白名单（DocumentTemplateNames.DestituteCancelFiling），
--     加载命中靠 DestituteCancelFilingService.OutputCategories=["档案_特困取消备案表","停止"]；
--   - 模板 categories 须与该集有交集才被 GetByCategoriesAsync 带出。
--   - 故意**不挂特困分类码**：避免在享特困整档（分类集含特困码）误选出此表（仅退出/停止场景出）。
--
-- 影响面核对：
--   - 「档案_特困取消备案表」为该模板独有叶子，其余模板不挂它 → 不影响其它模板选中。
--   - 「停止」位现有停保卷宗（封面/目录/变更告知书等）靠停保码命中，不冲突。
--   - 月报/高龄/临救/资产核查等域分类集不含这两值，零交集，不受影响。
--
-- 幂等性：array 追加只补缺失项（NOT (categories @> ARRAY[c])），重复执行不产生重复项。
-- 回滚：  见文件末尔回滚段（备份表 backup_20261103_cancel_filing_categories）。
-- ============================================================================

BEGIN;

-- ----------------------------------------------------------------------------
-- 0. 备份将被改动的行（便于回滚；重复执行时先清掉上次的备份表）
-- ----------------------------------------------------------------------------
DROP TABLE IF EXISTS backup_20261103_cancel_filing_categories;
CREATE TABLE backup_20261103_cancel_filing_categories AS
SELECT id, name, categories, updated_at
FROM nc_biz_templates
WHERE name = '特困供养人员取消备案表';

-- ----------------------------------------------------------------------------
-- 1. 逐项补挂缺失分类（缺失才追加 → 幂等；模板不存在时本句为 no-op）
-- ----------------------------------------------------------------------------
UPDATE nc_biz_templates t
SET categories = t.categories || (
        SELECT COALESCE(array_agg(c), ARRAY[]::text[])
        FROM unnest(ARRAY[
            '档案_特困取消备案表', '停止'
        ]::text[]) AS src(c)
        WHERE NOT (t.categories @> ARRAY[c])
    ),
    updated_at = NOW()
WHERE t.name = '特困供养人员取消备案表';

COMMIT;

-- ----------------------------------------------------------------------------
-- 2. 校验：应含 唯一叶子 +「停止」位
-- ----------------------------------------------------------------------------
SELECT id, name, array_to_string(categories, ' | ') AS categories
FROM nc_biz_templates
WHERE name = '特困供养人员取消备案表'
ORDER BY id;

-- ----------------------------------------------------------------------------
-- 回滚段（仅手工执行）
-- ----------------------------------------------------------------------------
-- UPDATE nc_biz_templates t
-- SET categories = b.categories, updated_at = NOW()
-- FROM backup_20261103_cancel_filing_categories b
-- WHERE t.id = b.id AND t.name = b.name;
