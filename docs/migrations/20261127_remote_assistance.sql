-- 2026-11-27 异地协助功能：nc_biz_applications 增加 application_type
-- 说明：异地协助是低收入认定流程的轻流程分支（仅基本信息 + 入户调查 + 邻里走访文书），
--      通过 application_type 隔离，不进入月报/首页统计/工作流/正式档案查询。
--      本脚本幂等，可重复执行。

-- 1. 增加业务类型列（存量数据默认 LowIncome）
ALTER TABLE nc_biz_applications
    ADD COLUMN IF NOT EXISTS application_type VARCHAR(30) DEFAULT 'LowIncome';

-- 2. 回填存量 NULL（防御，正常不会出现）
UPDATE nc_biz_applications
   SET application_type = 'LowIncome'
 WHERE application_type IS NULL;

-- 3. 异地协助专用部分索引（数据量小，按 updated_at DESC 排序，用于列表分页）
CREATE INDEX IF NOT EXISTS idx_applications_remote_assistance
    ON nc_biz_applications (updated_at DESC)
    WHERE application_type = 'RemoteAssistance' AND deleted_at IS NULL;

-- 4. 邻里访问调查表模板 doc_type 调整为「邻里访问」（便于精准筛选两张文书）
--    按模板名称匹配，覆盖现有已上传的模板；新上传模板在管理界面选「邻里访问」即可
UPDATE nc_biz_templates
   SET doc_type = '邻里访问'
 WHERE name LIKE '%邻里访问调查表%'
   AND doc_type IS DISTINCT FROM '邻里访问';
