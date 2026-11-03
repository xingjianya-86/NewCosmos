-- 2026-11-01 工作流列表性能索引（批3性能收口）
-- 背景：工作流 Tab2（草稿 status='Draft' AND current_step<5）/ Tab4（已完结 current_step=6）
--       是档案量最大子集，原索引无 current_step 维度（见 Resources/Schema/applications/database.yaml
--       idx_applications_* 清单），列表 ORDER BY updated_at DESC 只能顺序扫描 + 排序。
-- 幂等：CREATE INDEX IF NOT EXISTS，可重复执行。
-- 同步：本文件与 Resources/Schema/applications/database.yaml 的 indexes 段保持一致
--       （SchemaService 启动幂等补建，本迁移供未跑 Schema 修复的现网先行执行）。

CREATE INDEX IF NOT EXISTS idx_applications_draft_step_updated
    ON nc_biz_applications (updated_at DESC)
    WHERE status = 'Draft' AND current_step < 5 AND deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_applications_archived_step_updated
    ON nc_biz_applications (updated_at DESC)
    WHERE current_step = 6 AND deleted_at IS NULL;
