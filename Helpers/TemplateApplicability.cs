using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 模板适用性门槛单点（方案B，2026-11-18）：按「文档类型 doc_type + 数据事实」判定模板是否出现在输出清单。
///
/// 取代原 ArchiveOutputViewModel 内 5 处 Name.Contains 文件名门槛（无土地证明/村级土地说明/
/// 一事一议/近亲属），文件名模糊匹配已全库废除——新增同类模板只须上传时选对 doc_type，零代码生效。
///
/// 不在本类管的维度（勿混）：
///   ① 项目类型（categories 进哪个清单）→ ArchiveCategoryResolver.GetByCategoriesAsync；
///   ② 变动文书事实门 → ChangeDocumentApplicabilityService.IsTemplateApplicable（按 categories 判，本就类型化）；
///   ③ 入户调查补挂（文书模式按分类补列）→ ArchiveOutputViewModel 内 DocType==Survey 过滤。
/// </summary>
public static class TemplateApplicability
{
    /// <summary>
    /// 该文档类型在此数据快照下是否适用。事实来源=PrintNavigationData.FieldData 装配产物。
    /// 未列入门槛的类型恒适用（含 doc_type 为空的历史行：宁可多列不可漏打）。
    /// </summary>
    /// <param name="docType">模板文档类型（nc_biz_templates.doc_type）</param>
    /// <param name="fieldData">打印字段快照（可空）</param>
    public static bool IsApplicable(string? docType, Dictionary<string, string>? fieldData)
    {
        if (string.IsNullOrEmpty(docType)) return true;

        // 土地二选一：适用性规则相反，必须按类型分开判
        if (docType == TemplateDocTypes.NoLandProof) return !HasFamilyLand(fieldData);
        if (docType == TemplateDocTypes.VillageLandNote) return HasFamilyLand(fieldData);

        // 一事一议三表（申报表/申请表/备案表）：同一事实门——仅一事一议申请出
        if (Array.IndexOf(TemplateDocTypes.SpecialApprovalForms, docType) >= 0)
            return HasSpecialApprovalData(fieldData);

        // 近亲属两表：同一事实门——仅存在关联备案才出
        if (docType == TemplateDocTypes.NearRelative)
            return HasNearRelativeData(fieldData);

        return true;
    }

    /// <summary>家庭是否有土地（FAMILY_LAND_AREA 非 0/非空即有土地）。</summary>
    private static bool HasFamilyLand(Dictionary<string, string>? fieldData)
    {
        if (fieldData == null)
        {
            // 快照装配失败与"确实无土地"不可区分；土地模板会因此漏列，必须留痕。
            Serilog.Log.Warning("[TemplateApplicability] 打印字段快照为空，土地类模板按不适用处理");
            return false;
        }
        if (!fieldData.TryGetValue(FieldKeys.FAMILY_LAND_AREA, out var area))
            return false;
        if (string.IsNullOrWhiteSpace(area)) return false;
        return area != "0" && decimal.TryParse(area, out var value) && value > 0;
    }

    /// <summary>是否一事一议申请（申报表数据非空 = IsSpecialApproval 档案）。</summary>
    private static bool HasSpecialApprovalData(Dictionary<string, string>? fieldData)
        => fieldData != null
           && fieldData.TryGetValue(FieldKeys.SPECIAL_APPROVAL_MATTERS, out var matters)
           && !string.IsNullOrWhiteSpace(matters);

    /// <summary>是否存在近亲属关联备案（NEAR_RELATIVE_HAS_DATA 装配标志 = "1"）。</summary>
    private static bool HasNearRelativeData(Dictionary<string, string>? fieldData)
        => fieldData != null
           && fieldData.TryGetValue(FieldKeys.NEAR_RELATIVE_HAS_DATA, out var flag)
           && flag == "1";
}
