-- 20261014：推送打印队列支持「照片远程打印」任务类型
-- 背景：手机端采集证件/核查照片（nc_biz_archive_photos，批次二）后，需推送到 PC 端由打印代理直接打印照片。
--       原队列仅支持模板文书（job 无模板ID即在 PrintAgentService 硬失败）。本批新增任务类型分支。
-- 方案：新增 job_kind（Template/Photo）区分任务；照片以 bytea 内联入队（phone 已压缩 JPEG，PC 直接打印，
--       不回查照片表，避免跨表耦合）；photo_layout 控制版式；photo_category 供展示/留痕。
-- 说明：SchemaService 启动时也会按 Resources\Schema\printing\database.yaml 幂等补列，本脚本供部署先行执行。
ALTER TABLE nc_biz_print_jobs
    ADD COLUMN IF NOT EXISTS job_kind character varying(20) NOT NULL DEFAULT 'Template',
    ADD COLUMN IF NOT EXISTS photo_data bytea,
    ADD COLUMN IF NOT EXISTS photo_layout character varying(20),
    ADD COLUMN IF NOT EXISTS photo_category character varying(30);

COMMENT ON COLUMN nc_biz_print_jobs.job_kind IS
    '任务类型：Template=模板文书（走 PrintExecuteService 模板打印）, Photo=照片（PC 代理直接打印图片）';
COMMENT ON COLUMN nc_biz_print_jobs.photo_data IS
    '照片二进制（JPEG，job_kind=Photo 时有值；PC 代理直接打印，无需回查照片表）';
COMMENT ON COLUMN nc_biz_print_jobs.photo_layout IS
    '照片打印版式：FULL_PAGE整页 / HALF_PAGE半页 / ID_CARD证件原寸';
COMMENT ON COLUMN nc_biz_print_jobs.photo_category IS
    '照片类别（ID_CARD身份证 / HOUSEHOLD_REGISTER户口本 / PUBLICITY_CHECK公示核查 / OTHER_CERT其他证件，展示/留痕用）';
