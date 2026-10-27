-- ============================================================================
-- 档案照片 WebP 存储迁移
-- 日期：2026-10-24
-- 背景：
--   手机端档案照片原为 JPEG（Helpers/ImageCompressor 旧实现，长边 ≤2000px、q85），
--   现统一改为 WebP，并要求单张硬上限 500KB。
--   新写入由 ArchivePhotoService.SavePhotoAsync 压缩并校验；本迁移只补列与约束。
--
-- 执行顺序：
--   1) 执行本迁移（新增 image_format，旧数据默认 jpeg；新写入立即受 500KB 约束）；
--   2) 部署新版本，新上传照片自动为 WebP；
--   3) 在「档案照片查询」页执行「历史照片转 WebP」批量转换；
--   4) 转换完成后（无失败项）再执行：
--        ALTER TABLE nc_biz_archive_photos VALIDATE CONSTRAINT ck_archive_photos_max_size;
--        VACUUM (ANALYZE) nc_biz_archive_photos;
-- ============================================================================

ALTER TABLE nc_biz_archive_photos
    ADD COLUMN IF NOT EXISTS image_format varchar(16) NOT NULL DEFAULT 'jpeg';

COMMENT ON COLUMN nc_biz_archive_photos.image_format IS
    '照片格式（webp/jpeg/png；2026-10-24 起新采集为 webp，旧数据默认 jpeg）';

COMMENT ON COLUMN nc_biz_archive_photos.photo_data IS
    '照片二进制（新采集为 WebP；旧数据可能为 JPEG/PNG）';

-- 兜底：历史 NULL/空格式统一标记 jpeg（正常 ADD COLUMN DEFAULT 已回填）
UPDATE nc_biz_archive_photos
   SET image_format = 'jpeg'
 WHERE image_format IS NULL OR image_format = '';

-- 单张 500KB 硬上限：NOT VALID 仅约束新写入/更新，旧数据待批量转换后再 VALIDATE
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_archive_photos_max_size') THEN
        ALTER TABLE nc_biz_archive_photos
            ADD CONSTRAINT ck_archive_photos_max_size
            CHECK (octet_length(photo_data) <= 512000) NOT VALID;
    END IF;
END $$;

-- 格式枚举约束（旧数据已统一 jpeg，可直接满足；仍用 NOT VALID 与上限约束保持同一节奏）
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_archive_photos_image_format') THEN
        ALTER TABLE nc_biz_archive_photos
            ADD CONSTRAINT ck_archive_photos_image_format
            CHECK (image_format IN ('webp', 'jpeg', 'png')) NOT VALID;
    END IF;
END $$;

-- 转换完成后可选执行：
-- ALTER TABLE nc_biz_archive_photos VALIDATE CONSTRAINT ck_archive_photos_image_format;
-- ALTER TABLE nc_biz_archive_photos VALIDATE CONSTRAINT ck_archive_photos_max_size;
