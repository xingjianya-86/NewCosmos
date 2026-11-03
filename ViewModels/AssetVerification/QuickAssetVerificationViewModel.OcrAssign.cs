using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.ViewModels.AssetVerification;

/// <summary>
/// 快速资产核查表单的识别结果回填上下文（partial 扩展）——多身份证分配的主场景：
/// 证号/姓名命中名单 → 户主行或对应成员行；名单外 → 新增一行授权人（默认关系"儿子"，可改）。
/// 回填铁律：只填空字段（防误覆盖）。
/// </summary>
public partial class QuickAssetVerificationViewModel : IOcrAssignContext
{
    /// <summary>新增行的默认关系（用户决策：名单外默认子女赡养人）</summary>
    private const string DefaultNewRowRelationship = "儿子";

    IReadOnlyList<OcrAssignmentTarget> IOcrAssignContext.GetTargets(string categoryCode)
    {
        var targets = new List<OcrAssignmentTarget>();
        if (categoryCode is not (PhotoCategoryConstants.ID_CARD or PhotoCategoryConstants.DISABILITY_CERT))
            return targets; // 资产核查只关心人物类；其他类别不给目标（分配页仅"不使用"）

        var head = Applicants.FirstOrDefault(a => a.IsHead);
        if (head is not null)
            targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.Head, "户主"));

        for (var i = 0; i < Applicants.Count; i++)
        {
            var a = Applicants[i];
            if (a.IsHead) continue;
            var label = string.IsNullOrWhiteSpace(a.ApplicantName) ? $"成员 {i + 1}" : $"成员「{a.ApplicantName}」";
            targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.Member(i.ToString()), label));
        }

        targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.NewSupporter, "新增子女赡养人"));
        return targets;
    }

    IReadOnlyList<OcrAssignmentPlanner.MemberRow> IOcrAssignContext.GetMemberRows()
    {
        var rows = new List<OcrAssignmentPlanner.MemberRow>();
        for (var i = 0; i < Applicants.Count; i++)
        {
            var a = Applicants[i];
            var key = a.IsHead
                ? OcrAssignmentPlanner.TargetKeys.Head
                : OcrAssignmentPlanner.TargetKeys.Member(i.ToString());
            rows.Add(new OcrAssignmentPlanner.MemberRow(key, a.ApplicantName ?? string.Empty, a.ApplicantIdCard ?? string.Empty));
        }
        return rows;
    }

    Task<bool> IOcrAssignContext.ApplyAsync(OcrRecognizedRecord record, OcrAssignmentTarget target)
    {
        var written = false;
        switch (target.Key)
        {
            case OcrAssignmentPlanner.TargetKeys.Head:
                written = ApplyToRow(Applicants.FirstOrDefault(a => a.IsHead), record);
                break;

            case var k when OcrAssignmentPlanner.TargetKeys.IsMember(k):
                var indexStr = k[OcrAssignmentPlanner.TargetKeys.MemberPrefix.Length..];
                if (int.TryParse(indexStr, out var index) && index >= 0 && index < Applicants.Count)
                    written = ApplyToRow(Applicants[index], record);
                break;

            case OcrAssignmentPlanner.TargetKeys.NewSupporter:
                written = AddOcrApplicantRow(record);
                break;
        }

        if (written)
        {
            Logger.LogBusiness("OCR 回填快速核查授权人",
                ("Category", record.Category),
                ("Target", target.Key),
                ("IdCard", DataMasker.MaskIdCard(record.GetField(OcrFieldKeys.IdCard))));
        }
        return Task.FromResult(written);
    }

    /// <summary>回填到授权人行（姓名/证号只填空）</summary>
    private static bool ApplyToRow(AssetCheckItemDto? row, OcrRecognizedRecord record)
    {
        if (row is null) return false;
        var written = false;

        var name = record.GetField(OcrFieldKeys.Name);
        var idCard = record.GetField(OcrFieldKeys.IdCard);
        if (OcrAssignmentPlanner.IsBlank(row.ApplicantName) && name.Length > 0) { row.ApplicantName = name; written = true; }
        if (OcrAssignmentPlanner.IsBlank(row.ApplicantIdCard) && idCard.Length > 0) { row.ApplicantIdCard = idCard; written = true; }
        return written;
    }

    /// <summary>新增授权人行（名单外人物记录默认去向：子女赡养人）</summary>
    private bool AddOcrApplicantRow(OcrRecognizedRecord record)
    {
        var name = record.GetField(OcrFieldKeys.Name);
        var idCard = record.GetField(OcrFieldKeys.IdCard);
        if (name.Length == 0 && idCard.Length == 0)
            return false;

        var relIndex = AssetCheckItemDto.Relationships.IndexOf(DefaultNewRowRelationship);
        var row = new AssetCheckItemDto
        {
            ApplicantName = name,
            ApplicantIdCard = idCard,
            RelationshipIndex = relIndex >= 0 ? relIndex : 0,
            ApplicantIdType = IdTypes.FirstOrDefault() ?? IdTypeConstants.DefaultIdType,
            IsHead = false,
            BatchId = _batchId
        };
        Applicants.Add(row);
        return true;
    }
}
