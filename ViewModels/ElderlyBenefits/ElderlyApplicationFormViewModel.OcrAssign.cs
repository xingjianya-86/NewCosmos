using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 高龄津贴表单的识别结果回填上下文（partial 扩展）。
/// 高龄为单人申请：人物类记录回填申请人（Name/IdCard），银行卡类回填开户行/卡号。
/// 回填铁律：只填空字段；金额字段不回填（§9）。
/// </summary>
public partial class ElderlyApplicationFormViewModel : IOcrAssignContext
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
        }
        return targets;
    }

    IReadOnlyList<OcrAssignmentPlanner.MemberRow> IOcrAssignContext.GetMemberRows()
        => new List<OcrAssignmentPlanner.MemberRow>
        {
            new(OcrAssignmentPlanner.TargetKeys.Head, Name ?? string.Empty, IdCard ?? string.Empty)
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
                    if (OcrAssignmentPlanner.IsBlank(Name) && name.Length > 0) { Name = name; written = true; }
                    if (OcrAssignmentPlanner.IsBlank(IdCard) && idCard.Length > 0) { IdCard = idCard; written = true; }
                }
                break;

            case OcrAssignmentPlanner.TargetKeys.FormBank:
                var bankName = record.GetField(OcrFieldKeys.BankName);
                var cardNumber = record.GetField(OcrFieldKeys.CardNumber);
                if (OcrAssignmentPlanner.IsBlank(BankName) && bankName.Length > 0) { BankName = bankName; written = true; }
                if (OcrAssignmentPlanner.IsBlank(BankAccount) && cardNumber.Length > 0) { BankAccount = cardNumber; written = true; }
                break;
        }

        if (written)
        {
            Logger.LogBusiness("OCR 回填高龄津贴表单",
                ("Category", record.Category),
                ("Target", target.Key),
                ("IdCard", DataMasker.MaskIdCard(record.GetField(OcrFieldKeys.IdCard))));
        }
        return Task.FromResult(written);
    }
}
