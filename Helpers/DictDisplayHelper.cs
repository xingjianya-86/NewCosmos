using System.Collections.Concurrent;
using NewCosmos.Constants;
using NewCosmos.Services.Core;

namespace NewCosmos.Helpers;

public static class DictDisplayHelper
{
    private static ConcurrentDictionary<string, string> _assetExemptions = new();
    private static ConcurrentDictionary<string, string> _assetTypes = new();
    private static ConcurrentDictionary<string, string> _incomeTypes = new();
    private static ConcurrentDictionary<string, string> _rigidExpenditureTypes = new();
    private static ConcurrentDictionary<string, string> _assistanceCategories = new();
    private static ConcurrentDictionary<string, string> _assistanceTypes = new();
    private static ConcurrentDictionary<string, string> _familyRelationships = new();
    private static ConcurrentDictionary<string, string> _disabilityTypes = new();
    private static ConcurrentDictionary<string, string> _disabilityLevels = new();

    public static void UpdateFromCache(IDictCacheService cache)
    {
        _assetExemptions = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory("AssetExemptionTypes"));
        _assetTypes = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory("AssetTypes"));
        _incomeTypes = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory("IncomeTypes"));
        _rigidExpenditureTypes = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory("RigidExpenditureTypes"));
        _assistanceCategories = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory("AssistanceCategories"));
        _assistanceTypes = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory("AssistanceTypes"));
        _familyRelationships = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory(DictionaryTypeCodes.FamilyRelationships));
        _disabilityTypes = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory(DictionaryTypeCodes.DisabilityTypes));
        _disabilityLevels = new ConcurrentDictionary<string, string>(
            cache.GetAllByCategory(DictionaryTypeCodes.DisabilityLevels));
    }

    public static string GetAssetExemptionDisplay(string code) =>
        _assetExemptions.TryGetValue(code, out var v) ? v : code;

    public static string GetAssetTypeDisplay(string code) =>
        _assetTypes.TryGetValue(code, out var v) ? v : code;

    public static string GetIncomeTypeDisplay(string code) =>
        _incomeTypes.TryGetValue(code, out var v) ? v : code;

    public static string GetRigidExpenditureDisplay(string code) =>
        _rigidExpenditureTypes.TryGetValue(code, out var v) ? v : code;

    public static string GetAssistanceCategoryDisplay(string code) =>
        _assistanceCategories.TryGetValue(code, out var v) ? v : code;

    public static string GetAssistanceTypeDisplay(string code) =>
        _assistanceTypes.TryGetValue(code, out var v) ? v : code;

    /// <summary>
    /// 家庭关系代码（Head/Spouse/Son…）→ 中文显示。
    /// 空值返回 "-"；已中文或字典未命中则原样返回（兼容历史已中文化的记录）。
    /// </summary>
    public static string GetFamilyRelationshipDisplay(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "-";
        return _familyRelationships.TryGetValue(code, out var v) ? v : code;
    }

    /// <summary>残疾类型 key（Intellectual…）→ 中文显示；未命中原样返回</summary>
    public static string GetDisabilityTypeDisplay(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : (_disabilityTypes.TryGetValue(code, out var v) ? v : code);

    /// <summary>残疾等级 key（Level3…）→ 中文显示；未命中原样返回</summary>
    public static string GetDisabilityLevelDisplay(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : (_disabilityLevels.TryGetValue(code, out var v) ? v : code);
}
