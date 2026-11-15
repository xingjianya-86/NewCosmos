using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Utilities;
using NewCosmos.Models.Enums;
using System.Collections.Concurrent;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 档案照片服务实现：证件/核查照片 bytea 存库（仿 nc_biz_signatures 先例，避开输出根迁移断链）。
/// 新采集统一压缩为 WebP（单张 ≤500KB）后入库；旧 JPEG/PNG 通过 image_format 与魔数识别兼容。
/// 列表查询只取元数据（不含 photo_data），预览/打印时按 Id 单独取二进制，避免列表传输大字段。
/// </summary>
public class ArchivePhotoService : BaseService, IArchivePhotoService
{
    protected override string ServiceName => "ArchivePhotoService";

    private readonly IDatabaseService _db;
    private readonly IBusinessTimelineService _timeline;

    /// <summary>村屯坐标进程内缓存（键=县|镇|村；null=负缓存，本进程失败不再反复打网络）。</summary>
    private readonly ConcurrentDictionary<string, (double Longitude, double Latitude)?> _geoCoordinateCache = new();

    /// <summary>地理编码复用的 HttpClient（Singleton 服务内共享，线程安全；惯例同 HolidayManageService）。</summary>
    private static readonly HttpClient _geoHttpClient = CreateGeoHttpClient();

    private static HttpClient CreateGeoHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = global::System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(PhotoWatermarkConstants.GeocodeTimeoutSeconds)
        });
        client.Timeout = TimeSpan.FromSeconds(PhotoWatermarkConstants.GeocodeTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(PhotoWatermarkConstants.GeocodeUserAgent);
        return client;
    }

    public ArchivePhotoService(
        IDatabaseService db,
        IBusinessTimelineService timeline,
        ILoggerService logger) : base(logger)
    {
        _db = db;
        _timeline = timeline;
    }

    /// <summary>
    /// 人员维度三域聚合 CTE（**单点维护**）：低收入/高龄/临时救助 UNION → 按 $1 业务域 / $2 关键词过滤
    /// → 按身份证聚合成 counted（业务域、代表业务ID、已传照片张数）。
    /// 补传候选检索与 PC 插入归属人解析共用，禁止在别处另抄一份。
    /// </summary>
    private const string PersonCte = @"
WITH biz AS (
    SELECT 'ElderlyBenefits'::text AS business_type, e.id AS business_id,
           e.name AS person_name, e.id_card
    FROM nc_biz_elderly_applications e
    WHERE e.deleted_at IS NULL

    UNION ALL

    SELECT 'SocialAssistance', a.id, a.applicant_name, a.applicant_id_card
    FROM nc_biz_applications a
    WHERE a.deleted_at IS NULL

    UNION ALL

    SELECT 'TempRelief', t.id, t.applicant_name, t.applicant_id_card
    FROM nc_biz_temp_relief_applications t
    WHERE t.deleted_at IS NULL
),
filtered AS (
    SELECT * FROM biz
    WHERE ($1::text = '' OR business_type = $1::text)
      AND ($2::text = '' OR person_name ILIKE '%' || $2::text || '%' OR id_card ILIKE '%' || $2::text || '%')
),
people AS (
    SELECT id_card,
           MAX(person_name) AS person_name,
           string_agg(DISTINCT business_type, '/' ORDER BY business_type) AS business_types,
           MAX(business_id) AS business_id
    FROM filtered
    GROUP BY id_card
),
counted AS (
    SELECT p.id_card, p.person_name, p.business_types, p.business_id,
           COALESCE(c.photo_count, 0)::int AS photo_count
    FROM people p
    LEFT JOIN (
        SELECT applicant_id_card, COUNT(*)::int AS photo_count
        FROM nc_biz_archive_photos
        GROUP BY applicant_id_card
    ) c ON c.applicant_id_card = p.id_card
)";

    public async Task<Result<long>> SavePhotoAsync(ArchivePhoto photo, PhotoWatermarkInfo? watermark = null, CancellationToken ct = default)
    {
        if (photo == null)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片数据不能为空");
        if (photo.PhotoData == null || photo.PhotoData.Length == 0)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片内容为空");
        if (string.IsNullOrWhiteSpace(photo.ApplicantIdCard))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片缺少身份证号关联");

        // 水印（仅「入户排查/公示核查」类别，且调用方给了上下文）：先烧水印、再统一压缩——
        // 保证水印后的字节仍走 500KB 硬校验；绘制失败按原图继续保存并记 Warn（保照片优先于水印）。
        var bytes = photo.PhotoData;
        var category = string.IsNullOrWhiteSpace(photo.PhotoCategory)
            ? PhotoCategoryConstants.OTHER_CERT
            : photo.PhotoCategory;
        if (watermark != null && PhotoCategoryConstants.RequiresWatermark(category))
        {
            var filled = await FillWatermarkContextAsync(watermark, photo.ApplicantIdCard.Trim(), category, ct);
            var displayName = PhotoCategoryConstants.GetDisplayName(category);
            var watermarked = await Task.Run(
                () => PhotoWatermarkHelper.Apply(bytes, filled, displayName), ct);
            if (watermarked != null && watermarked.Length > 0)
            {
                bytes = watermarked;
            }
            else
            {
                LogWarn($"照片水印绘制失败，按原图入库: Category={category}, IdCard尾4={PhotoNameBuilder.IdCardLast4(photo.ApplicantIdCard)}");
            }
        }

        // 唯一写入收口：统一压缩为 WebP 并校验 500KB 硬上限，禁止原图旁路入库。
        var compressResult = await Task.Run(
            () => ImageCompressor.CompressToWebP(bytes, photo.PhotoCategory), ct);
        if (compressResult.IsFailure)
        {
            LogWarn($"照片压缩失败: {compressResult.Message}");
            return Result.Failure<long>(compressResult.ErrorCode!, compressResult.Message!);
        }

        var compressed = compressResult.Value;
        photo.PhotoData = compressed.Data;
        photo.FileSize = compressed.Data.LongLength;
        photo.ImageFormat = PhotoFormatConstants.WebP;

        if (!ImageCompressor.IsWebP(photo.PhotoData))
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片压缩结果不是 WebP，拒绝入库");
        }

        if (photo.PhotoData.LongLength > PhotoCompressionConstants.HardMaxBytes)
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED,
                $"照片超过 {PhotoCompressionConstants.HardMaxBytes / 1024}KB 上限，拒绝入库");
        }

        const string sql = @"INSERT INTO nc_biz_archive_photos
                (photo_data, image_format, applicant_id_card, application_id, photo_category,
                 photo_name, file_size, created_by, created_by_name, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, NOW())
                RETURNING id";

        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            photo.PhotoData,
            photo.ImageFormat,
            photo.ApplicantIdCard,
            photo.ApplicationId,
            string.IsNullOrWhiteSpace(photo.PhotoCategory) ? PhotoCategoryConstants.OTHER_CERT : photo.PhotoCategory,
            photo.PhotoName,
            photo.FileSize,
            photo.CreatedBy,
            photo.CreatedByName);

        if (result.IsFailure)
        {
            LogError($"照片保存失败: {result.Message}");
            return result;
        }

        LogInfo($"照片保存成功: Id={result.Value}, Category={photo.PhotoCategory}, Format={photo.ImageFormat}, " +
                $"Size={photo.FileSize}B, Original={compressed.OriginalBytes}B, " +
                $"{compressed.Width}x{compressed.Height}, Q={compressed.Quality}");
        return Result.Success(result.Value);
    }

    /// <summary>
    /// 水印上下文按批补齐（2026-11-15 二改）：姓名 ← 户主档案聚合；地址 ← 档案区划代号 JOIN 区划表；
    /// 经纬度 ← **所在村屯查询信息**（区划坐标列，缺失在线地理编码回写；不再取设备 GPS）；
    /// 时间 ← **档案时间轴时间**（入户调查日/公示起日）+ 对应记录创建时分，全取不到回退采集时刻。
    /// 快照语义：<see cref="PhotoWatermarkInfo.IsResolved"/> 置位后逐张保存不再查库（§8 批内禁 N+1）；
    /// 补齐失败也置位并记 Warn——缺失字段对应水印行省略，绝不阻断照片保存。
    /// </summary>
    public async Task<PhotoWatermarkInfo> FillWatermarkContextAsync(
        PhotoWatermarkInfo watermark, string idCard, string category, CancellationToken ct = default)
    {
        if (watermark.IsResolved) return watermark;

        if (!PhotoCategoryConstants.RequiresWatermark(category) || string.IsNullOrWhiteSpace(idCard))
            return watermark with { IsResolved = true };

        try
        {
            var name = watermark.PersonName?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                var owner = await ResolveOwnerAsync(idCard, ct);
                if (owner.IsSuccess && !string.IsNullOrWhiteSpace(owner.Value?.PersonName))
                    name = owner.Value!.PersonName.Trim();
            }

            var address = watermark.Address?.Trim() ?? string.Empty;
            double? lat = null, lng = null;

            var partsResult = await GetWatermarkAddressPartsAsync(idCard, ct);
            var parts = partsResult.IsSuccess ? partsResult.Value : null;
            if (parts != null)
            {
                if (address.Length == 0)
                    address = AddressResolver.BuildWatermarkAddress(parts.County, parts.Town, parts.Village);

                var coords = await LookupVillageCoordinateAsync(parts, ct);
                if (coords.HasValue)
                {
                    // 固定地理编码值（2026-11-15 定稿）：同村屯恒同坐标，不叠加随机偏差
                    lat = coords.Value.Latitude;
                    lng = coords.Value.Longitude;
                }
            }

            var eventTime = await ResolveWatermarkTimeAsync(idCard, category, watermark.CapturedAt, ct);

            return watermark with
            {
                PersonName = name,
                IdCard = string.IsNullOrWhiteSpace(watermark.IdCard) ? idCard.Trim() : watermark.IdCard.Trim(),
                Address = address,
                CapturedAt = eventTime,
                Latitude = lat,
                Longitude = lng,
                IsResolved = true,
            };
        }
        catch (Exception ex)
        {
            LogWarn($"水印上下文补齐失败（按已有字段入库，缺失行省略）: {ex.Message}");
            return watermark with { IsResolved = true };
        }
    }

    // ── 水印取数行类（实体映射路径，带属性才可映射；禁止改回元组查询） ──

    private sealed class WatermarkAddressRow
    {
        public string? County { get; set; }
        public string? Town { get; set; }
        public string? Village { get; set; }
        public string? AltTown { get; set; }
        public long? VillageId { get; set; }
    }

    private sealed class VillageGeoRow
    {
        public long Id { get; set; }
        public double? Longitude { get; set; }
        public double? Latitude { get; set; }
    }

    private sealed class SurveyRow
    {
        public DateTime? SurveyDate { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    private sealed class PublicityRow
    {
        public DateTime? PublicizeStartDate { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>地址/坐标共用的区划部件（临时救助镇列回退已在构造时并入 Town）。</summary>
    private sealed record WatermarkAddressParts(string? County, string? Town, string? Village, long? VillageId);

    // 三域按优先级取首个有地址的档案：① 低收入 —— 优先区划代号列 JOIN nc_regions_* 转名称
    // （区划表为名称单一事实来源），该级代号为空才回退同级名称文本列；② 高龄 —— family_* 代号 JOIN；
    // ③ 临时救助 —— 无区划代号列读名称文本（镇列回退 family_town）。值全部参数化，表名静态常量。
    private const string WatermarkAddressSql = @"
WITH addr AS (
    SELECT 1 AS prio,
           COALESCE(NULLIF(rc.county_name, ''), NULLIF(a.district, '')) AS county,
           COALESCE(NULLIF(rt.town_name, ''), NULLIF(a.town, '')) AS town,
           COALESCE(NULLIF(rv.village_name, ''), NULLIF(a.community, '')) AS village,
           NULL::text AS alt_town,
           a.village_id
    FROM nc_biz_applications a
    LEFT JOIN nc_regions_counties rc ON rc.id = a.county_id
    LEFT JOIN nc_regions_towns rt ON rt.id = a.town_id
    LEFT JOIN nc_regions_villages rv ON rv.id = a.village_id
    WHERE a.applicant_id_card = $1 AND a.deleted_at IS NULL
    UNION ALL
    SELECT 2, NULLIF(rc.county_name, ''), NULLIF(rt.town_name, ''), NULLIF(rv.village_name, ''), NULL::text, rv.id
    FROM nc_biz_elderly_applications e
    LEFT JOIN nc_regions_counties rc ON rc.id = e.family_county_id
    LEFT JOIN nc_regions_towns rt ON rt.id = e.family_town_id
    LEFT JOIN nc_regions_villages rv ON rv.id = e.family_village_id
    WHERE e.id_card = $1 AND e.deleted_at IS NULL
    UNION ALL
    SELECT 3, NULLIF(t.family_district, ''), NULLIF(t.town, ''), NULLIF(t.village, ''), NULLIF(t.family_town, ''), NULL::bigint
    FROM nc_biz_temp_relief_applications t
    WHERE t.applicant_id_card = $1 AND t.deleted_at IS NULL
)
SELECT county, town, village, alt_town, village_id
FROM addr
WHERE COALESCE(county, '') <> '' OR COALESCE(town, '') <> '' OR COALESCE(village, '') <> ''
ORDER BY prio
LIMIT 1";

    /// <summary>入户调查记录（低收入域，最新一条）：日期=survey_date，时分=记录创建时刻。</summary>
    private const string WatermarkSurveySql = @"
SELECT s.survey_date, s.created_at
FROM nc_biz_household_surveys s
JOIN nc_biz_applications a ON a.id = s.application_id
WHERE a.applicant_id_card = $1 AND a.deleted_at IS NULL AND s.deleted_at IS NULL
ORDER BY s.id DESC
LIMIT 1";

    /// <summary>临时救助公示起日（直存列，最新一条）。</summary>
    private const string WatermarkPublicitySql = @"
SELECT publicize_start_date, created_at
FROM nc_biz_temp_relief_applications
WHERE applicant_id_card = $1 AND deleted_at IS NULL
ORDER BY id DESC
LIMIT 1";

    /// <summary>低收入档案锚点行（ArchiveAnchorHelper 解析申请日期所需列，最新一条）。</summary>
    private const string WatermarkAppAnchorSql = @"
SELECT application_no, created_at, first_approved_at, data_completed_at, updated_at
FROM nc_biz_applications
WHERE applicant_id_card = $1 AND deleted_at IS NULL
ORDER BY id DESC
LIMIT 1";

    private async Task<Result<WatermarkAddressParts?>> GetWatermarkAddressPartsAsync(string idCard, CancellationToken ct)
    {
        var result = await _db.QuerySingleAsync<WatermarkAddressRow>(WatermarkAddressSql, ct, idCard.Trim());
        if (result.IsFailure)
            return Result.Failure<WatermarkAddressParts?>(result.ErrorCode!, result.Message!);

        var row = result.Value;
        if (row == null ||
            (string.IsNullOrWhiteSpace(row.County) && string.IsNullOrWhiteSpace(row.Town) &&
             string.IsNullOrWhiteSpace(row.Village) && string.IsNullOrWhiteSpace(row.AltTown)))
        {
            return Result.Success<WatermarkAddressParts?>(null);   // 三域均无地址记录：水印省略地址行
        }

        var town = string.IsNullOrWhiteSpace(row.Town) ? row.AltTown : row.Town;
        return Result.Success<WatermarkAddressParts?>(
            new WatermarkAddressParts(row.County, town, row.Village, row.VillageId));
    }

    /// <summary>
    /// 村屯坐标：① 区划坐标列（有代号按 id 直查，无代号按 县/镇/村 名称匹配）；
    /// ② 列无坐标 → 在线地理编码（Nominatim，超时 <see cref="PhotoWatermarkConstants.GeocodeTimeoutSeconds"/>）
    /// 命中村行则**回写坐标列**（此后本机/全服不再联网）；③ 失败 → 省略水印经纬度行（负缓存不反复打网络）。
    /// </summary>
    private async Task<(double Longitude, double Latitude)?> LookupVillageCoordinateAsync(
        WatermarkAddressParts parts, CancellationToken ct)
    {
        var village = AddressResolver.NormalizeVillageForAddress(parts.Village);
        if (string.IsNullOrEmpty(village)) return null;   // 镇直/「本级」：无村级单位，不出坐标行

        var key = $"{parts.County}|{parts.Town}|{village}";
        if (_geoCoordinateCache.TryGetValue(key, out var cached)) return cached;

        (double Longitude, double Latitude)? result = null;
        try
        {
            long rowId = 0;
            double? rowLng = null, rowLat = null;

            if (parts.VillageId.HasValue)
            {
                var byId = await _db.QuerySingleAsync<VillageGeoRow>(
                    "SELECT id, longitude::double precision, latitude::double precision FROM nc_regions_villages WHERE id = $1",
                    ct, parts.VillageId.Value);
                if (byId.Value != null)
                    (rowId, rowLng, rowLat) = (byId.Value.Id, byId.Value.Longitude, byId.Value.Latitude);
            }
            else
            {
                const string byNameSql = @"
SELECT v.id, v.longitude::double precision, v.latitude::double precision
FROM nc_regions_villages v
JOIN nc_regions_towns t ON t.id = v.town_id
JOIN nc_regions_counties c ON c.id = t.county_id
WHERE v.village_name = $1
  AND ($2::text = '' OR t.town_name = $2)
  AND ($3::text = '' OR c.county_name LIKE '%' || $3)
  AND v.is_active = TRUE
ORDER BY v.id
LIMIT 1";
                var byName = await _db.QuerySingleAsync<VillageGeoRow>(
                    byNameSql, ct, village, parts.Town?.Trim() ?? string.Empty, parts.County?.Trim() ?? string.Empty);
                if (byName.Value != null)
                    (rowId, rowLng, rowLat) = (byName.Value.Id, byName.Value.Longitude, byName.Value.Latitude);
            }

            if (rowLng.HasValue && rowLat.HasValue)
            {
                result = (rowLng.Value, rowLat.Value);
            }
            else
            {
                var url = NominatimGeocoder.BuildSearchUrl(parts.County, parts.Town, village);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(PhotoWatermarkConstants.GeocodeTimeoutSeconds));
                var json = await _geoHttpClient.GetStringAsync(url, timeoutCts.Token);
                var geo = NominatimGeocoder.ParseSearchResponse(json);
                if (geo.HasValue)
                {
                    result = geo;
                    if (rowId > 0)
                    {
                        var updated = await _db.ExecuteNonQueryAsync(
                            "UPDATE nc_regions_villages SET longitude = $1, latitude = $2 WHERE id = $3",
                            ct, geo.Value.Longitude, geo.Value.Latitude, rowId);
                        if (updated.IsSuccess)
                            LogInfo($"村屯坐标已在线定位并回写: {village} → {geo.Value.Longitude:F6},{geo.Value.Latitude:F6}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // 页面离开/令牌取消：不缓存、不留痕
        }
        catch (Exception ex)
        {
            LogWarn($"村屯坐标定位失败（水印省略经纬度行）: {village}: {ex.Message}");
        }

        _geoCoordinateCache[key] = result;   // null 也缓存（负缓存）
        return result;
    }

    /// <summary>
    /// 水印时间（2026-11-15 二改：档案时间轴时间 + 记录创建时分）：
    /// 入户排查 → ① 入户调查记录日期+其创建时分 → ② B线时间轴「入户调查日期」（SurveyDateResolver
    /// 与档案产出同口径）+ 档案记录创建时分；公示核查 → ① 临时救助公示起日+其创建时分 →
    /// ② B线时间轴公示起日 + 档案记录创建时分；全取不到回退采集时刻（带时分）。
    /// </summary>
    private async Task<DateTime> ResolveWatermarkTimeAsync(
        string idCard, string category, DateTime captureTime, CancellationToken ct)
    {
        try
        {
            if (category == PhotoCategoryConstants.HOUSEHOLD_INSPECTION)
            {
                var survey = await _db.QuerySingleAsync<SurveyRow>(WatermarkSurveySql, ct, idCard);
                var surveyDate = survey.Value?.SurveyDate ?? survey.Value?.CreatedAt?.Date;
                if (surveyDate.HasValue)
                    return CombineDateAndTime(surveyDate.Value,
                        survey.Value!.CreatedAt?.TimeOfDay ?? captureTime.TimeOfDay);

                var (businessDate, tl, recordTime) = await ResolveBusinessTimelineAsync(idCard, captureTime, ct);
                if (businessDate.HasValue && tl != null)
                    return CombineDateAndTime(
                        SurveyDateResolver.Resolve(tl, null, businessDate.Value),
                        recordTime ?? captureTime.TimeOfDay);
            }
            else if (category == PhotoCategoryConstants.PUBLICITY_CHECK)
            {
                var pr = await _db.QuerySingleAsync<PublicityRow>(WatermarkPublicitySql, ct, idCard);
                if (pr.Value?.PublicizeStartDate.HasValue == true)
                    return CombineDateAndTime(pr.Value.PublicizeStartDate.Value,
                        pr.Value.CreatedAt?.TimeOfDay ?? captureTime.TimeOfDay);

                var (_, tl, recordTime) = await ResolveBusinessTimelineAsync(idCard, captureTime, ct);
                if (tl != null && tl.PublicityStartDate != default)
                    return CombineDateAndTime(tl.PublicityStartDate, recordTime ?? captureTime.TimeOfDay);
            }
        }
        catch (Exception ex)
        {
            LogWarn($"水印时间取档案时间轴失败，回退采集时刻: {ex.Message}");
        }

        return captureTime;
    }

    /// <summary>
    /// 业务锚点 + B 线时间轴（复用 ArchiveAnchorHelper / SurveyDateResolver / IBusinessTimelineService
    /// 单点口径，与档案产出同源，禁止另抄）：返回业务锚点日期、B线时间轴与档案记录创建时分。
    /// </summary>
    private async Task<(DateTime? BusinessDate, TimelineResult? Timeline, TimeSpan? RecordTime)> ResolveBusinessTimelineAsync(
        string idCard, DateTime fallbackTime, CancellationToken ct)
    {
        var appResult = await _db.QuerySingleAsync<ApplicationEntity>(WatermarkAppAnchorSql, ct, idCard);
        var app = appResult.Value;
        if (app == null) return (null, null, null);

        var applicationDate = ArchiveAnchorHelper.ResolveApplicationDate(app);

        DateTime? surveyDate = null;
        var survey = await _db.QuerySingleAsync<SurveyRow>(WatermarkSurveySql, ct, idCard);
        if (survey.Value?.SurveyDate is { } sd && sd != default)
            surveyDate = sd;

        var appCycle = _timeline.GetTimelineForDate(applicationDate, TimelineType.BusinessProcess);
        var surveyCycle = (0, 0);
        if (surveyDate is { } s2 && s2 != default)
        {
            var sc = _timeline.GetTimelineForDate(s2.Date, TimelineType.BusinessProcess);
            surveyCycle = (sc.Year, sc.Month);
        }

        var businessDate = ArchiveAnchorHelper.ResolveBusinessAnchorDate(
            applicationDate, surveyDate, (appCycle.Year, appCycle.Month), surveyCycle);
        var tl = _timeline.GetTimelineForApplicationDate(businessDate, TimelineType.BusinessProcess);
        var recordTime = app.CreatedAt != default ? app.CreatedAt.TimeOfDay : fallbackTime.TimeOfDay;
        return (businessDate, tl, recordTime);
    }

    private static DateTime CombineDateAndTime(DateTime date, TimeSpan time) => date.Date.Add(time);

    /// <inheritdoc/>
    public async Task<Result<string>> GetWatermarkAddressAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Failure<string>(ErrorCodes.VALIDATION_FAILED, "身份证号不能为空");

        // 地址部件查询单点（含区划代号 JOIN 转名称 + 名称文本兕底 + 村代号），此处只负责拼装展示串
        var partsResult = await GetWatermarkAddressPartsAsync(idCard, ct);
        if (partsResult.IsFailure)
            return Result.Failure<string>(partsResult.ErrorCode!, partsResult.Message!);
        if (partsResult.Value == null)
            return Result.Success(string.Empty); // 三域均无地址记录：水印省略地址行

        var parts = partsResult.Value;
        return Result.Success(AddressResolver.BuildWatermarkAddress(parts.County, parts.Town, parts.Village));
    }

    public async Task<Result<List<ArchivePhoto>>> GetByPersonAsync(
        string applicantIdCard, string? category = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(applicantIdCard))
            return Result.Failure<List<ArchivePhoto>>(ErrorCodes.VALIDATION_FAILED, "身份证号不能为空");

        // 列表只取元数据，排除 photo_data 大字段
        const string sql = @"SELECT id, applicant_id_card, application_id, photo_category,
                photo_name, file_size, image_format, created_by, created_by_name, created_at
                FROM nc_biz_archive_photos
                WHERE applicant_id_card = $1
                  AND ($2::varchar IS NULL OR photo_category = $2)
                ORDER BY created_at DESC
                LIMIT 200";

        var result = await _db.QueryAsync<ArchivePhoto>(sql, ct, applicantIdCard, category);
        if (result.IsFailure)
        {
            LogError($"照片列表查询失败: {result.Message}");
            return Result.Failure<List<ArchivePhoto>>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value ?? new List<ArchivePhoto>());
    }

    public async Task<Result<byte[]>> GetPhotoDataAsync(long photoId, CancellationToken ct = default)
    {
        const string sql = "SELECT photo_data FROM nc_biz_archive_photos WHERE id = $1";
        var result = await _db.QuerySingleAsync<byte[]>(sql, ct, photoId);

        if (result.IsFailure)
        {
            LogError($"照片二进制查询失败: {result.Message}");
            return Result.Failure<byte[]>(result.ErrorCode!, result.Message!);
        }

        if (result.Value == null || result.Value.Length == 0)
            return Result.Failure<byte[]>(ErrorCodes.NOT_FOUND, $"照片不存在或为空: Id={photoId}");

        return Result.Success(result.Value);
    }

    public async Task<Result<bool>> DeletePhotoAsync(long photoId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM nc_biz_archive_photos WHERE id = $1";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, photoId);
        if (result.IsFailure)
        {
            LogError($"照片删除失败: {result.Message}");
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);
        }

        LogInfo($"照片删除: Id={photoId}");
        return Result.Success(true);
    }

    /// <summary>
    /// 照片补传候选（人员维度）：三域 UNION ALL → 按业务域/关键词过滤 → 按身份证聚合（业务域、代表业务ID）
    /// → LEFT JOIN 照片张数。不做"未传"过滤：已传照片的人同样可继续补传。
    /// </summary>
    public async Task<Result<PagedResult<PhotoBackfillCandidate>>> GetBackfillCandidatesAsync(
        string? businessType, string? keyword,
        int pageIndex, int pageSize, CancellationToken ct = default)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);

        const string cte = PersonCte;

        var type = businessType ?? string.Empty;
        var kw = keyword?.Trim() ?? string.Empty;

        var countResult = await _db.ExecuteScalarAsync<long>(
            cte + " SELECT COUNT(*) FROM counted", ct, type, kw);
        if (countResult.IsFailure)
        {
            LogError($"补传候选计数失败: {countResult.Message}");
            return Result.Failure<PagedResult<PhotoBackfillCandidate>>(countResult.ErrorCode!, countResult.Message!);
        }

        var listResult = await _db.QueryAsync<PhotoBackfillCandidate>(
            cte + @"
SELECT id_card, person_name, business_types, business_id, photo_count
FROM counted
ORDER BY person_name, id_card
LIMIT $3 OFFSET $4",
            ct, type, kw, pageSize, (pageIndex - 1) * pageSize);

        if (listResult.IsFailure)
        {
            LogError($"补传候选查询失败: {listResult.Message}");
            return Result.Failure<PagedResult<PhotoBackfillCandidate>>(listResult.ErrorCode!, listResult.Message!);
        }

        return Result.Success(new PagedResult<PhotoBackfillCandidate>
        {
            Items = listResult.Value ?? new List<PhotoBackfillCandidate>(),
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = (int)countResult.Value
        });
    }

    /// <summary>照片查询（桌面端/导出用）：关键词匹配身份证/备注，类别可过滤，返回元数据分页。</summary>
    public async Task<Result<PagedResult<ArchivePhoto>>> GetPhotosPagedAsync(
        string? keyword, string? category, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);

        const string where = @"
WHERE ($1::text = '' OR applicant_id_card ILIKE '%' || $1::text || '%'
       OR COALESCE(photo_name, '') ILIKE '%' || $1::text || '%')
  AND ($2::text = '' OR photo_category = $2::text)";

        var kw = keyword?.Trim() ?? string.Empty;
        var cat = category ?? string.Empty;

        var countResult = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_archive_photos" + where, ct, kw, cat);
        if (countResult.IsFailure)
        {
            LogError($"照片查询计数失败: {countResult.Message}");
            return Result.Failure<PagedResult<ArchivePhoto>>(countResult.ErrorCode!, countResult.Message!);
        }

        var listResult = await _db.QueryAsync<ArchivePhoto>(
            @"SELECT id, applicant_id_card, application_id, photo_category, photo_name,
                     file_size, image_format, created_by, created_by_name, created_at
              FROM nc_biz_archive_photos" + where + @"
              ORDER BY created_at DESC, id DESC
              LIMIT $3 OFFSET $4",
            ct, kw, cat, pageSize, (pageIndex - 1) * pageSize);

        if (listResult.IsFailure)
        {
            LogError($"照片查询失败: {listResult.Message}");
            return Result.Failure<PagedResult<ArchivePhoto>>(listResult.ErrorCode!, listResult.Message!);
        }

        return Result.Success(new PagedResult<ArchivePhoto>
        {
            Items = listResult.Value ?? new List<ArchivePhoto>(),
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = (int)countResult.Value
        });
    }

    /// <summary>
    /// 解析照片插入归属人：18 位合法 + 三类业务档案中确实存在（必须指定已存在的档案人员）。
    /// 复用 <see cref="PersonCte"/>（与补传候选同一段 SQL），按身份证精确定位到一个人。
    /// </summary>
    public async Task<Result<PhotoOwner>> ResolveOwnerAsync(string idCard, CancellationToken ct = default)
    {
        var trimmed = idCard?.Trim() ?? string.Empty;
        if (!IdCardValidator.IsValid(trimmed))
            return Result.Failure<PhotoOwner>(ErrorCodes.VALIDATION_FAILED, "身份证号格式不正确（需 18 位）");

        // $1 业务域过滤（空 = 全部）、$2 关键词（空 = 不过滤），身份证精确定位走 $3
        const string sql = PersonCte + @"
SELECT id_card, person_name, business_types, business_id, photo_count
FROM counted
WHERE id_card = $3
LIMIT 1";

        var result = await _db.QueryAsync<PhotoBackfillCandidate>(sql, ct, string.Empty, string.Empty, trimmed);
        if (result.IsFailure)
        {
            LogError($"档案人员解析失败: {result.Message}");
            return Result.Failure<PhotoOwner>(result.ErrorCode!, result.Message!);
        }

        var hit = result.Value?.FirstOrDefault();
        if (hit == null)
        {
            return Result.Failure<PhotoOwner>(ErrorCodes.NOT_FOUND,
                "该身份证不在低收入/高龄/临时救助档案中（插入照片必须指定已存在的档案人员）");
        }

        return Result.Success(new PhotoOwner
        {
            IdCard = hit.IdCard,
            PersonName = hit.PersonName,
            BusinessTypes = hit.BusinessTypes,
            ApplicationId = hit.BusinessId > 0 ? hit.BusinessId : null,
            PhotoCount = hit.PhotoCount
        });
    }

    /// <summary>
    /// 批量插入照片（PC 端查询页按钮 / 拖拽）：逐张走 <see cref="SavePhotoAsync"/>，
    /// 压缩、命名、体积校验与手机端采集完全同源；编号从该人该类别下一序号开始，本批内递增。
    /// 部分失败不回滚已成功张，成败与首个原因在结果里显式带出（不吞异常、不静默跳过）。
    /// </summary>
    public async Task<Result<BatchPhotoInsertResult>> InsertPhotosAsync(
        PhotoOwner owner, string category, IReadOnlyList<byte[]> files,
        PhotoWatermarkInfo? watermark = null, CancellationToken ct = default)
    {
        if (owner is null || string.IsNullOrWhiteSpace(owner.IdCard))
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.VALIDATION_FAILED, "缺少照片归属人（必须指定已存在的档案人员）");
        if (files is null || files.Count == 0)
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.VALIDATION_FAILED, "待插入的照片为空");
        if (files.All(f => f == null || f.Length == 0))
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.VALIDATION_FAILED, "照片内容为空");

        var idCard = owner.IdCard.Trim();
        var cat = string.IsNullOrWhiteSpace(category) ? PhotoCategoryConstants.OTHER_CERT : category;

        // 水印上下文整批只补齐一次（姓名/地址/村屯坐标/档案时间不进逐张循环，避免 N+1）
        if (watermark != null)
            watermark = await FillWatermarkContextAsync(watermark, idCard, cat, ct);

        // 编号基准：该人该类别现有照片（与手机端采集同一取数与推进规则，往下递增不覆盖）
        var existingResult = await GetByPersonAsync(idCard, cat, ct);
        if (existingResult.IsFailure)
            return Result.Failure<BatchPhotoInsertResult>(existingResult.ErrorCode!, existingResult.Message!);

        var nextSeq = PhotoNameBuilder.NextSequence(existingResult.Value ?? new List<ArchivePhoto>());

        var ids = new List<long>(files.Count);
        var succeeded = 0;
        string? firstError = null;

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var raw = files[i];
            if (raw == null || raw.Length == 0)
            {
                firstError ??= "存在内容为空的图片文件";
                continue;
            }

            try
            {
                var photo = new ArchivePhoto
                {
                    PhotoData = raw,
                    ApplicantIdCard = idCard,
                    ApplicationId = owner.ApplicationId,
                    PhotoCategory = cat,
                    PhotoName = PhotoNameBuilder.Build(owner.PersonName, idCard, cat, nextSeq),
                    CreatedBy = App.CurrentUserId,
                    CreatedByName = App.CurrentUserName
                };

                var save = await SavePhotoAsync(photo, watermark, ct);
                if (save.IsFailure)
                {
                    firstError ??= save.Message;
                    LogWarn($"插入照片失败: {save.ErrorCode} {save.Message}");
                    continue;
                }

                ids.Add(save.Value);
                succeeded++;
                nextSeq++;
                LogInfo($"插入照片成功: Id={save.Value}, Category={cat}, Name={photo.PhotoName}, " +
                        $"Original={raw.Length}B, Stored={photo.FileSize}B");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                firstError ??= ex.Message;
                LogWarn($"插入照片异常: {ex.Message}");
            }
        }

        return Result.Success(new BatchPhotoInsertResult
        {
            Total = files.Count,
            Succeeded = succeeded,
            FirstError = firstError,
            PhotoIds = ids
        });
    }
}
