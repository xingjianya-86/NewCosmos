-- 一事一议：备案回执 + 长期公示标记（黑民规〔2026〕10 号）
-- 2026-11-25：备案回执仅存文件名/路径；长期公示标记在“通过结论”登记时置 true。
ALTER TABLE nc_biz_special_approval_forms
    ADD COLUMN IF NOT EXISTS filing_receipt varchar(500),
    ADD COLUMN IF NOT EXISTS is_long_term_publicized boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN nc_biz_special_approval_forms.filing_receipt
    IS '备案回执文件（文件名/路径）';
COMMENT ON COLUMN nc_biz_special_approval_forms.is_long_term_publicized
    IS '是否纳入长期公示（一事一议通过对象标记）';
