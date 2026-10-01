using NewCosmos.Models.Results;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Windows;

/// <summary>
/// Windows 端身份证读取桩：端侧 OCR 为移动端数据录入能力，电脑端不提供。
/// </summary>
public sealed class WindowsIdentityReader : IIdentityReader
{
    public bool IsSupported => false;

    public Task<Result<IdCardInfo>> ReadAsync(Stream image, CancellationToken ct = default)
        => Task.FromResult(Result<IdCardInfo>.Failure("OCR_NOT_SUPPORTED", "电脑端不支持身份证扫描，请在手机端使用"));
}
