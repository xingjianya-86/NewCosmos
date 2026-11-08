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
        public const string FormAddress = "form:address";
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

    /// <summary>区划匹配结果：命中的候选名、在文本中的起始下标、实际占用的文本长度。</summary>
    public readonly record struct RegionMatch(string Name, int Index, int Length);

    /// <summary>
    /// 在一组候选区划名里匹配地址片段：取「出现位置最靠前，同位置最长」的候选。
    /// 用于户口本 OCR 地址文本 → 户籍级联（市/县/镇/村）逐级解析；
    /// 位置靠前保证按"市→县→镇→村"的书写顺序取词，最长匹配防止「林口县」被「林口」类短名截断。
    /// 村/社区级做**后缀归一化**：区划库存「万寿村委会」，户口本写「万寿村」，精确匹配会漏，
    /// 因此额外用去后缀的变体（万寿村/万寿）匹配，并返回实际占用的文本长度供逐级截断。
    /// 精确命中优先于变体命中，降低变体误匹配风险。
    /// </summary>
    public static RegionMatch? MatchRegionOption(IReadOnlyList<string> options, string text)
    {
        var exact = FindBestMatches(options, text, includeVariants: false);
        if (exact.Count > 0) return exact[0];

        var variant = FindBestMatches(options, text, includeVariants: true);
        return variant.Count > 0 ? variant[0] : null;
    }

    /// <summary>
    /// 村级匹配（比 <see cref="MatchRegionOption"/> 更严格）：**歧义即视为不匹配**。
    /// 旧村名/自然屯常只剩词干（如库内「柞木村委会」、户口本写「柞木屯」），只能靠变体命中；
    /// 若同一位置上多个村共用同一词干（「石青村委会」「石青沟村委会」都含「石青」），
    /// 猜错会把区划 ID 写成别的村，因此这里返回 null，由上层整体放弃回填、保留表单原值。
    /// 顺序：全称精确 > 后缀归一/词干变体；任一级出现多个不同候选 → null。
    /// </summary>
    public static RegionMatch? MatchVillageOption(IReadOnlyList<string> options, string text)
    {
        var exact = FindBestMatches(options, text, includeVariants: false);
        if (exact.Count == 1) return exact[0];
        if (exact.Count > 1) return null;   // 重名村：不猜

        var variant = FindBestMatches(options, text, includeVariants: true);
        return variant.Count == 1 ? variant[0] : null;   // 0=没有；>1=词干歧义，不猜
    }

    /// <summary>
    /// 找出「出现位置最靠前、同位置最长」的全部候选（不同名字去重，同名只留一条）。
    /// 返回多条 = 该位置上存在歧义。精确与变体分两轮调用，保证精确命中永远优先。
    /// </summary>
    private static List<RegionMatch> FindBestMatches(
        IReadOnlyList<string> options, string text, bool includeVariants)
    {
        var matches = new List<RegionMatch>();
        if (string.IsNullOrEmpty(text) || options.Count == 0) return matches;

        var bestIndex = int.MaxValue;
        var bestLength = 0;

        foreach (var option in options)
        {
            if (string.IsNullOrEmpty(option)) continue;

            if (!includeVariants)
            {
                Consider(option, option, option.Length);
                continue;
            }

            foreach (var key in BuildVariantKeys(option))
                Consider(option, key, key.Length);
        }

        return matches;

        void Consider(string name, string key, int length)
        {
            var index = text.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) return;

            if (index < bestIndex || (index == bestIndex && length > bestLength))
            {
                bestIndex = index;
                bestLength = length;
                matches.Clear();
            }

            if (index != bestIndex || length != bestLength) return;
            if (!matches.Any(m => string.Equals(m.Name, name, StringComparison.Ordinal)))
                matches.Add(new RegionMatch(name, index, length));
        }
    }

    /// <summary>
    /// 生成区划名的等价变体（不含原名本身）：从区划库全称里剥出户口本常见写法。
    /// 「万寿村委会」「万寿村民委员会」→「万寿村」「万寿」；
    /// 「XX居民委员会」「XX居委会」→「XX居委会」「XX」；「XX社区」→「XX」；「XX村」→「XX」。
    /// 镇/县/市级不做剥离（户口本写法与库内一致，避免「林口」误配「林口县」后截断失配）。
    /// </summary>
    private static IEnumerable<string> BuildVariantKeys(string option)
    {
        var stem = option;

        if (option.EndsWith("村民委员会", StringComparison.Ordinal))
            stem = option[..^"村民委员会".Length];
        else if (option.EndsWith("村委会", StringComparison.Ordinal))
            stem = option[..^"村委会".Length];
        else if (option.EndsWith("居民委员会", StringComparison.Ordinal))
            stem = option[..^"居民委员会".Length];
        else if (option.EndsWith("居委会", StringComparison.Ordinal))
            stem = option[..^"居委会".Length];
        else if (option.EndsWith("社区", StringComparison.Ordinal))
            stem = option[..^"社区".Length];
        else if (option.EndsWith("村", StringComparison.Ordinal))
            stem = option[..^"村".Length];
        else
            yield break; // 非村级（市/县/镇）：只用全称精确匹配

        if (stem.Length == 0) yield break;

        // 「万寿村委会」→ 先试「万寿村」（最接近户口本写法），再试「万寿」；
        // 同时兼容户口本全称写法「万寿村民委员会 / 万寿居民委员会」，避免只吃掉前缀、
        // 把「民委员会99号」当成门牌号写进详细地址。
        if (!string.Equals(stem + "村", option, StringComparison.Ordinal))
            yield return stem + "村";

        if (option.EndsWith("村民委员会", StringComparison.Ordinal)
            || option.EndsWith("村委会", StringComparison.Ordinal))
            yield return stem + "村民委员会";

        if (option.EndsWith("居民委员会", StringComparison.Ordinal)
            || option.EndsWith("居委会", StringComparison.Ordinal))
            yield return stem + "居民委员会";

        if (option.EndsWith("居民委员会", StringComparison.Ordinal)
            || option.EndsWith("居委会", StringComparison.Ordinal))
            yield return stem + "居委会";

        yield return stem;
    }
}
