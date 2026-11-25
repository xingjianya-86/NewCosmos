-- 2026-11-19 户主成员行补录（"未找到死亡原户主成员记录"根因修复配套）
--
-- 背景：表单建档只把「共同生活成员」写入 nc_biz_family_members，户主信息权威在主表
--       nc_biz_applications.applicant_name/applicant_id_card。户主死亡/户主变更等流程按
--       "is_applicant / member_category='HouseholdHead' / 与申请人身份证或姓名一致" 定位户主行，
--       无户主行的档案会报"未找到死亡原户主成员记录"（2026-11-19 倪德奎 231025196005224918 / app 56）。
-- 做法：为"未删除、有申请人姓名与身份证、且无有效户主成员行"的档案补一条户主成员行：
--       is_applicant=TRUE、member_category='HouseholdHead'、relationship_to_head='本人/户主'，
--       其余人口学/户籍/健康字段取主表同源列；身份证合规时按身份证推导 birth_date/age。
--       代码侧同口径兜底见 ChangeService.DeathAndQueries.ResolveDeceasedHead（成员表无户主行时合成虚拟户主）。
-- 说明：幂等可重复执行（NOT EXISTS 去重）；无 DROP/TRUNCATE/UPDATE，仅 INSERT。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261119_family_member_head_backfill.sql

BEGIN;

INSERT INTO nc_biz_family_members (
    application_id, name, id_card, is_applicant,
    relationship_to_head, member_category,
    gender, birth_date, age, ethnicity, marital_status, phone,
    hukou_type, hukou_address,
    home_province, home_city, home_district, home_town, home_village, home_address,
    education_level, political_status, health_status, employment_status, work_unit, main_income_source,
    is_disabled, disability_certificate_no, disability_type, disability_level,
    disease_name, secondary_disease, is_severe_disease,
    created_at, updated_at
)
SELECT
    a.id,
    TRIM(a.applicant_name),
    TRIM(a.applicant_id_card),
    TRUE,
    '本人/户主',
    'HouseholdHead',
    a.gender,
    CASE WHEN a.applicant_id_card ~ '^[0-9]{17}[0-9Xx]$'
         THEN to_date(substr(a.applicant_id_card, 7, 8), 'YYYYMMDD') END,
    CASE WHEN a.applicant_id_card ~ '^[0-9]{17}[0-9Xx]$'
         THEN date_part('year', age(to_date(substr(a.applicant_id_card, 7, 8), 'YYYYMMDD')))::int END,
    a.ethnicity,
    a.marital_status,
    a.applicant_phone,
    a.hukou_type,
    a.hukou_address,
    a.province,
    a.city,
    a.district,
    a.town,
    a.community,
    a.address,
    a.education_level,
    a.political_status,
    a.health_status,
    a.employment_status,
    a.work_unit,
    a.income_source,
    COALESCE(a.disability_type, '') <> '',
    a.disability_card_no,
    a.disability_type,
    a.disability_level,
    a.disease_name,
    a.secondary_disease_name,
    COALESCE(a.is_severe_disease, FALSE),
    NOW(),
    NOW()
FROM nc_biz_applications a
WHERE a.deleted_at IS NULL
  AND COALESCE(TRIM(a.applicant_name), '') <> ''
  AND COALESCE(TRIM(a.applicant_id_card), '') <> ''
  AND NOT EXISTS (
        SELECT 1
          FROM nc_biz_family_members m
         WHERE m.application_id = a.id
           AND m.deleted_at IS NULL
           AND (COALESCE(m.is_applicant, FALSE)
                OR m.member_category = 'HouseholdHead'
                OR TRIM(COALESCE(m.id_card, '')) = TRIM(a.applicant_id_card)));

COMMIT;

-- 核对：补录后仍无户主成员行的档案数（应为 0）；同时打印本次新增户主行数
\pset pager off
SELECT COUNT(*) AS apps_without_head_member
  FROM nc_biz_applications a
 WHERE a.deleted_at IS NULL
   AND COALESCE(TRIM(a.applicant_name), '') <> ''
   AND COALESCE(TRIM(a.applicant_id_card), '') <> ''
   AND NOT EXISTS (
        SELECT 1
          FROM nc_biz_family_members m
         WHERE m.application_id = a.id
           AND m.deleted_at IS NULL
           AND (COALESCE(m.is_applicant, FALSE)
                OR m.member_category = 'HouseholdHead'
                OR TRIM(COALESCE(m.id_card, '')) = TRIM(a.applicant_id_card)));

SELECT COUNT(*) AS head_rows_total
  FROM nc_biz_family_members
 WHERE is_applicant = TRUE AND deleted_at IS NULL;
