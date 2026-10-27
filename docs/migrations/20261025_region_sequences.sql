-- =====================================================================
-- 2026-10-25  地区表主键改用序列取号（nc_regions_cities/counties/towns/villages）
--
-- 背景
--   RegionService 原来用 SELECT COALESCE(MAX(id), 基线)+1 给四张地区表分配主键：
--     1) 并发创建时两条事务会算出同一个 ID，后到的撞主键 23505；
--     2) 取号失败只能返回兜底假 ID（9001 / 90001 / 900001），
--        等于拿一个假 ID 去 INSERT —— 违反 AGENTS §7「失败不当空值/假值」。
--   改为 nextval（原子），失败如实上抛。
--
-- 实现方式（重要）
--   **只换"下一个 ID"的分配方式，所有 INSERT 仍显式传 id。**
--   种子与批量导入依赖既定 ID 区间（镇 10001-99999、村 100001-999999），
--   因此**不设列 DEFAULT**，也不改任何 INSERT 语句 —— 对既有数据零影响。
--   序列的创建与对齐由 RegionService 幂等执行：
--     · EnsureRegionSequencesAsync  —— 进程首次进地区页时建序列 + 对齐
--     · SyncRegionSequencesAsync    —— 种子初始化提交后 / TRUNCATE 之后重新对齐
--   本文件是**同一套 DDL 的手工副本**，用于核验，或在不便走应用侧时手动执行。
--
-- 执行顺序：① 查现状 → ② 执行 ③ 复验
-- =====================================================================

-- ---------------------------------------------------------------------
-- ① 只读核验
-- ---------------------------------------------------------------------
SELECT sequencename, last_value
FROM pg_sequences
WHERE schemaname = 'public' AND sequencename LIKE 'seq_nc_regions_%'
ORDER BY sequencename;

SELECT 'nc_regions_cities'    AS tbl, COALESCE(MAX(id), 0) AS max_id FROM nc_regions_cities
UNION ALL
SELECT 'nc_regions_counties',          COALESCE(MAX(id), 0)          FROM nc_regions_counties
UNION ALL
SELECT 'nc_regions_towns',             COALESCE(MAX(id), 0)          FROM nc_regions_towns
UNION ALL
SELECT 'nc_regions_villages',          COALESCE(MAX(id), 0)          FROM nc_regions_villages;

-- ---------------------------------------------------------------------
-- ② 建序列（IF NOT EXISTS，可重复执行）
--    ⚠️ 语句必须在建表之后执行：先确认四张表都存在。
-- ---------------------------------------------------------------------
CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_cities;
CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_counties;
CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_towns;
CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_villages;

-- ---------------------------------------------------------------------
-- ③ 对齐：setval(GREATEST(表内最大 ID, 序列已发到的值, 基线), true)
--    GREATEST 保证**只前进不回退** —— 不会把已发号的位置让回去导致主键冲突。
--    基线沿用历史 MAX(id)+1 的兜底值；地级市无基线（从 0 起，下一个取 1）。
--    结果含义：下一次 nextval = 上面括号里的值 + 1。
-- ---------------------------------------------------------------------
SELECT setval('seq_nc_regions_cities', GREATEST(
           COALESCE((SELECT MAX(id) FROM nc_regions_cities), 0),
           COALESCE((SELECT last_value FROM pg_sequences
                     WHERE schemaname = 'public' AND sequencename = 'seq_nc_regions_cities'), 0),
           0));

SELECT setval('seq_nc_regions_counties', GREATEST(
           COALESCE((SELECT MAX(id) FROM nc_regions_counties), 0),
           COALESCE((SELECT last_value FROM pg_sequences
                     WHERE schemaname = 'public' AND sequencename = 'seq_nc_regions_counties'), 0),
           9000));

SELECT setval('seq_nc_regions_towns', GREATEST(
           COALESCE((SELECT MAX(id) FROM nc_regions_towns), 0),
           COALESCE((SELECT last_value FROM pg_sequences
                     WHERE schemaname = 'public' AND sequencename = 'seq_nc_regions_towns'), 0),
           90000));

SELECT setval('seq_nc_regions_villages', GREATEST(
           COALESCE((SELECT MAX(id) FROM nc_regions_villages), 0),
           COALESCE((SELECT last_value FROM pg_sequences
                     WHERE schemaname = 'public' AND sequencename = 'seq_nc_regions_villages'), 0),
           900000));

-- ---------------------------------------------------------------------
-- ④ 复验：序列存在，且 nextval 落在 max(id) 之后（测完即回滚，不留副作用）
-- ---------------------------------------------------------------------
SELECT sequencename, last_value FROM pg_sequences
WHERE schemaname = 'public' AND sequencename LIKE 'seq_nc_regions_%'
ORDER BY sequencename;

BEGIN;
SELECT nextval('seq_nc_regions_counties');   -- 应 > max(id)
SELECT nextval('seq_nc_regions_towns');
SELECT nextval('seq_nc_regions_villages');
ROLLBACK;

-- ---------------------------------------------------------------------
-- 何时需要重新执行 ③
--   · 跑完 RegionService.InitializeFromSeedAsync（显式 ID 覆盖了序列进度）
--   · 跑完 RegionService.ClearTablesAsync（TRUNCATE 后 max(id) 归零）
--   应用侧已自动调用 SyncRegionSequencesAsync，手工执行仅用于排障。
-- ---------------------------------------------------------------------
