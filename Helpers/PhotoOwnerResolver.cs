namespace NewCosmos.Helpers;

/// <summary>
/// 档案照片插入归属人解析（纯逻辑，便于单测）。
/// PC 端「插入照片 / 拖拽插入 / 打开采集页」必须锁定**已存在被查询的人**，解析顺序：
/// ① 查询关键词本身是合法 18 位身份证 → 用它（用户显式指定）；
/// ② 否则若当前查询结果全部同属一人且身份证合法 → 用该人（"已经被查询到的人"）；
/// ③ 否则解析不出 → 返回 null，由调用方提示"请先指定档案人员"。
/// 本方法只做身份证定位；"是否真在低收入/高龄/临时救助档案中存在"
/// 由 <c>IArchivePhotoService.ResolveOwnerAsync</c> 查库判定。
/// </summary>
public static class PhotoOwnerResolver
{
    /// <summary>
    /// 从查询关键词 + 当前列表解析归属身份证；解析不出返回 null。
    /// </summary>
    /// <param name="keyword">查询框关键词（身份证或照片名模糊词）。</param>
    /// <param name="listedIdCards">当前查询结果各行的身份证（用于"结果同属一人"判定）。</param>
    public static string? Resolve(string? keyword, IEnumerable<string?>? listedIdCards)
    {
        // ① 关键词即合法身份证
        var kw = keyword?.Trim();
        if (!string.IsNullOrEmpty(kw) && IdCardValidator.IsValid(kw))
            return kw;

        // ② 查询结果同属一人
        if (listedIdCards == null) return null;

        string? single = null;
        foreach (var raw in listedIdCards)
        {
            var id = raw?.Trim();
            if (string.IsNullOrEmpty(id)) continue;

            if (single == null)
            {
                single = id;
                continue;
            }

            if (!string.Equals(single, id, StringComparison.Ordinal))
                return null; // 多人 → 归属不唯一
        }

        if (string.IsNullOrEmpty(single)) return null;
        return IdCardValidator.IsValid(single!) ? single : null;
    }
}
