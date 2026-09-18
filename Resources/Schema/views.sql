-- 视图定义脚本
-- 用于管理数据库视图，不纳入 YAML 重建流程
-- 如果视图丢失，可以执行此脚本重建

-- ── 字典分类视图 ──
-- 合并种子数据和更新数据，提供统一的字典分类查询接口
CREATE OR REPLACE VIEW nc_view_dict_categories AS
SELECT 
    s.id,
    COALESCE(u.category, s.category) AS category,
    COALESCE(u.display_name, s.display_name) AS display_name,
    COALESCE(u.description, s.description) AS description,
    COALESCE(u.sort_order, s.sort_order) AS sort_order,
    CASE 
        WHEN u.id IS NOT NULL THEN u.is_active 
        ELSE s.is_active 
    END AS is_active,
    CASE 
        WHEN u.id IS NOT NULL THEN 'updated' 
        ELSE 'seed' 
    END AS status,
    u.source,
    u.action,
    u.created_at AS updated_at,
    u.created_by AS updated_by
FROM nc_dict_categories s
LEFT JOIN nc_dict_categories_updates u ON s.id = u.id
WHERE COALESCE(u.action, 'none') <> 'delete'

UNION ALL

SELECT 
    u.id,
    u.category,
    u.display_name,
    u.description,
    u.sort_order,
    u.is_active,
    'updated' AS status,
    u.source,
    u.action,
    u.created_at AS updated_at,
    u.created_by AS updated_by
FROM nc_dict_categories_updates u
WHERE u.action = 'add' 
  AND NOT EXISTS (SELECT 1 FROM nc_dict_categories s WHERE s.id = u.id)

ORDER BY 2, 5, 1;

-- ── 字典项视图 ──
-- 合并种子数据和更新数据，提供统一的字典项查询接口
CREATE OR REPLACE VIEW nc_view_dict_items AS
SELECT 
    s.id,
    COALESCE(u.category, s.category) AS category,
    COALESCE(u.item_key, s.item_key) AS item_key,
    COALESCE(u.item_value, s.item_value) AS item_value,
    COALESCE(u.sort_order, s.sort_order) AS sort_order,
    CASE 
        WHEN u.id IS NOT NULL THEN u.is_active 
        ELSE s.is_active 
    END AS is_active,
    COALESCE(u.description, s.description) AS description,
    CASE 
        WHEN u.id IS NOT NULL THEN 'updated' 
        ELSE 'seed' 
    END AS status,
    u.source,
    u.action,
    u.created_at AS updated_at,
    u.created_by AS updated_by
FROM nc_dict_items s
LEFT JOIN nc_dict_items_updates u ON s.id = u.id
WHERE COALESCE(u.action, 'none') <> 'delete'

UNION ALL

SELECT 
    u.id,
    u.category,
    u.item_key,
    u.item_value,
    u.sort_order,
    u.is_active,
    u.description,
    'updated' AS status,
    u.source,
    u.action,
    u.created_at AS updated_at,
    u.created_by AS updated_by
FROM nc_dict_items_updates u
WHERE u.action = 'add' 
  AND NOT EXISTS (SELECT 1 FROM nc_dict_items s WHERE s.id = u.id)

ORDER BY 2, 5, 1;
