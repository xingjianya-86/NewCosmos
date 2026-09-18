-- 20260918 回填 nc_biz_applications.created_at / updated_at 并补齐列默认值
-- 背景：created_at 为 NULL 时，应用实体带 DateTime.UtcNow 初值，映射层跳过 NULL 赋值，
--       导致档案打印/补打时"申请时间"取到加载时刻（每次打印都变）。
--       本次按申请编号内嵌日期（SA+yyyyMMdd，创建时生成）回填，兜底首次审批/补全/更新时间。
-- 影响行：11 条（id 48,49,50,51,53,54,55,56,57,58,62），执行前已导出 affected_rows_before.csv。

-- 1) created_at：申请编号 SA+yyyyMMdd 优先
UPDATE nc_biz_applications
SET created_at = COALESCE(
      CASE WHEN application_no ~ '^SA[0-9]{8}'
           THEN to_timestamp(substring(application_no from 3 for 8), 'YYYYMMDD')::timestamp END,
      first_approved_at, data_completed_at, updated_at, NOW())
WHERE created_at IS NULL;

-- 2) updated_at 为空回填为 created_at
UPDATE nc_biz_applications
SET updated_at = created_at
WHERE updated_at IS NULL AND created_at IS NOT NULL;

-- 3) 列默认值与 Resources\Schema\applications\database.yaml 的 defaultValue: NOW() 对齐
ALTER TABLE nc_biz_applications ALTER COLUMN created_at SET DEFAULT NOW();
ALTER TABLE nc_biz_applications ALTER COLUMN updated_at SET DEFAULT NOW();
