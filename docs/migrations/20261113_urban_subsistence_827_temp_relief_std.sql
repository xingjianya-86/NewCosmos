-- ============================================================================
-- 20261113 城镇低保标准修正为 827 + 停用临时救助小额定额三档
--
-- 背景（用户裁定，2026-11-13）：
--   ① 城镇低保月标准 = 827 元（原 780 / 832 两行均错误，两行都改）；
--      农村行不动（当前生效 632、2025 年 585）。
--   ② 临时救助小额"定额救助"改为按户籍动态取低保月标准
--      （农村 632 / 城镇 827），原三档 500/1000/2000 废弃停用（留行可回溯）。
--
-- 影响：Rural/UrbanSubsistenceStandard 为低保分类判定、收入对比、文书显示、
--       定额救助的读取源；832→827 仅影响新判定（已存判定不重算）。
-- 前置备份：backup\20261113_163452_定额低保标准\nc_config_standards_before.csv
-- 执行：psql -f docs\migrations\20261113_urban_subsistence_827_temp_relief_std.sql
-- 注意：执行后重启应用（StandardConfigService 有 5 分钟进程内缓存）。
-- 登记：powershell -File deploy\check_migrations.ps1 -Mark 20261113_urban_subsistence_827_temp_relief_std.sql
-- ============================================================================

BEGIN;

-- ① 城镇低保标准两行（2025 年 780、2026-04 起 832）→ 827
UPDATE nc_config_standards
   SET standard_value = 827,
       updated_at = CURRENT_TIMESTAMP
 WHERE standard_type = 'UrbanSubsistenceStandard';

-- ② 停用临时救助小额定额三档（一般困难/较重困难/特别困难）
UPDATE nc_config_standards
   SET is_active = FALSE,
       updated_at = CURRENT_TIMESTAMP
 WHERE standard_type = 'TEMP_RELIEF_SMALL';

-- 断言：城镇两行均为 827 且仍启用；定额三行均已停用；否则整体回滚
DO $$
DECLARE
    urban_ok integer;
    small_off integer;
BEGIN
    SELECT COUNT(*) INTO urban_ok
      FROM nc_config_standards
     WHERE standard_type = 'UrbanSubsistenceStandard'
       AND standard_value = 827
       AND is_active = TRUE;

    SELECT COUNT(*) INTO small_off
      FROM nc_config_standards
     WHERE standard_type = 'TEMP_RELIEF_SMALL'
       AND is_active = FALSE;

    IF urban_ok <> 2 THEN
        RAISE EXCEPTION '预期城镇低保标准 2 行=827，实际 % 行，已回滚', urban_ok;
    END IF;
    IF small_off <> 3 THEN
        RAISE EXCEPTION '预期临时救助小额定额 3 行停用，实际 % 行，已回滚', small_off;
    END IF;
END $$;

COMMIT;
