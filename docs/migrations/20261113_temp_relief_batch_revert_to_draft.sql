-- ============================================================================
-- 20261113 临时救助 C 线本月批次 14 条：已确认 → 草稿（一次性数据修正）
--
-- 背景：因特殊情况，将 C 线本月批次窗口 (2026-10-13, 2026-11-13] 内的
--       14 条已确认记录全部转回草稿，供重新编辑后确认。
-- 口径：与补打中心「本月C线」/ 月报周期同源（业务时间
--       COALESCE(apply_date, report_time, created_at)）。
-- 处理：status='Draft'，并清空 confirmed_at / confirmed_by（用户裁定）；
--       stopped_*、明细表、打印记录一律不动。
-- 前置备份：backup\20261113_160538_临时救助转草稿\temp_relief_before.csv
-- 断言：影响/落库行数必须为 14，否则整体回滚。
-- 执行：psql -f docs\migrations\20261113_temp_relief_batch_revert_to_draft.sql
-- 登记：powershell -File deploy\check_migrations.ps1 -Mark 20261113_temp_relief_batch_revert_to_draft.sql
-- ============================================================================

BEGIN;

UPDATE nc_biz_temp_relief_applications
   SET status = 'Draft',
       confirmed_at = NULL,
       confirmed_by = '',
       updated_at = NOW(),
       updated_by = '批量转草稿'
 WHERE id = ANY(ARRAY[21,22,23,24,26,27,28,29,30,31,32,33,34,35])
   AND status = 'Confirmed'
   AND deleted_at IS NULL;

-- 断言：14 条已全部落为草稿且确认痕迹已清空；任何一条不符即回滚
DO $$
DECLARE
    affected integer;
BEGIN
    SELECT COUNT(*) INTO affected
      FROM nc_biz_temp_relief_applications
     WHERE id = ANY(ARRAY[21,22,23,24,26,27,28,29,30,31,32,33,34,35])
       AND status = 'Draft'
       AND confirmed_at IS NULL
       AND COALESCE(confirmed_by, '') = ''
       AND deleted_at IS NULL;

    IF affected <> 14 THEN
        RAISE EXCEPTION '预期 14 条转草稿，实际 % 条，已回滚', affected;
    END IF;
END $$;

COMMIT;
