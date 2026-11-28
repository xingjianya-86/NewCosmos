using NewCosmos.Models.Entities;
using NewCosmos.Services.Templates;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 推送打印守卫：签名占位判定（方案A）。
/// Windows Agent（NewCosmos.Agent）跳过 SIGNATURE_* 占位不内嵌签名图，含签名模板推过去会丢签名，
/// 故含签名占位一律拒绝定向推送、回退本地打印（PrintExecuteService 会内嵌 nc_biz_signatures 签名图）。
/// </summary>
public static class PrintPushGuard
{
    /// <summary>
    /// 模板 config 的字段映射是否含 SIGNATURE_* 前缀 FieldKey（签名内嵌占位的权威来源）。
    /// 模板缺失 / 配置损坏 → false（不因解析失败把推送一并堵死，本地打印路径同样会报错）。
    /// </summary>
    public static bool HasSignaturePlaceholder(Template? template)
    {
        if (template == null || string.IsNullOrWhiteSpace(template.ConfigJson)) return false;

        TemplateConfig config;
        try
        {
            config = TemplateConfig.FromJson(template.ConfigJson);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[PrintPushGuard] 模板 config 解析失败，按无签名处理: {Name}", template.Name);
            return false;
        }

        return config.Fields.Any(f =>
            f != null
            && !string.IsNullOrEmpty(f.FieldKey)
            && f.FieldKey.StartsWith("SIGNATURE_", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>入队请求 fields 字典是否含 SIGNATURE_* 前缀键（防御：个别入口把签名值放进 fields）。</summary>
    public static bool HasSignatureField(PrintJobRequest? request)
        => request?.Fields != null
           && request.Fields.Keys.Any(k => k.StartsWith("SIGNATURE_", StringComparison.OrdinalIgnoreCase));
}
