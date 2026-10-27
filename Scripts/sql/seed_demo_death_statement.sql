-- ============================================================================
-- 死亡情况说明 · 演示种子数据（幂等，可整删）
--
-- 目的：开发/演示库缺少"按村组织的死亡申报"数据，本脚本在指定村上造出可冒烟的数据集：
--   ① 同一死者 3 条申报（2 条有效 → 重复申报提醒；1 条已作废 → 作废展示）
--   ② 有效申报缺死亡证明编号 → 提醒「缺证明编号」
--   ③ 第二名死者：状态已核实 + 证明编号齐全
--   ④ 第三名死者：只有档案死亡登记、无申报 → 列表标「未申报」+ 缺确切时刻提醒
--   ⑤ 身份聚合/联动：复用库内**真实在保人员**（不伪造 7 张身份表），命中什么显示什么
--
-- 参数（psql 变量）：village_id（数值，ASCII，避免中文变量经控制台编码损坏）
--                    村名一律从 nc_regions_villages 按 id 取，再在 SQL 内做后缀归一化比对
-- 幂等标记：created_by = 'DEMO'（申报台账与死亡登记同标记），先删后插。
-- 执行：Scripts\seed_demo_death_statement.ps1（自动解析 village_id 并注入变量）
-- ============================================================================

BEGIN;

-- ① 幂等清理（仅演示行）
DELETE FROM nc_biz_death_declarations WHERE created_by = 'DEMO';
DELETE FROM nc_biz_death_records     WHERE created_by = 'DEMO';

-- ② 目标村 + 本村在保户主最多 3 名 → 临时表（演示人物 P/Q/R，事务结束自动删）
CREATE TEMP TABLE demo_persons ON COMMIT DROP AS
WITH v AS (
    SELECT id, town_id, village_name
    FROM nc_regions_villages
    WHERE id = :village_id
),
scoped AS (
    SELECT a.id AS application_id,
           a.applicant_name,
           a.applicant_id_card,
           ROW_NUMBER() OVER (ORDER BY a.id) AS rn
    FROM nc_biz_applications a
    WHERE a.deleted_at IS NULL
      AND a.applicant_id_card IS NOT NULL
      AND a.applicant_id_card <> ''
      AND (a.hukou_village_id = v.id
           OR a.village_id = v.id
           OR regexp_replace(COALESCE(a.community, ''),
                '(村民委员会|居民委员会|村公所|居委会|村委会|委员会|社区|村)+$', '')
              = regexp_replace(COALESCE(v.village_name, ''),
                '(村民委员会|居民委员会|村公所|居委会|村委会|委员会|社区|村)+$', ''))
)
SELECT s.rn,
       s.application_id,
       s.applicant_name,
       s.applicant_id_card,
       v.id          AS village_id,
       v.town_id,
       v.village_name
FROM scoped s
CROSS JOIN v
LIMIT 3;

-- ②-1 P(rn=1)：有效申报（村民委员会，缺死亡证明编号 → 提醒）
INSERT INTO nc_biz_death_declarations
    (deceased_name, deceased_id_card, deceased_relation,
     death_date, death_time, death_reason, death_certificate_no,
     declared_by_name, declared_by_id_card, declared_by_phone, declared_by_type,
     declared_at, declare_channel, status,
     application_id, village_id, town_id, village_name,
     operator_name, remark, created_by, created_at)
SELECT applicant_name, applicant_id_card, '本人',
       CURRENT_DATE - 2, TIME '14:30:00', '病故', NULL,
       '演示村民委员会', NULL, '0000-00000000', '村民委员会',
       CURRENT_TIMESTAMP - INTERVAL '2 days', 'PC', 'Pending',
       application_id, village_id, town_id, village_name,
       'DEMO', '演示数据：缺死亡证明编号 → 触发「缺证明编号」提醒', 'DEMO', NOW()
FROM demo_persons WHERE rn = 1;

-- ②-2 P：重复有效申报（家属，同死亡日期 → 触发「疑似重复申报」提醒）
INSERT INTO nc_biz_death_declarations
    (deceased_name, deceased_id_card, deceased_relation,
     death_date, death_time, death_reason, death_certificate_no,
     declared_by_name, declared_by_id_card, declared_by_phone, declared_by_type,
     declared_at, declare_channel, status,
     application_id, village_id, town_id, village_name,
     operator_name, remark, created_by, created_at)
SELECT applicant_name, applicant_id_card, '子女',
       CURRENT_DATE - 2, TIME '15:00:00', '病故', 'DEMO-CERT-' || application_id,
       '演示家属', NULL, '0000-00000001', '家属',
       CURRENT_TIMESTAMP - INTERVAL '1 day', 'PC', 'Pending',
       application_id, village_id, town_id, village_name,
       'DEMO', '演示数据：同死者重复有效申报 → 触发「疑似重复申报」提醒', 'DEMO', NOW()
FROM demo_persons WHERE rn = 1;

-- ②-3 P：已作废申报（列表默认隐藏，勾选「含作废申报」可见）
INSERT INTO nc_biz_death_declarations
    (deceased_name, deceased_id_card, deceased_relation,
     death_date, death_time, death_reason, death_certificate_no,
     declared_by_name, declared_by_id_card, declared_by_phone, declared_by_type,
     declared_at, declare_channel, status,
     application_id, village_id, town_id, village_name,
     operator_name, remark, created_by, created_at)
SELECT applicant_name, applicant_id_card, '子女',
       CURRENT_DATE - 40, TIME '09:00:00', '其他', 'DEMO-CERT-OLD',
       '演示家属', NULL, '0000-00000001', '家属',
       CURRENT_TIMESTAMP - INTERVAL '40 days', 'PC', 'Revoked',
       application_id, village_id, town_id, village_name,
       'DEMO', '[作废] 演示数据：日期填错后作废重报', 'DEMO', NOW()
FROM demo_persons WHERE rn = 1;

-- ②-4 Q(rn=2)：已核实、证明编号齐全（正常形态）
INSERT INTO nc_biz_death_declarations
    (deceased_name, deceased_id_card, deceased_relation,
     death_date, death_time, death_reason, death_certificate_no,
     declared_by_name, declared_by_id_card, declared_by_phone, declared_by_type,
     declared_at, declare_channel, status,
     application_id, village_id, town_id, village_name,
     operator_name, remark, created_by, created_at)
SELECT applicant_name, applicant_id_card, '本人',
       CURRENT_DATE - 5, TIME '08:20:00', '意外', 'DEMO-CERT-' || application_id,
       '演示村民委员会', NULL, '0000-00000000', '村民委员会',
       CURRENT_TIMESTAMP - INTERVAL '5 days', 'PC', 'Verified',
       application_id, village_id, town_id, village_name,
       'DEMO', '演示数据：正常已核实申报', 'DEMO', NOW()
FROM demo_persons WHERE rn = 2;

-- ②-5 R(rn=3)：只有档案死亡登记、没有申报 → 列表标「未申报」+ 提醒「未采集确切时刻」
INSERT INTO nc_biz_death_records
    (application_id, member_name, member_id_card, relationship_to_head,
     death_date, death_time, death_reason, operator_name, created_by, created_at)
SELECT application_id, applicant_name, applicant_id_card, '本人',
       CURRENT_DATE - 7, NULL, '病故', '演示补录', 'DEMO', NOW()
FROM demo_persons WHERE rn = 3;

COMMIT;

-- ③ 核对
\pset pager off
SELECT id, deceased_name, LEFT(deceased_id_card, 6) || '********' AS id_card_masked,
       death_date, death_time, declared_by_type, declared_by_name, status,
       COALESCE(death_certificate_no, '(空)') AS cert_no, remark
FROM nc_biz_death_declarations
WHERE created_by = 'DEMO'
ORDER BY deceased_id_card, declared_at;

SELECT id, member_name, death_date, death_time, created_by
FROM nc_biz_death_records
WHERE created_by = 'DEMO'
ORDER BY id;
