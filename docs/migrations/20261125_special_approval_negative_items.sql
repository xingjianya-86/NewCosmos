-- 一事一议负面清单留痕（黑民规〔2026〕10 号）
-- 2026-11-25：提交时命中负面清单会被阻断；本列保存经办人勾选项（code 逗号分隔），供追溯。
ALTER TABLE nc_biz_special_approval_forms
    ADD COLUMN IF NOT EXISTS negative_items varchar(500);

COMMENT ON COLUMN nc_biz_special_approval_forms.negative_items
    IS '负面清单勾选项（code 逗号分隔；正常提交应为空，留痕用）';
