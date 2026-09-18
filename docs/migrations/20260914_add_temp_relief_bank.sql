-- 临时救助档案 开户行 / 银行卡号：nc_biz_temp_relief_applications.bank_name / bank_account
-- 用途：临时救助申请表录入；来源台账预填；月报"临时救助新增汇总表"一卡通账号取 bank_account。
-- 执行：psql -h <host> -U new_cosmos -d new_cosmos -f 20260914_add_temp_relief_bank.sql

ALTER TABLE nc_biz_temp_relief_applications ADD COLUMN IF NOT EXISTS bank_name varchar(200);
ALTER TABLE nc_biz_temp_relief_applications ADD COLUMN IF NOT EXISTS bank_account varchar(64);

COMMENT ON COLUMN nc_biz_temp_relief_applications.bank_name IS '开户行（银行卡开户行）';
COMMENT ON COLUMN nc_biz_temp_relief_applications.bank_account IS '银行卡号/一卡通账号（汇总表"一卡通账号"取此列）';

-- 回填：仅对有银行列的来源台账（rural/urban/destitute）按 source_family_id 精确匹配；
-- 一卡通账号优先 one_card_account、否则 bank_account；开户行取 bank_name。
-- （低保边缘/刚性支出台账无银行列，跳过；后续可按需补列）
UPDATE nc_biz_temp_relief_applications t
SET bank_name = f.bank_name,
    bank_account = COALESCE(NULLIF(f.one_card_account, ''), f.bank_account)
FROM nc_biz_rural_subsistence_families f
WHERE t.source_table = 'nc_biz_rural_subsistence_families'
  AND t.source_family_id = f.id
  AND t.deleted_at IS NULL
  AND (t.bank_account IS NULL OR t.bank_account = '');

UPDATE nc_biz_temp_relief_applications t
SET bank_name = f.bank_name,
    bank_account = COALESCE(NULLIF(f.one_card_account, ''), f.bank_account)
FROM nc_biz_urban_subsistence_families f
WHERE t.source_table = 'nc_biz_urban_subsistence_families'
  AND t.source_family_id = f.id
  AND t.deleted_at IS NULL
  AND (t.bank_account IS NULL OR t.bank_account = '');

UPDATE nc_biz_temp_relief_applications t
SET bank_name = f.bank_name,
    bank_account = COALESCE(NULLIF(f.one_card_account, ''), f.bank_account)
FROM nc_biz_destitute_families f
WHERE t.source_table = 'nc_biz_destitute_families'
  AND t.source_family_id = f.id
  AND t.deleted_at IS NULL
  AND (t.bank_account IS NULL OR t.bank_account = '');

-- 校验
SELECT id, applicant_name, bank_name, bank_account
FROM nc_biz_temp_relief_applications
WHERE deleted_at IS NULL
ORDER BY id;
