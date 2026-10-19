-- 2026-10-19 追缴记录「应追缴总额」订正（存量数据）
--
-- 背景：nc_biz_recovery_records.recovery_amount 由 `月保障额(monthly_amount) × 追缴月数(recovery_months)` 派生。
--       张彦亮（id=3）的来源档案（nc_biz_applications.id=99 / 98，来源表 nc_biz_rural_subsistence_families.id=2521）
--       月保障额均为 0 且来源行已不存在，导致追缴总额被算成 0.00，与业务事实不符。
-- 口径：本记录应追缴总额业务核实为 16301 元（16301 为质数，无法拆成「整数月标准 × 33 个月」，
--       故直接落库存储值，不再由月保障额反算）。
-- 影响：本库仅命中 1 行（id=3 张彦亮）；对其它记录为 no-op。
-- 说明：本脚本只做「先备份再订正」，不做任何 DROP/TRUNCATE；可重复执行（幂等）。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261019_recovery_amount_fix.sql

BEGIN;

-- ① 备份将被修改的行（幂等：已存在则保留首次备份）
CREATE TABLE IF NOT EXISTS nc_biz_recovery_records_bak_20261019_amount (
    id               bigint      PRIMARY KEY,
    person_name      varchar,
    monthly_amount   numeric,
    recovery_months  integer,
    recovery_amount  numeric,
    updated_at       timestamp,
    backed_up_at     timestamp   NOT NULL DEFAULT NOW()
);

INSERT INTO nc_biz_recovery_records_bak_20261019_amount
    (id, person_name, monthly_amount, recovery_months, recovery_amount, updated_at)
SELECT r.id, r.person_name, r.monthly_amount, r.recovery_months, r.recovery_amount, r.updated_at
FROM nc_biz_recovery_records r
WHERE r.id = 3
  AND r.person_name = '张彦亮'
ON CONFLICT (id) DO NOTHING;

-- ② 订正应追缴总额 = 16301（月保障额、月数保持原样，来源档案无月标准可依据）
UPDATE nc_biz_recovery_records
SET recovery_amount = 16301,
    updated_at      = NOW()
WHERE id = 3
  AND person_name = '张彦亮'
  AND recovery_amount IS DISTINCT FROM 16301;

COMMIT;

-- ③ 核对：应恰好 1 行，且 recovery_amount = 16301.00
\pset pager off
SELECT id, person_name, monthly_amount, recovery_months, recovery_amount, recovered_amount, status,
       start_month, end_month, updated_at
FROM nc_biz_recovery_records
WHERE id = 3;
