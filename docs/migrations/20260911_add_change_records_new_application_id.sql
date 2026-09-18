-- 变更记录：新增 new_application_id（旧档案→新档案链接），用于补打中心新旧档案查询
-- 执行：psql -h <host> -U new_cosmos -d new_cosmos -f 20260911_add_change_records_new_application_id.sql

ALTER TABLE nc_biz_change_records ADD COLUMN IF NOT EXISTS new_application_id bigint;
COMMENT ON COLUMN nc_biz_change_records.new_application_id IS '变更后新档案ID（停旧建新时写入，旧→新链接）';
CREATE INDEX IF NOT EXISTS idx_change_records_new_application_id ON nc_biz_change_records(new_application_id);

-- 回填：按旧档案反查现役新档案（同一旧档案的多条记录填同一新档案ID）
UPDATE nc_biz_change_records cr
SET new_application_id = c.id
FROM nc_biz_applications c
WHERE c.original_application_id = cr.application_id
  AND c.deleted_at IS NULL
  AND cr.deleted_at IS NULL;

-- 去重：解维平旧档案 44 因跨大类补写产生 FundChange(18)+CategoryStop(19) 两条，
-- 业务上旧档案只应有一条退出/链接记录——保留 CategoryStop(19)，物理删除重复的 FundChange(18) 及其快照/明细。
DELETE FROM nc_biz_change_details  WHERE change_id = 18;
DELETE FROM nc_biz_change_snapshots WHERE change_id = 18;
DELETE FROM nc_biz_change_records  WHERE id = 18;

-- 校验
SELECT 'link_rows' AS k, count(*) AS v FROM nc_biz_change_records WHERE deleted_at IS NULL AND new_application_id IS NOT NULL
UNION ALL
SELECT 'distinct_old_links', count(DISTINCT application_id) FROM nc_biz_change_records WHERE deleted_at IS NULL AND new_application_id IS NOT NULL;
