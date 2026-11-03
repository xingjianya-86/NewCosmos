using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 识别结果分配规划（纯逻辑，无 UI/IO 依赖）：
/// 按名单预匹配回填目标（证号/姓名命中 → 该成员行；未命中 → 新增赡养人），
/// 并检测多条记录抢占同一目标的冲突。
/// Key 编码见 <see cref="TargetKeys"/>。名单行须包含户主行（Key="head"）。
/// </summary>
public static class OcrAssignmentPlanner
{
    /// <summary>目标 Key 编码（纯客户端路由约定，服务端无感知）</summary>
    public static class TargetKeys
    {
        public const string Head = "head";
        public const string Ignore = "ignore";

        /// <summary>既有成员行：member:{行标识}（表单自定义，如 AssetCheckItemDto 索引或 FamilyMember.Id）</summary>
        public const string MemberPrefix = "member:";

        /// <summary>新增赡养人/授权人行</summary>
        public const string NewSupporter = "new_supporter";

        /// <summary>表单级字段目标</summary>
        public const string FormBank = "form:bank";
        public const string FormHukou = "form:hukou";
        public const string FormReason = "form:reason";
        public const string FormPhone = "form:phone";

        public static string Member(string id) => MemberPrefix + id;
        public static bool IsMember(string key) => key.StartsWith(MemberPrefix, StringComparison.Ordinal);
    }

    /// <summary>名单行（来源表单提供：目标行标识 + 姓名 + 证号；户主行 Key 用 TargetKeys.Head）</summary>
    public sealed record MemberRow(string Key, string Name, string IdCard);

    /// <summary>
    /// 为一条识别记录预匹配目标。
    /// 规则：① 证号精确命中（忽略大小写）→ 该行；② 姓名精确命中 → 该行（多命中取首个）；
    /// ③ 人物类记录（含 name/id_card 字段）均未命中 → 新增赡养人；
    /// ④ 表单类记录（户口本/银行卡等）返回 null，由调用方给固定目标。
    /// </summary>
    public static string? SuggestTarget(
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<MemberRow> members)
    {
        if (fields.Count == 0) return null;

        var idCard = GetTrimmed(fields, OcrFieldKeys.IdCard);
        var name = GetTrimmed(fields, OcrFieldKeys.Name);

        // ① 证号命中优先（OCR 姓名可能误识，证号带校验更可靠）
        if (idCard.Length > 0)
        {
            foreach (var m in members)
            {
                if (string.Equals(m.IdCard?.Trim(), idCard, StringComparison.OrdinalIgnoreCase))
                    return m.Key;
            }
        }

        // ② 姓名命中
        if (name.Length > 0)
        {
            foreach (var m in members)
            {
                if (string.Equals(m.Name?.Trim(), name, StringComparison.Ordinal))
                    return m.Key;
            }
        }

        // ③ 人物类记录默认新增赡养人（用户决策：名单外 → 子女赡养人）
        var isPersonRecord = fields.ContainsKey(OcrFieldKeys.IdCard) || fields.ContainsKey(OcrFieldKeys.Name);
        return isPersonRecord ? TargetKeys.NewSupporter : null;
    }

    /// <summary>
    /// 冲突检测：同一目标被多条记录占用时返回冲突的目标 Key 列表（去重，按首次出现顺序）。
    /// ignore 与 null（未选）不参与冲突。
    /// </summary>
    public static IReadOnlyList<string> FindConflicts(IEnumerable<string?> selectedKeys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var key in selectedKeys)
        {
            if (string.IsNullOrEmpty(key) || key == TargetKeys.Ignore) continue;
            if (!seen.Add(key) && conflicts.Add(key))
                order.Add(key);
        }
        return order;
    }

    /// <summary>字段值是否空白（回填"只填空字段"判定）</summary>
    public static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>取字段值并去首尾空白</summary>
    public static string GetTrimmed(IReadOnlyDictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var v) ? (v ?? string.Empty).Trim() : string.Empty;
}
