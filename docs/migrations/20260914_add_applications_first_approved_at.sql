-- 首次审批时间：nc_biz_applications.first_approved_at
-- 目的：把"新增 / 审批时间"统计从可变的 updated_at 解耦到不可变的首次审批时间，
--       避免任何一次编辑/分类判定/变更把老档案重算成"本月新增"。
-- 执行：psql -h <host> -U new_cosmos -d new_cosmos -f 20260914_add_applications_first_approved_at.sql

ALTER TABLE nc_biz_applications ADD COLUMN IF NOT EXISTS first_approved_at timestamp without time zone;
COMMENT ON COLUMN nc_biz_applications.first_approved_at IS
  '首次成为保障对象（归档/审批通过）时间；新增及审批时间统计口径，写入一次不覆盖';
CREATE INDEX IF NOT EXISTS idx_applications_first_approved_at ON nc_biz_applications(first_approved_at);

-- 回填（存量无真实审批时间，用不可变的 created_at 近似；created_at 为空的旧行回退 updated_at；Draft 保持 NULL）
UPDATE nc_biz_applications
   SET first_approved_at = COALESCE(created_at, updated_at)
 WHERE deleted_at IS NULL
   AND first_approved_at IS NULL
   AND status IN ('Approved', 'Completed', 'Stopped');

-- 校验
SELECT status, count(*) AS total, count(first_approved_at) AS with_first_approved
FROM nc_biz_applications
WHERE deleted_at IS NULL
GROUP BY status
ORDER BY status;
