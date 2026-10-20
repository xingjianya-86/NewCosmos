-- 2026-10-20 追缴金额来源改造：上线后只读核验
--
-- 用途：迁移（20261019_recovery_amount_fix.sql / 20261019_recovery_amount_source.sql）执行后，
--       确认结构、存量、一致性、回滚依据全部就位。全为 SELECT，可反复执行。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -v ON_ERROR_STOP=1 -f docs/migrations/20261020_recovery_online_verify.sql
-- 判定：① 三列齐全且 amount_source 默认 'Flat'；② 分段表 10 列 + 外键 CASCADE + recovery_id 索引；
--       ③ Manual 必有原因；④ 无非法来源值、无分段孤儿行；⑤ 备份表行数 ≥ 现网行数（可回滚）。

\pset pager off

\echo === 1. 主表新列（应为 3 行，amount_source 非空且默认 'Flat'）===
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'nc_biz_recovery_records'
  AND column_name IN ('amount_source', 'amount_note', 'auto_amount')
ORDER BY column_name;

\echo === 2. 分段表结构（应为 10 列）===
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'nc_biz_recovery_segments'
ORDER BY ordinal_position;

\echo === 3. 分段表约束与索引（外键须为 ON DELETE CASCADE）===
SELECT conname, pg_get_constraintdef(oid) AS def
FROM pg_constraint
WHERE conrelid = 'nc_biz_recovery_segments'::regclass
ORDER BY conname;

SELECT indexname
FROM pg_indexes
WHERE tablename = 'nc_biz_recovery_segments'
ORDER BY indexname;

\echo === 4. 追缴记录数据快照 ===
SELECT id, person_name, monthly_amount, recovery_months, recovery_amount,
       amount_source, auto_amount,
       COALESCE(left(amount_note, 48), '(null)') AS note,
       updated_at
FROM nc_biz_recovery_records
ORDER BY id;

\echo === 5. 一致性检查（以下各项均应为 0）===
SELECT count(*) AS manual_missing_note
FROM nc_biz_recovery_records
WHERE amount_source = 'Manual' AND COALESCE(btrim(amount_note), '') = '';

SELECT count(*) AS invalid_source
FROM nc_biz_recovery_records
WHERE amount_source NOT IN ('Segment', 'Flat', 'Manual');

SELECT count(*) AS orphan_segments
FROM nc_biz_recovery_segments s
LEFT JOIN nc_biz_recovery_records r ON r.id = s.recovery_id
WHERE r.id IS NULL;

\echo === 6. 回滚依据（备份行数应 ≥ 现网行数）===
SELECT (SELECT count(*) FROM nc_biz_recovery_records)                               AS live_rows,
       (SELECT count(*) FROM nc_biz_recovery_records_bak_20261019_source)           AS bak_source_rows,
       (SELECT count(*) FROM nc_biz_recovery_records_bak_20261019_amount)           AS bak_amount_rows;

\echo === 7. 汇总判定（三列全 true 为通过）===
SELECT
    (SELECT count(*) = 3 FROM information_schema.columns
      WHERE table_name = 'nc_biz_recovery_records'
        AND column_name IN ('amount_source', 'amount_note', 'auto_amount'))            AS columns_ok,
    (SELECT count(*) = 0 FROM nc_biz_recovery_records
      WHERE amount_source = 'Manual' AND COALESCE(btrim(amount_note), '') = '')        AS manual_note_ok,
    (SELECT to_regclass('public.nc_biz_recovery_segments') IS NOT NULL)                AS segments_table_ok;
