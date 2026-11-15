using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 照片推送打印三端共用实现（补传页 / 采集页 / 查询页，2026-11-15 收敛三份重复代码）：
/// 权限门控 + 逐张取图 → 组 <see cref="PrintJobRequest"/> → 入队。
/// 版式选项与「按默认」解析见 <see cref="PhotoLayoutConstants"/>（同一单点）；禁止在 ViewModel 里再抄一遍。
/// </summary>
public static class PhotoPrintPushHelper
{
    /// <summary>单张待推送照片（附带打印抬头所需的业务与人员上下文）。</summary>
    /// <param name="Photo">照片实体（取字节 Id、类别、PhotoName）。</param>
    /// <param name="BusinessId">业务/档案 ID（写入 print_jobs.business_id 追溯）。</param>
    /// <param name="ApplicantName">纸面抬头姓名（查询页从 PhotoName 反解析；补传/采集页用已知人名）。</param>
    /// <param name="ApplicantIdCard">身份证号（纸面抬头）。</param>
    /// <param name="FailureLabel">失败消息前缀；null = 用 PhotoName（空则「照片 #Id」）。</param>
    public sealed record PushItem(
        ArchivePhoto Photo,
        long? BusinessId,
        string ApplicantName,
        string ApplicantIdCard,
        string? FailureLabel = null);

    /// <summary>推送结果：入队成功张数 + 打印任务号（日志留痕）+ 逐项失败原因（调用方负责汇总提示与日志）。</summary>
    public sealed record PushResult(int OkCount, IReadOnlyList<string> JobNos, IReadOnlyList<string> Failures);

    /// <summary>
    /// 打印权限门控：复用「家庭收入档案打印」权限码（与文书打印同源，不新增权限种子）。
    /// 返回 false = 无权限且已弹提示（调用方直接 return）；无登录用户时放行（与既有口径一致）。
    /// </summary>
    public static async Task<bool> EnsurePrintPermissionAsync(
        INewPermissionService permissionService,
        IDialogService dialogService,
        CancellationToken ct)
    {
        var userId = App.CurrentUserId;
        if (!userId.HasValue) return true;

        var allowed = await permissionService.HasPermissionAsync(
            userId.Value, PermissionCodes.PRINT_FAMILYAPPLICATION, ct);
        if (!allowed)
        {
            await dialogService.DisplayAlertAsync(
                "权限不足", "您没有打印权限（PRINT_FAMILYAPPLICATION），请联系管理员", "确定");
        }
        return allowed;
    }

    /// <summary>
    /// 逐张取图 → 组任务 → 入队。<paramref name="forcedLayout"/> = null 时按默认
    /// （逐张按类别：身份证原寸、其余整页）。任何单张失败不中断其余，逐项收集原因。
    /// </summary>
    public static async Task<PushResult> PushAsync(
        IArchivePhotoService photoService,
        IPrintQueueService queueService,
        IReadOnlyList<PushItem> items,
        string? forcedLayout,
        CancellationToken ct)
    {
        var okCount = 0;
        var jobNos = new List<string>();
        var failures = new List<string>();

        foreach (var item in items)
        {
            var label = item.FailureLabel
                ?? (string.IsNullOrWhiteSpace(item.Photo.PhotoName)
                    ? $"照片 #{item.Photo.Id}"
                    : item.Photo.PhotoName!);
            try
            {
                var data = await photoService.GetPhotoDataAsync(item.Photo.Id, ct);
                if (data.IsFailure || data.Value == null || data.Value.Length == 0)
                {
                    failures.Add($"{label}：读取照片失败");
                    continue;
                }

                var request = new PrintJobRequest
                {
                    BusinessType = PrintBusinessTypes.ArchivePhoto,
                    BusinessId = item.BusinessId,
                    Classification = item.Photo.PhotoCategoryName,
                    ApplicantName = item.ApplicantName,
                    ApplicantIdCard = item.ApplicantIdCard,
                    Copies = 1,
                    IsDuplex = false,
                    JobKind = PrintJobConstants.JobKindPhoto,
                    PhotoData = data.Value,
                    PhotoLayout = forcedLayout ?? PhotoLayoutConstants.DefaultForCategory(item.Photo.PhotoCategory),
                    PhotoCategory = item.Photo.PhotoCategory,
                    RequestedBy = App.CurrentUserId,
                    RequestedByName = App.CurrentUserName
                };

                var enqueue = await queueService.EnqueueAsync(request, ct);
                if (enqueue.IsSuccess)
                {
                    okCount++;
                    jobNos.Add(enqueue.Value?.JobNo ?? string.Empty);
                }
                else failures.Add($"{label}：{enqueue.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"{label}：{ex.Message}");
            }
        }

        return new PushResult(okCount, jobNos, failures);
    }
}
