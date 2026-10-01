using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// 在线更新流程协调器：检查 → 提示（强制/普通/跳过）→ 下载（进度弹窗）→ 安装。
/// Windows 与 Android 共用（安装步骤由平台 <c>IAppPackageInstaller</c> 处理）。
/// </summary>
public interface IAppUpdateCoordinator
{
    /// <summary>
    /// 检查更新并引导用户完成升级（manual=true 时强制给出反馈）。
    /// 返回 true 表示已成功下载并启动安装（Windows 调用方随后应退出应用）。
    /// </summary>
    Task<bool> CheckAndPromptAsync(bool manual, CancellationToken ct = default);
}
