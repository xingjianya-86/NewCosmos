using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.ViewModels.TempRelief;

/// <summary>
/// 临时救助表单的识别结果回填上下文（partial 扩展）。
/// 单人申请：人物类回填申请人；银行卡类回填开户行/卡号；病历类回填困难原因（诊断文本）。
/// 回填铁律：只填空字段；金额字段不回填（§9）。
/// </summary>
public partial class TempReliefFormViewModel : IOcrAssignContext
{
    IReadOnlyList<OcrAssignmentTarget> IOcrAssignContext.GetTargets(string categoryCode)
    {
        var targets = new List<OcrAssignmentTarget>();
        switch (categoryCode)
        {
            case PhotoCategoryConstants.ID_CARD:
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.Head, "申请人"));
                break;
            case PhotoCategoryConstants.BANK_CARD:
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.FormBank, "开户行/银行卡号"));
                break;
            case PhotoCategoryConstants.MEDICAL_RECORD:
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.FormReason, "困难原因"));
                break;
        }
        return targets;
    }

    IReadOnlyList<OcrAssignmentPlanner.MemberRow> IOcrAssignContext.GetMemberRows()
        => new List<OcrAssignmentPlanner.MemberRow>
        {
            new(OcrAssignmentPlanner.TargetKeys.Head, ApplicantName ?? string.Empty, ApplicantIdCard ?? string.Empty)
        };

    Task<bool> IOcrAssignContext.ApplyAsync(OcrRecognizedRecord record, OcrAssignmentTarget target)
    {
        var written = false;
        switch (target.Key)
        {
            case OcrAssignmentPlanner.TargetKeys.Head:
                if (record.Category == PhotoCategoryConstants.ID_CARD)
                {
                    var name = record.GetField(OcrFieldKeys.Name);
                    var idCard = record.GetField(OcrFieldKeys.IdCard);
                    if (OcrAssignmentPlanner.IsBlank(ApplicantName) && name.Length > 0) { ApplicantName = name; written = true; }
                    if (OcrAssignmentPlanner.IsBlank(ApplicantIdCard) && idCard.Length > 0) { ApplicantIdCard = idCard; written = true; }
                }
                break;

            case OcrAssignmentPlanner.TargetKeys.FormBank:
                var bankName = record.GetField(OcrFieldKeys.BankName);
                var cardNumber = record.GetField(OcrFieldKeys.CardNumber);
                if (OcrAssignmentPlanner.IsBlank(BankName) && bankName.Length > 0) { BankName = bankName; written = true; }
                if (OcrAssignmentPlanner.IsBlank(BankAccount) && cardNumber.Length > 0) { BankAccount = cardNumber; written = true; }
                break;

            case OcrAssignmentPlanner.TargetKeys.FormReason:
                // 困难原因可自动生成（_suppressReasonDirty 联动），仅空时补填诊断文本
                var diagnosis = record.GetField(OcrFieldKeys.Diagnosis);
                if (OcrAssignmentPlanner.IsBlank(DifficultyReason) && diagnosis.Length > 0)
                {
                    DifficultyReason = diagnosis;
                    written = true;
                }
                break;
        }

        if (written)
        {
            Logger.LogBusiness("OCR 回填临时救助表单",
                ("Category", record.Category),
                ("Target", target.Key),
                ("IdCard", DataMasker.MaskIdCard(record.GetField(OcrFieldKeys.IdCard))));
        }
        return Task.FromResult(written);
    }
}
