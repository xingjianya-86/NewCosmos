-- 待归属照片池（无身份证先拍照/选图，拿到身份证后再归属）
-- 2026-11-25：PC/扫描入口允许无身份证保存照片，归属时复用 nc_biz_archive_photos。
CREATE TABLE IF NOT EXISTS nc_biz_archive_photo_pending (
    id                   bigserial PRIMARY KEY,
    photo_data           bytea NOT NULL,
    image_format         varchar(16) NOT NULL DEFAULT 'webp',
    photo_category       varchar(30) NOT NULL DEFAULT 'OTHER_CERT',
    photo_name           varchar(200),
    file_size            bigint NOT NULL DEFAULT 0,
    recognized_name      varchar(100),
    recognized_id_card   varchar(30),
    source               varchar(30),
    status               varchar(20) NOT NULL DEFAULT 'Pending',
    assigned_photo_id    bigint,
    assigned_at          timestamp,
    created_by           integer,
    created_by_name      varchar(100),
    created_at           timestamp NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE nc_biz_archive_photo_pending IS '待归属照片池：身份证未定时先存，归属后写入 nc_biz_archive_photos';
COMMENT ON COLUMN nc_biz_archive_photo_pending.recognized_name IS 'OCR 识别姓名（可空）';
COMMENT ON COLUMN nc_biz_archive_photo_pending.recognized_id_card IS 'OCR 识别身份证号（可空）';
COMMENT ON COLUMN nc_biz_archive_photo_pending.status IS 'Pending待归属 / Assigned已归属';

CREATE INDEX IF NOT EXISTS idx_archive_photo_pending_status
    ON nc_biz_archive_photo_pending (status, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_archive_photo_pending_id_card
    ON nc_biz_archive_photo_pending (recognized_id_card);
