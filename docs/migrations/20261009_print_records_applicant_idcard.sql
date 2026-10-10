-- 20261009：打印留痕归属列（防证明打印历史串台）
-- 背景：档案查询页证明打印的 business_id 跨 6 套 id 空间（nc_biz_applications + 5 张导入台账），
--       (business_type, business_id) 键空间不隔离，id 碰撞即打印历史串台。
-- 方案：新增 applicant_id_card 归属列；新留痕由 PrintExecuteService 落户主身份证；
--       档案查询打印历史按归属过滤（NULL 不展示）。存量已于 20261009 全表清理归零，本列无回填需求。
-- 说明：SchemaService 启动时也会按 Resources\Schema\archive\database.yaml 幂等补列，本脚本供部署先行执行。
ALTER TABLE nc_biz_print_records
    ADD COLUMN IF NOT EXISTS applicant_id_card character varying(18);

COMMENT ON COLUMN nc_biz_print_records.applicant_id_card IS
    '申请人身份证号（打印留痕归属标记）：证明类打印按户主身份证区分键空间，防不同档案 id 碰撞串台；NULL=无归属';
