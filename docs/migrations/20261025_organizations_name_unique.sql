-- =====================================================================
-- 2026-10-25  经办机构管理：组织名称唯一索引
--
-- 背景
--   OrganizationService.CreateAsync 先用 CheckNameExistsAsync 查重、再 INSERT，
--   两者之间存在并发窗口：查重通过 ≠ 一定能插进去，历史上可产生重名机构。
--   本迁移补一个唯一索引兜底（应用层同时把 DUPLICATE_ID_CARD 误用改为
--   ORGANIZATION_NAME_DUPLICATE，并把 23505 映射为该错误码）。
--
-- Schema 侧（Resources/Schema/new_permission_system/database.yaml）已声明
--   ux_nc_sys_organizations_name，SchemaService 启动时会尝试
--   CREATE UNIQUE INDEX IF NOT EXISTS；若存量有重名，建索引失败只记 Warn、
--   不中断启动 —— 因此必须先手工执行本文件完成清理，索引才会真正生效。
--
-- 执行顺序：① 只读核验 → ② 人工决定合并/改名 → ③ 建索引 → ④ 复验
-- =====================================================================

-- ---------------------------------------------------------------------
-- ① 只读核验：列出所有重名（预期返回 0 行才可继续）
-- ---------------------------------------------------------------------
SELECT name,
       COUNT(*)        AS cnt,
       MIN(id)         AS keep_id,          -- 建议保留的主键（最早创建的那条）
       ARRAY_AGG(id ORDER BY id) AS all_ids,
       MAX(updated_at) AS last_updated
FROM nc_sys_organizations
GROUP BY name
HAVING COUNT(*) > 1
ORDER BY cnt DESC, name;

-- ---------------------------------------------------------------------
-- ② 人工处理（不要盲目执行，先看 ① 的结果）
--
--   方案 A —— 保留最早一条，其余改名加后缀（可逆，推荐先跑这个看效果）：
--     UPDATE nc_sys_organizations o
--     SET name = o.name || ' (' || o.id || ')',
--         updated_at = NOW()
--     FROM (
--         SELECT id, name, MIN(id) OVER (PARTITION BY name) AS keep_id
--         FROM nc_sys_organizations
--     ) d
--     WHERE o.id = d.id
--       AND o.id <> d.keep_id;
--
--   方案 B —— 确认是完全重复的机构（无用户/无子组织）才可删除：
--     先确认，再删，删前按 §2 惯例备份：
--       SELECT o.id, o.name,
--              (SELECT COUNT(*) FROM nc_sys_users u WHERE u.organization_id = o.id) AS users,
--              (SELECT COUNT(*) FROM nc_sys_organizations c WHERE c.parent_id = o.id) AS children
--       FROM nc_sys_organizations o
--       WHERE o.name IN (SELECT name FROM nc_sys_organizations GROUP BY name HAVING COUNT(*) > 1)
--       ORDER BY o.name, o.id;
--     users = 0 且 children = 0 的重复行才允许删除。
--
--   ⚠️ 若重名机构下挂着用户（nc_sys_users.organization_id），
--      直接删会破坏用户归属 —— 必须先迁移用户再删。
-- ---------------------------------------------------------------------

-- ---------------------------------------------------------------------
-- ③ 建唯一索引（IF NOT EXISTS，可重复执行）
-- ---------------------------------------------------------------------
CREATE UNIQUE INDEX IF NOT EXISTS ux_nc_sys_organizations_name
    ON nc_sys_organizations (name);

-- ---------------------------------------------------------------------
-- ④ 复验：索引存在 + 仍无重名
-- ---------------------------------------------------------------------
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'nc_sys_organizations'
  AND indexname = 'ux_nc_sys_organizations_name';

SELECT name, COUNT(*) AS cnt
FROM nc_sys_organizations
GROUP BY name
HAVING COUNT(*) > 1;
