-- 档案照片命名回填（2026-11-04）
-- 规则（与 C# PhotoNameBuilder.Build 完全一致）：姓名_身份证后4位_类别_编号
--   · 编号 = 该人 + 该类别内流水号（已有可解析编号的照片往后接，不覆盖、不复用）
--   · 类别显示名中的 '/' 替换为全角 '／'（病历/诊断证明、银行卡/一卡通 等）
--   · 姓名取三域业务表（高龄/低收入/临时救助）按身份证聚合 MAX；查不到姓名则省略姓名段
--   · 只回填 photo_name 为空的行；已有备注（如"户口本第2页"）不覆盖
-- 幂等：回填后无空行，重复执行为 no-op。
-- 执行：psql -w -v ON_ERROR_STOP=1 -f docs/migrations/20261104_archive_photo_name_backfill.sql

WITH name_src AS (
    SELECT id_card, MAX(name) AS name
    FROM (
        SELECT e.id_card, e.name
        FROM nc_biz_elderly_applications e
        WHERE e.deleted_at IS NULL
        UNION ALL
        SELECT a.applicant_id_card, a.applicant_name
        FROM nc_biz_applications a
        WHERE a.deleted_at IS NULL
        UNION ALL
        SELECT t.applicant_id_card, t.applicant_name
        FROM nc_biz_temp_relief_applications t
        WHERE t.deleted_at IS NULL
    ) u
    WHERE name IS NOT NULL AND BTRIM(name) <> ''
    GROUP BY id_card
),
target AS (
    -- 待回填行：按人+类别按采集时间排序取流水（只数空名行）
    SELECT p.id,
           p.applicant_id_card,
           p.photo_category,
           ROW_NUMBER() OVER (
               PARTITION BY p.applicant_id_card, p.photo_category
               ORDER BY p.created_at, p.id
           ) AS rn
    FROM nc_biz_archive_photos p
    WHERE p.photo_name IS NULL OR BTRIM(p.photo_name) = ''
),
base AS (
    -- 已有可解析编号的最大值：新回填编号从其后接续（不与既有编号撞号）
    SELECT applicant_id_card,
           photo_category,
           COALESCE(MAX((REGEXP_REPLACE(photo_name, '^.*_(\d+)$', '\1'))::bigint), 0) AS max_seq
    FROM nc_biz_archive_photos
    WHERE photo_name IS NOT NULL
      AND BTRIM(photo_name) <> ''
      AND photo_name ~ '_\d+$'
    GROUP BY applicant_id_card, photo_category
),
cat_map(code, cn) AS (
    VALUES
        ('ID_CARD', '身份证'),
        ('HOUSEHOLD_REGISTER', '户口本'),
        ('PERSON_PHOTO', '本人照片／证件照'),
        ('MEDICAL_RECORD', '病历／诊断证明'),
        ('DISABILITY_CERT', '残疾证'),
        ('BANK_CARD', '银行卡／一卡通'),
        ('INCOME_PROOF', '收入／务工证明'),
        ('PUBLICITY_CHECK', '公示／核查照片'),
        ('OTHER_CERT', '其他证件照')
)
UPDATE nc_biz_archive_photos p
SET photo_name = CASE
        WHEN COALESCE(ns.name, '') = '' THEN
            RIGHT(p.applicant_id_card, 4) || '_' || cm.cn || '_' || LPAD((COALESCE(b.max_seq, 0) + t.rn)::text, 2, '0')
        ELSE
            BTRIM(ns.name) || '_' || RIGHT(p.applicant_id_card, 4) || '_' || cm.cn || '_' ||
            LPAD((COALESCE(b.max_seq, 0) + t.rn)::text, 2, '0')
    END
FROM target t
JOIN cat_map cm ON cm.code = t.photo_category
LEFT JOIN name_src ns ON ns.id_card = t.applicant_id_card
LEFT JOIN base b ON b.applicant_id_card = t.applicant_id_card
                 AND b.photo_category = t.photo_category
WHERE p.id = t.id;
