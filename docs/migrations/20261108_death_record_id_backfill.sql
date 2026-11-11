-- 2026-11-08 死亡登记外键回填（P2-1：解除「一期不回填」约定）
--
-- 背景：nc_biz_death_declarations.death_record_id 语义关联 nc_biz_death_records，
--       但一直未回填（FK 永远 NULL），双源只能靠 deceased_id_card 文本关联，
--       检索/提醒/联动每次都要双表合并。
-- 做法：按身份证把 death_record_id 补挂为**最新**一条死亡登记；
--       仅首次回填（death_record_id IS NULL），已挂不覆盖；作废申报（Revoked）不回填。
-- 写入侧：ChangeService.BackfillDeathRecordLinksAsync 已挂到三处死亡登记写入点
--       （成员死亡批量 CTE / 户主死亡停旧建新 / 单人户整户停止），本文件只负责存量一次性回填。
-- 说明：幂等可重复执行；无 DROP/TRUNCATE。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261108_death_record_id_backfill.sql

BEGIN;

UPDATE nc_biz_death_declarations d
SET death_record_id = (
    SELECT r.id FROM nc_biz_death_records r
    WHERE r.member_id_card = d.deceased_id_card
    ORDER BY r.id DESC LIMIT 1)
WHERE d.death_record_id IS NULL
  AND d.deleted_at IS NULL
  AND d.status <> 'Revoked'
  AND EXISTS (SELECT 1 FROM nc_biz_death_records r2 WHERE r2.member_id_card = d.deceased_id_card);

COMMIT;

-- 核对：回填条数（death_record_id 非空）与仍为 NULL 的有效申报数
\pset pager off
SELECT COUNT(*) FILTER (WHERE death_record_id IS NOT NULL) AS linked,
       COUNT(*) FILTER (WHERE death_record_id IS NULL)     AS unlinked
FROM nc_biz_death_declarations
WHERE deleted_at IS NULL AND status <> 'Revoked';
