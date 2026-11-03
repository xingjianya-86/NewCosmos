-- 2026-11-01 追缴编号落库（补打不变号 / 并发不撞号）
-- 背景：旧实现每次打印时 COUNT(*)+1 现算编号（RecoveryService.GenerateRecoveryCodeAsync）：
--   ① 跨月补打编号变号（传入 DateTime.Now 而非记录创建月）；
--   ② COUNT 无锁，并发打印可生成同号；
--   ③ TO_CHAR(created_at,'YYYYMM') 函数包列，索引失效。
-- 方案：编号随记录保存一次生成、存入 recovery_code 列；打印读库内编号；
--       存量记录（列为空）首次打印时补分配并回写（应用内 GetOrAssignRecoveryCodeAsync）。
-- 执行方式：psql -d <dbname> -f 20261101_recovery_code.sql（幂等，可重复执行）

BEGIN;

ALTER TABLE nc_biz_recovery_records ADD COLUMN IF NOT EXISTS recovery_code varchar(20);

COMMENT ON COLUMN nc_biz_recovery_records.recovery_code IS
  '追缴编号（YYYYMM-NNN，随保存一次生成；打印/补打同号；存量记录首次打印时补分配）';

-- 编号唯一（并发生成由应用层 pg_advisory_xact_lock 串行化，此索引兜底）
CREATE UNIQUE INDEX IF NOT EXISTS ux_nc_biz_recovery_records_recovery_code
  ON nc_biz_recovery_records (recovery_code)
  WHERE recovery_code IS NOT NULL;

COMMIT;
