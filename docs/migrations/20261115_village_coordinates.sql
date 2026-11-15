-- 2026-11-15 村屯坐标列：照片水印「经纬度」改用**所在村屯查询信息**（不再取设备 GPS）。
-- 坐标值来源：① 本迁移只建机制、不造数据（坐标列先留空）；
-- ② 首次拍到某村屯照片且区划无坐标时，在线地理编码（Nominatim）查询并回写本列，
--    此后全库共享、不再联网（ArchivePhotoService.LookupVillageCoordinateAsync，含进程内负缓存）；
-- ③ 也可按需手工批量补录（UPDATE ... WHERE id = ...）。
-- Schema 同步：Resources/Schema/regions/database.yaml（nc_regions_villages 新增两列）。

ALTER TABLE nc_regions_villages ADD COLUMN IF NOT EXISTS longitude numeric(10,6);
ALTER TABLE nc_regions_villages ADD COLUMN IF NOT EXISTS latitude  numeric(10,6);

COMMENT ON COLUMN nc_regions_villages.longitude IS
    '村屯中心经度（照片水印经纬度用；在线地理编码回写，见 ArchivePhotoService.LookupVillageCoordinateAsync）';
COMMENT ON COLUMN nc_regions_villages.latitude IS
    '村屯中心纬度（同 longitude）';
