-- 2026-09-24: 渐退期内实际应发月保障金（超限封顶，不含分类施保）
-- 例：原户享受 1000 元/月，户主死亡后 1 人，农村标准 632 上限 → 渐退期内应发 632
ALTER TABLE nc_biz_grace_periods
    ADD COLUMN IF NOT EXISTS grace_grant_amount DECIMAL(12,2);

COMMENT ON COLUMN nc_biz_grace_periods.grace_grant_amount IS
    '渐退期内实际应发月保障金（原额超户口类型上限时已封顶，不含分类施保）';
