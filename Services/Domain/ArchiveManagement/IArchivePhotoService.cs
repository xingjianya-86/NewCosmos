using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 档案照片服务：手机端采集的证件/核查照片的存取。
/// 照片按人（applicant_id_card）+ 申请（application_id）+ 类别（photo_category）归档，存库 bytea。
/// 新采集统一由保存入口压缩为 WebP（单张 ≤500KB）；旧 JPEG/PNG 通过 image_format 兼容。
/// </summary>
public interface IArchivePhotoService
{
    /// <summary>保存一张照片，返回新记录 ID。
    /// <paramref name="watermark"/>：水印上下文——仅「入户排查 / 公示核查」类别在压缩前烧进图片，
    /// null = 不打水印（其余类别与历史入口行为不变）。</summary>
    Task<Result<long>> SavePhotoAsync(ArchivePhoto photo, PhotoWatermarkInfo? watermark = null, CancellationToken ct = default);

    /// <summary>列出指定人的照片元数据（不含 photo_data 二进制，列表用；按类别可选过滤）。</summary>
    Task<Result<List<ArchivePhoto>>> GetByPersonAsync(
        string applicantIdCard, string? category = null, CancellationToken ct = default);

    /// <summary>取回单张照片的二进制（预览/打印用）。</summary>
    Task<Result<byte[]>> GetPhotoDataAsync(long photoId, CancellationToken ct = default);

    /// <summary>删除一张照片（采集人误删/重拍场景）。</summary>
    Task<Result<bool>> DeletePhotoAsync(long photoId, CancellationToken ct = default);

    /// <summary>
    /// 照片补传候选（人员维度）：按姓名/身份证检索用户可补传的人员，附带所属业务域与已有照片数。
    /// businessType 空 = 全部域；同一身份证跨域聚合为一行；不做"未传"过滤（已传也可继续补传）。
    /// </summary>
    Task<Result<PagedResult<PhotoBackfillCandidate>>> GetBackfillCandidatesAsync(
        string? businessType, string? keyword,
        int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 照片查询（桌面端/导出用）：关键词匹配身份证或照片备注，类别可过滤，分页返回元数据（不含二进制）。
    /// </summary>
    Task<Result<PagedResult<ArchivePhoto>>> GetPhotosPagedAsync(
        string? keyword, string? category, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 解析照片插入归属人：身份证须 18 位合法，且在低收入/高龄/临时救助/资产核查任一业务域存在——
    /// PC 端「插入照片 / 拖拽插入 / 打开采集页」的前置校验（必须指定已存在的档案人员）。
    /// 查不到返回 NOT_FOUND，不静默放行。
    /// </summary>
    Task<Result<PhotoOwner>> ResolveOwnerAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 批量插入照片（PC 端）：与手机端采集走**同一个**保存入口
    /// （压缩 WebP 单张 ≤500KB、命名 姓名_后4位_类别_编号、编号往下递增不覆盖），
    /// 返回逐张成败统计；部分失败不回滚已成功张（与手机端批量采集口径一致）。
    /// <paramref name="watermark"/>：水印上下文（同 SavePhotoAsync，整批同一上下文、拍摄时间一致）。
    /// </summary>
    Task<Result<BatchPhotoInsertResult>> InsertPhotosAsync(
        PhotoOwner owner, string category, IReadOnlyList<byte[]> files,
        PhotoWatermarkInfo? watermark = null, CancellationToken ct = default);

    /// <summary>
    /// 户主档案地址（水印用）：三业务域按优先级 低收入 → 高龄 → 临时救助 取首个有地址的档案，
    /// 拼「林口县柳树镇XXX村村民委员会」（拼装规则单点在 AddressResolver.BuildWatermarkAddress；
    /// 低收入/高龄按区划代号 JOIN 区划表转名称，临时救助回退名称文本）。
    /// 查不到返回 Success("")（水印省略地址行，不报错）；地址只取户主档案，不兜底当前用户单位。
    /// </summary>
    Task<Result<string>> GetWatermarkAddressAsync(string idCard, CancellationToken ct = default);

    /// <summary>
    /// 水印上下文按批补齐（2026-11-15 二改，一次到位后逐张保存不再查库）：
    /// 姓名 ← 户主档案聚合；地址 ← 档案区划；经纬度 ← **所在村屯查询信息**（区划坐标列，
    /// 缺失在线地理编码回写，不取设备 GPS）；时间 ← **档案时间轴时间**（入户调查日/公示起日）
    /// + 记录创建时分，全取不到回退入参的采集时刻。返回值 IsResolved=true。
    /// </summary>
    Task<PhotoWatermarkInfo> FillWatermarkContextAsync(
        PhotoWatermarkInfo watermark, string idCard, string category, CancellationToken ct = default);
}
