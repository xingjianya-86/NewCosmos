-- 2026-10-19 追缴记录「金额来源」与分段明细（结构 + 存量回填）
--
-- 背景：nc_biz_recovery_records.recovery_amount 原为「月保障额(monthly_amount) × 追缴月数(recovery_months)」派生：
--       来源档案月保障额为 0（来源台账行已不存在、变更记录不全）时总额被算成 0.00，与业务事实不符，
--       且界面只读、无兜底（实例：张彦亮 id=3，业务核实应追缴 16301 元）。
-- 口径（定稿，代码侧唯一实现见 Services/Domain/Recovery/RecoveryAmountCalculator.cs）：
--   · recovery_amount 为唯一事实来源：表单/列表/打印/落库同源，不再由月保障额无条件反算；
--   · amount_source ∈ Segment（变更记录分段累加）/ Flat（月标准×月数）/ Manual（人工核实填写）；
--   · 自动算不出（无历史月标准 / 跨度超 MaxAutoMonths）时必须手工填写，且 amount_note 记录原因；
--   · auto_amount 保留"自动计算的参考值"，便于审计"人工值 vs 自动值"差异。
-- 影响：本库 3 条追缴记录；仅命中 id=3（其余 2 条 monthly_amount > 0，为 Flat）。
-- 说明：只做加列/建表/回填，无 DROP/TRUNCATE；可重复执行（幂等）。
-- 执行：psql -h <host> -p 5432 -U <user> -d <db> -f docs/migrations/20261019_recovery_amount_source.sql

BEGIN;

-- ① 备份将被回填的行（幂等：仅首次留底）
CREATE TABLE IF NOT EXISTS nc_biz_recovery_records_bak_20261019_source (
    id              bigint      PRIMARY KEY,
    monthly_amount  numeric,
    recovery_amount numeric,
    updated_at      timestamp,
    backed_up_at    timestamp   NOT NULL DEFAULT NOW()
);

INSERT INTO nc_biz_recovery_records_bak_20261019_source (id, monthly_amount, recovery_amount, updated_at)
SELECT r.id, r.monthly_amount, r.recovery_amount, r.updated_at
FROM nc_biz_recovery_records r
ON CONFLICT (id) DO NOTHING;

-- ② 加列
ALTER TABLE nc_biz_recovery_records ADD COLUMN IF NOT EXISTS amount_source varchar(10) NOT NULL DEFAULT 'Flat';
ALTER TABLE nc_biz_recovery_records ADD COLUMN IF NOT EXISTS amount_note   text;
ALTER TABLE nc_biz_recovery_records ADD COLUMN IF NOT EXISTS auto_amount   numeric(14,2);

COMMENT ON COLUMN nc_biz_recovery_records.amount_source IS '金额来源：Segment=变更记录分段累加 / Flat=月标准×月数 / Manual=人工核实填写';
COMMENT ON COLUMN nc_biz_recovery_records.amount_note   IS '手工填写原因（Manual 时必填）或自动计算依据说明';
COMMENT ON COLUMN nc_biz_recovery_records.auto_amount   IS '自动计算的参考值（人工覆盖时保留，便于审计对比）';

-- ③ 存量回填
-- ③-1 已知人工核实的记录：张彦亮（历史台账缺失）
UPDATE nc_biz_recovery_records
SET amount_source = 'Manual',
    amount_note   = COALESCE(NULLIF(amount_note, ''),
                             '历史台账缺失（来源档案月保障额为 0，来源台账行已不存在），业务核实应追缴总额 16301 元'),
    updated_at    = NOW()
WHERE id = 3
  AND (amount_source IS DISTINCT FROM 'Manual' OR amount_note IS NULL);

-- ③-2 其余：有月标准→Flat；月标准缺失→Manual（缺原因时补默认说明）
UPDATE nc_biz_recovery_records
SET amount_source = CASE WHEN COALESCE(monthly_amount, 0) > 0 THEN 'Flat' ELSE 'Manual' END,
    amount_note   = CASE WHEN COALESCE(monthly_amount, 0) > 0 THEN amount_note
                         ELSE COALESCE(NULLIF(amount_note, ''), '来源档案月保障额缺失，手工核实填写') END,
    updated_at    = NOW()
WHERE id <> 3
  AND (COALESCE(monthly_amount, 0) = 0 AND amount_source IS DISTINCT FROM 'Manual');

-- ④ 分段明细表（列结构镜像 nc_biz_elderly_payback_segments，去掉类别档位、增加序号与依据来源）
CREATE TABLE IF NOT EXISTS nc_biz_recovery_segments (
    id                  BIGSERIAL     PRIMARY KEY,
    recovery_id         BIGINT        NOT NULL,
    segment_no          INTEGER       NOT NULL DEFAULT 1,
    monthly_amount      NUMERIC(14,2),
    segment_start_month VARCHAR(7),
    segment_end_month   VARCHAR(7),
    months              INTEGER,
    segment_amount      NUMERIC(14,2),
    source              VARCHAR(20),
    created_at          TIMESTAMP     NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE  nc_biz_recovery_segments IS '追缴金额自动计算的分段明细（依据来源 + 起止月 + 月标准 + 段金额）';
COMMENT ON COLUMN nc_biz_recovery_segments.source IS '分段依据：ChangeRecord=保障金变更记录 / Ledger=台账现值 / GracePeriod=渐退期原标准';

CREATE INDEX IF NOT EXISTS idx_recovery_segments_recovery_id ON nc_biz_recovery_segments (recovery_id);

-- 外键（幂等）：追缴记录删除时级联清理分段
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_recovery_segments_recovery') THEN
        ALTER TABLE nc_biz_recovery_segments
            ADD CONSTRAINT fk_recovery_segments_recovery
            FOREIGN KEY (recovery_id) REFERENCES nc_biz_recovery_records (id) ON DELETE CASCADE;
    END IF;
END $$;

COMMIT;

-- ⑤ 核对：3 行，id=3 应为 Manual / 16301.00 且带原因
\pset pager off
SELECT id, person_name, monthly_amount, recovery_months, recovery_amount,
       amount_source, auto_amount, left(COALESCE(amount_note, ''), 40) AS note
FROM nc_biz_recovery_records
ORDER BY id;

SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'nc_biz_recovery_segments'
ORDER BY ordinal_position;

SELECT count(*) AS segments_rows FROM nc_biz_recovery_segments;
