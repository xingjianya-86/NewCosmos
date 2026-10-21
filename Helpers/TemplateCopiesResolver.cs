using NewCosmos.Services.Templates;

namespace NewCosmos.Helpers;

/// <summary>
/// 模板打印份数解析入口（JSON → 生效份数），口径唯一实现在 <see cref="TemplateConfig.ResolveCopies"/>：
///     分类映射 copiesByClassification[classification]（分类级，最高优先） → copies（模板级默认） → 1。
/// 档案输出页（ArchiveOutputViewModel）与手机推送打印（PrintJobFactory）必须经此解析，
/// 禁止各自另写份数规则（曾两套口径）。
/// 非法/损坏 config_json 一律回退默认值 1，不向外抛异常——与 ArchiveOutputViewModel.LoadTemplateConfigAsync
/// 既有兜底口径一致（损坏配置只影响份数，不得阻断打印与模板管理页选择）。
/// </summary>
public static class TemplateCopiesResolver
{
    /// <summary>按模板 config_json 与申请分类解析生效份数（空/非法配置 → 1）。</summary>
    public static int Resolve(string? configJson, string? classification) =>
        Parse(configJson).ResolveCopies(classification);

    /// <summary>读模板级默认份数（模板管理页编辑框用；未配置 → 1，越界钳制 1..99）。</summary>
    public static int ResolveDefault(string? configJson)
    {
        var copies = Parse(configJson).Copies;
        return copies > 0 ? Math.Clamp(copies, 1, 99) : 1;
    }

    /// <summary>读分类级份数映射（模板管理页"分类级优先"提示用；未配置/损坏 → 空表，不抛异常）。</summary>
    public static IReadOnlyDictionary<string, int> ClassificationCopies(string? configJson) =>
        Parse(configJson).CopiesByClassification ?? new Dictionary<string, int>();

    private static TemplateConfig Parse(string? configJson)
    {
        try
        {
            return TemplateConfig.FromJson(configJson ?? string.Empty);
        }
        catch
        {
            return new TemplateConfig();
        }
    }
}
