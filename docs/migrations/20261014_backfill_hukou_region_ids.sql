-- ============================================================
-- 20261014 存量数据回填户籍区划 ID（户籍级联回显用）
-- 背景：历史保存只写 hukou_address 全文，hukou_city_id/hukou_county_id/
--       hukou_town_id/hukou_village_id 四列全为 NULL，
--       编辑打开时无法按 ID 回填户籍级联（如仲立亮 id=95）。
-- 口径：hukou_address = 市名+县名+镇名+村名 直接拼接（无分隔符）。
--       按 市→县→镇→村 四级链式前缀匹配（每级取最长匹配，县限定 city_name、
--       镇限定 county_id、村限定 town_id，靠父级链消歧重名村镇）。
--       四级全部匹配成功才回填；任一级不匹配保持 NULL，
--       由应用侧 TryResolveHukouCascadeFromTextAsync 打开时按文本兜底解析。
-- 幂等：仅更新匹配结果与现状不同的行，可重复执行。
-- 备份：步骤1 生成整表备份，回滚语句见文件尾。
-- 执行顺序：步骤1 备份 → 步骤2 预演人工过目 → 步骤3 更新 → 步骤4 报告。
-- ============================================================

-- ------------------------------------------------------------
-- 步骤1：备份受影响行（hukou_address 非空的全部记录）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS nc_biz_applications_hukou_bak_20261014 AS
SELECT id,
       hukou_address,
       hukou_city_id,
       hukou_county_id,
       hukou_town_id,
       hukou_village_id,
       updated_at
FROM nc_biz_applications
WHERE hukou_address IS NOT NULL AND hukou_address <> '';

-- ------------------------------------------------------------
-- 步骤2：预演（只读）——列出每行的地址与四级匹配结果，供人工过目
-- 四级全部匹配成功的行才会在步骤3被回填；NULL = 该行不回填。
-- ------------------------------------------------------------
SELECT a.id,
       a.hukou_address,
       c.id   AS city_id,     c.city_name,
       co.id  AS county_id,   co.county_name,
       t.id   AS town_id,     t.town_name,
       v.id   AS village_id,  v.village_name
FROM nc_biz_applications a
JOIN LATERAL (
    SELECT c2.id, c2.city_name
    FROM nc_regions_cities c2
    WHERE c2.is_active
      AND a.hukou_address LIKE c2.city_name || '%'
    ORDER BY length(c2.city_name) DESC
    LIMIT 1
) c ON TRUE
JOIN LATERAL (
    SELECT co2.id, co2.county_name
    FROM nc_regions_counties co2
    WHERE co2.is_active
      AND co2.city_name = c.city_name
      AND substr(a.hukou_address, length(c.city_name) + 1) LIKE co2.county_name || '%'
    ORDER BY length(co2.county_name) DESC
    LIMIT 1
) co ON TRUE
JOIN LATERAL (
    SELECT t2.id, t2.town_name
    FROM nc_regions_towns t2
    WHERE t2.is_active
      AND t2.county_id = co.id
      AND substr(a.hukou_address, length(c.city_name) + length(co.county_name) + 1) LIKE t2.town_name || '%'
    ORDER BY length(t2.town_name) DESC
    LIMIT 1
) t ON TRUE
JOIN LATERAL (
    SELECT v2.id, v2.village_name
    FROM nc_regions_villages v2
    WHERE v2.is_active
      AND v2.town_id = t.id
      AND substr(a.hukou_address,
                 length(c.city_name) + length(co.county_name) + length(t.town_name) + 1
      ) LIKE v2.village_name || '%'
    ORDER BY length(v2.village_name) DESC
    LIMIT 1
) v ON TRUE
WHERE a.hukou_address <> ''
ORDER BY a.id;

-- ------------------------------------------------------------
-- 步骤3：执行回填（四级全部匹配成功才写入；幂等，仅更新有变化的行）
-- ------------------------------------------------------------
UPDATE nc_biz_applications a
SET hukou_city_id     = m.city_id,
    hukou_county_id   = m.county_id,
    hukou_town_id     = m.town_id,
    hukou_village_id  = m.village_id
FROM (
    SELECT a2.id,
           c.id  AS city_id,
           co.id AS county_id,
           t.id  AS town_id,
           v.id  AS village_id
    FROM nc_biz_applications a2
    JOIN LATERAL (
        SELECT c2.id, c2.city_name
        FROM nc_regions_cities c2
        WHERE c2.is_active
          AND a2.hukou_address LIKE c2.city_name || '%'
        ORDER BY length(c2.city_name) DESC
        LIMIT 1
    ) c ON TRUE
    JOIN LATERAL (
        SELECT co2.id, co2.county_name
        FROM nc_regions_counties co2
        WHERE co2.is_active
          AND co2.city_name = c.city_name
          AND substr(a2.hukou_address, length(c.city_name) + 1) LIKE co2.county_name || '%'
        ORDER BY length(co2.county_name) DESC
        LIMIT 1
    ) co ON TRUE
    JOIN LATERAL (
        SELECT t2.id, t2.town_name
        FROM nc_regions_towns t2
        WHERE t2.is_active
          AND t2.county_id = co.id
          AND substr(a2.hukou_address, length(c.city_name) + length(co.county_name) + 1) LIKE t2.town_name || '%'
        ORDER BY length(t2.town_name) DESC
        LIMIT 1
    ) t ON TRUE
    JOIN LATERAL (
        SELECT v2.id
        FROM nc_regions_villages v2
        WHERE v2.is_active
          AND v2.town_id = t.id
          AND substr(a2.hukou_address,
                     length(c.city_name) + length(co.county_name) + length(t.town_name) + 1
          ) LIKE v2.village_name || '%'
        ORDER BY length(v2.village_name) DESC
        LIMIT 1
    ) v ON TRUE
    WHERE a2.hukou_address <> ''
) m
WHERE a.id = m.id
  AND (a.hukou_city_id    IS DISTINCT FROM m.city_id
    OR a.hukou_county_id  IS DISTINCT FROM m.county_id
    OR a.hukou_town_id    IS DISTINCT FROM m.town_id
    OR a.hukou_village_id IS DISTINCT FROM m.village_id);

-- ------------------------------------------------------------
-- 步骤4：报告——回填后的分布与未匹配（city_id 仍为 NULL）清单
-- ------------------------------------------------------------
SELECT count(*) AS total_with_text,
       count(*) FILTER (WHERE hukou_city_id IS NOT NULL)    AS city_filled,
       count(*) FILTER (WHERE hukou_county_id IS NOT NULL)  AS county_filled,
       count(*) FILTER (WHERE hukou_town_id IS NOT NULL)    AS town_filled,
       count(*) FILTER (WHERE hukou_village_id IS NOT NULL) AS village_filled
FROM nc_biz_applications
WHERE hukou_address IS NOT NULL AND hukou_address <> '';

SELECT id, hukou_address
FROM nc_biz_applications
WHERE hukou_address IS NOT NULL AND hukou_address <> ''
  AND hukou_city_id IS NULL
ORDER BY id;

-- ------------------------------------------------------------
-- 回滚（如需）：
-- UPDATE nc_biz_applications a
-- SET hukou_city_id    = b.hukou_city_id,
--     hukou_county_id  = b.hukou_county_id,
--     hukou_town_id    = b.hukou_town_id,
--     hukou_village_id = b.hukou_village_id
-- FROM nc_biz_applications_hukou_bak_20261014 b
-- WHERE a.id = b.id;
-- ------------------------------------------------------------
