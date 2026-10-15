-- 20261015_caregivers_bank_account.sql
-- 照料人表新增分散照料银行卡字段（特困分散供养协议 {分散照料银行卡号}{分散照料开户行} 数据源）。
-- Schema 权威定义同步：Resources\Schema\social_assistance\caregivers.yaml
-- 幂等：IF NOT EXISTS 可重复执行。

ALTER TABLE nc_biz_caregivers
    ADD COLUMN IF NOT EXISTS bank_name varchar(100);

ALTER TABLE nc_biz_caregivers
    ADD COLUMN IF NOT EXISTS bank_account varchar(50);

COMMENT ON COLUMN nc_biz_caregivers.bank_name IS '分散照料银行卡开户行';
COMMENT ON COLUMN nc_biz_caregivers.bank_account IS '分散照料银行卡号（分散供养协议"分散照料银行卡号"取此列）';
