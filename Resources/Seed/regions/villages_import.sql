-- ============================================================
-- 黑龙江省村/社区数据导入脚本
-- 数据量：约10000条（需从数据源提取）
-- 用途：通过SQL脚本导入村级数据（不适合YAML种子）
-- 数据来源：https://github.com/modood/Administrative-divisions-of-China
-- ============================================================

-- ============================================================
-- 步骤1：创建临时导入表
-- ============================================================
CREATE TEMP TABLE tmp_villages_import (
    village_code VARCHAR(15) PRIMARY KEY,
    village_name VARCHAR(100) NOT NULL,
    town_code VARCHAR(12) NOT NULL,
    village_type VARCHAR(20)
);

-- ============================================================
-- 步骤2：使用COPY导入原始数据
-- 从GitHub下载villages.csv后，过滤黑龙江省数据：
--   grep "^23" villages.csv > hlj_villages.csv
-- CSV格式：code,name,parent_code,province_code,city_code
-- ============================================================
-- COPY tmp_villages_import (village_code, village_name, town_code, village_type)
-- FROM '/path/to/hlj_villages.csv'
-- WITH (FORMAT csv, HEADER false, ENCODING 'UTF8');

-- ============================================================
-- 步骤3：从临时表转换插入到正式表
-- ============================================================
INSERT INTO nc_regions_villages (id, town_id, village_name, village_code, village_type, sort_order, is_active)
SELECT
    ROW_NUMBER() OVER (ORDER BY t.id, tmp.village_code) + 100000 AS id,
    t.id AS town_id,
    tmp.village_name,
    tmp.village_code,
    CASE
        WHEN tmp.village_name LIKE '%居委会%' THEN '居委会'
        WHEN tmp.village_name LIKE '%村委会%' THEN '村委会'
        WHEN tmp.village_name LIKE '%社区%' THEN '居委会'
        ELSE '村委会'
    END AS village_type,
    ROW_NUMBER() OVER (PARTITION BY t.id ORDER BY tmp.village_code) AS sort_order,
    TRUE AS is_active
FROM tmp_villages_import tmp
INNER JOIN nc_regions_towns t ON tmp.town_code = t.town_code
WHERE t.is_active = TRUE;

-- 清理临时表
DROP TABLE tmp_villages_import;

-- ============================================================
-- 示例数据（哈尔滨市道里区部分村/社区，可直接执行）
-- ============================================================
INSERT INTO nc_regions_villages (id, town_id, village_name, village_code, village_type, sort_order, is_active) VALUES
-- 道里区兆麟街道 (town_id: 10001)
(100001, 10001, '兆麟社区', '230102001001', '居委会', 1, true),
(100002, 10001, '柳树社区', '230102001002', '居委会', 2, true),
(100003, 10001, '索菲亚社区', '230102001003', '居委会', 3, true),

-- 道里区新阳路街道 (town_id: 10002)
(100004, 10002, '新阳社区', '230102002001', '居委会', 1, true),
(100005, 10002, '安发社区', '230102002002', '居委会', 2, true),
(100006, 10002, '银达社区', '230102002003', '居委会', 3, true),

-- 道里区抚顺街道 (town_id: 10003)
(100007, 10003, '抚顺社区', '230102003001', '居委会', 1, true),
(100008, 10003, '安和社区', '230102003002', '居委会', 2, true),
(100009, 10003, '地德里社区', '230102003003', '居委会', 3, true),

-- 道里区安化街道 (town_id: 10004)
(100010, 10004, '安化社区', '230102004001', '居委会', 1, true),
(100011, 10004, '正阳社区', '230102004002', '居委会', 2, true),

-- 道里区共乐街道 (town_id: 10005)
(100012, 10005, '共乐社区', '230102005001', '居委会', 1, true),
(100013, 10005, '光华社区', '230102005002', '居委会', 2, true),
(100014, 10005, '民安社区', '230102005003', '居委会', 3, true),

-- 南岗区松花江街道 (town_id: 10020)
(100015, 10020, '松花江社区', '230103001001', '居委会', 1, true),
(100016, 10020, '建筑社区', '230103001002', '居委会', 2, true),

-- 南岗区花园街道 (town_id: 10021)
(100017, 10021, '花园社区', '230103002001', '居委会', 1, true),
(100018, 10021, '复华社区', '230103002002', '居委会', 2, true),
(100019, 10021, '海城社区', '230103002003', '居委会', 3, true);

-- ============================================================
-- 批量导入说明：
-- 1. 从数据源下载 villages.csv
-- 2. 过滤黑龙江省数据（code以23开头）
-- 3. 使用 COPY 或 INSERT 批量导入
-- ============================================================

-- 批量导入命令示例（PostgreSQL）：
-- COPY nc_regions_villages (id, town_id, village_name, village_code, village_type, sort_order, is_active)
-- FROM '/path/to/heilongjiang_villages.csv'
-- WITH (FORMAT csv, HEADER true, ENCODING 'UTF8');

-- ============================================================
-- 数据完整性检查
-- ============================================================

-- 检查孤立记录（town_id不存在）
SELECT v.id, v.village_name, v.town_id
FROM nc_regions_villages v
LEFT JOIN nc_regions_towns t ON v.town_id = t.id
WHERE t.id IS NULL;

-- 统计各村数量
SELECT t.town_name, COUNT(v.id) as village_count
FROM nc_regions_villages v
JOIN nc_regions_towns t ON v.town_id = t.id
GROUP BY t.town_name
ORDER BY village_count DESC;
