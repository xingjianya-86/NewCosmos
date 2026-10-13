-- ============================================================================
-- 20261013_stop_archive_document_categories.sql
--
-- 目的：拒保/停保档案（分类码为停保族：RuralIncomeExceeded/UrbanIncomeExceeded/
--       IneligibleWithLabor/Ineligible/IneligibleOther）补齐卷宗必备文书模板挂码。
--
-- 背景（仲立亮 app95 实测错例）：
--   归档终结·拒保的独立新档（CompleteArchiveAsync 直接置 Stopped，无变更记录），
--   记录分类集 = ["IneligibleWithLabor"]（拒保码连"新增"位都不挂，见
--   Helpers\ArchiveCategoryResolver.GetRecordCategories）。以下三张模板的 categories
--   此前只挂享受类分类码，与停保码零交集 → 整档/文书/补打三条路径全部取不到：
--     159 档案_入户调查表   —— 调查数据已录入却永远打不出表；
--     208 档案_封面         —— 拒保卷宗无封面；
--     212 档案_目录         —— 拒保卷宗无目录。
--   （241 档案_变更告知书已自带停保码，不受影响。）
--
-- 方案：给上述三张模板 categories 追加 5 个停保码，与"告知书靠自己的停保码命中"
--       同构；不动任何代码选择逻辑（整档 GetByCategoriesAsync、文书模式补挂
--       ArchiveOutputViewModel L419、补打中心均自动生效）。
--
-- 影响面核对：
--   - 变动文书清单（DocumentOperationCategories）不含停保码 → 208/212/159 的新增
--     停保码不会灌入变动文书基础清单；文书模式补挂本就按 classification 取，
--     停保码档案由此能取到入户调查表（预期修复）。
--   - 月报/高龄/临时救助/资产核查等域按各自前缀分类查询，零交集，不受影响。
--   - 241 档案_变更告知书不动。
--
-- 幂等性：逐码补挂（只追加缺失码），重复执行不产生重复项。
-- 回滚：  见文件末尔回滚段（备份表 backup_20261013_template_categories）。
-- ============================================================================

BEGIN;

-- ----------------------------------------------------------------------------
-- 0. 备份将被改动的行（便于回滚；重复执行时先清掉上次的备份表）
-- ----------------------------------------------------------------------------
DROP TABLE IF EXISTS backup_20261013_template_categories;
CREATE TABLE backup_20261013_template_categories AS
SELECT id, name, categories, updated_at
FROM nc_biz_templates
WHERE name IN ('档案_入户调查表', '档案_封面', '档案_目录');

-- ----------------------------------------------------------------------------
-- 1. 逐码补挂 5 个停保码（缺失才追加 → 幂等）
-- ----------------------------------------------------------------------------
UPDATE nc_biz_templates t
SET categories = t.categories || (
        SELECT COALESCE(array_agg(c), ARRAY[]::text[])
        FROM unnest(ARRAY[
            'RuralIncomeExceeded', 'UrbanIncomeExceeded',
            'IneligibleWithLabor', 'Ineligible', 'IneligibleOther'
        ]::text[]) AS src(c)
        WHERE NOT (t.categories @> ARRAY[c])
    ),
    updated_at = NOW()
WHERE t.name IN ('档案_入户调查表', '档案_封面', '档案_目录');

COMMIT;

-- ----------------------------------------------------------------------------
-- 2. 校验：三张模板应各含 5 个停保码
-- ----------------------------------------------------------------------------
SELECT id, name, array_to_string(categories, ' | ') AS categories
FROM nc_biz_templates
WHERE name IN ('档案_入户调查表', '档案_封面', '档案_目录')
ORDER BY id;

-- ----------------------------------------------------------------------------
-- 回滚段（仅手工执行）
-- ----------------------------------------------------------------------------
-- UPDATE nc_biz_templates t
-- SET categories = b.categories, updated_at = NOW()
-- FROM backup_20261013_template_categories b
-- WHERE t.id = b.id AND t.name = b.name;
