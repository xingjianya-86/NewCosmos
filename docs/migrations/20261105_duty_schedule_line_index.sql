-- 值班轮转锚点索引（2026-11-05）
-- 背景：月度值班表生成的起点已改为「目标月之前最近一条真实班次」
--       （DutyService.Schedule.cs -> LoadPreviousRotationFactsAsync）：
--       SELECT DISTINCT ON (group_code, date_type) ... FROM nc_duty_schedules
--       WHERE duty_date < $1::date ... ORDER BY group_code, date_type, duty_date DESC
--       原索引 uq_duty_schedules(duty_date, group_code) / idx_duty_schedules_year_month(year, month)
--       都不含 date_type 维度，删除中间月份、跨节假日空档补生成时只能整表扫描 + 排序。
-- 幂等：CREATE INDEX IF NOT EXISTS，可重复执行。
-- 同步：本文件与 Resources/Schema/duty_management/database.yaml 的 idx_duty_schedules_line_date 保持一致
--       （SchemaService 启动幂等补建，本迁移供未跑 Schema 修复的现网先行执行）。
-- 执行：psql -w -v ON_ERROR_STOP=1 -f docs/migrations/20261105_duty_schedule_line_index.sql

CREATE INDEX IF NOT EXISTS idx_duty_schedules_line_date
    ON nc_duty_schedules (group_code, date_type, duty_date);
