using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 低收入认定表单的识别结果回填上下文（partial 扩展）。
/// 目标：户主行 / 各家庭成员行 / 新增赡养人 + 表单级（开户行卡号/户籍地址/申请事由详情）。
/// 回填铁律：只填空字段（防误覆盖）；金额字段一律不回填（§9）。
/// </summary>
public partial class ApplicationFormViewModel : IOcrAssignContext
{
    IReadOnlyList<OcrAssignmentTarget> IOcrAssignContext.GetTargets(string categoryCode)
    {
        var targets = new List<OcrAssignmentTarget>();
        switch (categoryCode)
        {
            case PhotoCategoryConstants.ID_CARD:
            case PhotoCategoryConstants.DISABILITY_CERT:
                // 人物类：户主 + 各成员行 + 新增赡养人
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.Head, "户主"));
                for (var i = 0; i < FamilyMembers.Count; i++)
                {
                    var m = FamilyMembers[i];
                    if (m.IsHouseholdHead || m.RelationshipToHead == "本人/户主")
                        continue; // 户主已在首位
                    var label = string.IsNullOrWhiteSpace(m.Name) ? $"成员 {i + 1}" : $"成员「{m.Name}」";
                    targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.Member(i.ToString()), label));
                }
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.NewSupporter, "新增赡养人"));
                break;

            case PhotoCategoryConstants.BANK_CARD:
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.FormBank, "开户行/银行卡号"));
                break;

            case PhotoCategoryConstants.HOUSEHOLD_REGISTER:
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.FormHukou, "户籍地址"));
                break;

            case PhotoCategoryConstants.MEDICAL_RECORD:
                targets.Add(new OcrAssignmentTarget(OcrAssignmentPlanner.TargetKeys.FormReason, "申请事由详情"));
                break;
        }
        return targets;
    }

    IReadOnlyList<OcrAssignmentPlanner.MemberRow> IOcrAssignContext.GetMemberRows()
    {
        var rows = new List<OcrAssignmentPlanner.MemberRow>();
        for (var i = 0; i < FamilyMembers.Count; i++)
        {
            var m = FamilyMembers[i];
            var isHead = m.IsHouseholdHead || m.RelationshipToHead == "本人/户主";
            if (isHead)
            {
                // 户主行身份优先取表单级字段（ApplicantName/ApplicantIdCard），成员行空时回退
                rows.Add(new OcrAssignmentPlanner.MemberRow(
                    OcrAssignmentPlanner.TargetKeys.Head,
                    OcrAssignmentPlanner.IsBlank(ApplicantName) ? m.Name ?? string.Empty : ApplicantName,
                    OcrAssignmentPlanner.IsBlank(ApplicantIdCard) ? m.IdCard ?? string.Empty : ApplicantIdCard));
                continue;
            }
            rows.Add(new OcrAssignmentPlanner.MemberRow(
                OcrAssignmentPlanner.TargetKeys.Member(i.ToString()),
                m.Name ?? string.Empty, m.IdCard ?? string.Empty));
        }
        return rows;
    }

    async Task<bool> IOcrAssignContext.ApplyAsync(OcrRecognizedRecord record, OcrAssignmentTarget target)
    {
        var written = false;
        switch (target.Key)
        {
            case OcrAssignmentPlanner.TargetKeys.Head:
                // 与旧扫描行为一致：先填表单级申请人字段（ApplicantName/ApplicantIdCard），
                // 再补户主成员行（只填空；两处可能联动也可能独立，双写防漏）
                if (record.Category == PhotoCategoryConstants.ID_CARD)
                {
                    var name = record.GetField(OcrFieldKeys.Name);
                    var idCard = record.GetField(OcrFieldKeys.IdCard);
                    if (OcrAssignmentPlanner.IsBlank(ApplicantName) && name.Length > 0) { ApplicantName = name; written = true; }
                    if (OcrAssignmentPlanner.IsBlank(ApplicantIdCard) && idCard.Length > 0) { ApplicantIdCard = idCard; written = true; }
                }
                written |= ApplyToMember(FamilyMembers.FirstOrDefault(m => m.IsHouseholdHead || m.RelationshipToHead == "本人/户主"), record);
                break;

            case var k when OcrAssignmentPlanner.TargetKeys.IsMember(k):
                var indexStr = k[OcrAssignmentPlanner.TargetKeys.MemberPrefix.Length..];
                if (int.TryParse(indexStr, out var index) && index >= 0 && index < FamilyMembers.Count)
                    written = ApplyToMember(FamilyMembers[index], record);
                break;

            case OcrAssignmentPlanner.TargetKeys.NewSupporter:
                written = await AddOcrSupporterAsync(record);
                break;

            case OcrAssignmentPlanner.TargetKeys.FormBank:
                // 只填空字段
                var bankName = record.GetField(OcrFieldKeys.BankName);
                var cardNumber = record.GetField(OcrFieldKeys.CardNumber);
                if (OcrAssignmentPlanner.IsBlank(BankName) && bankName.Length > 0) { BankName = bankName; written = true; }
                if (OcrAssignmentPlanner.IsBlank(BankAccount) && cardNumber.Length > 0) { BankAccount = cardNumber; written = true; }
                break;

            case OcrAssignmentPlanner.TargetKeys.FormHukou:
                var hukou = record.GetField(OcrFieldKeys.HukouAddress);
                if (OcrAssignmentPlanner.IsBlank(HukouAddress) && hukou.Length > 0) { HukouAddress = hukou; written = true; }
                break;

            case OcrAssignmentPlanner.TargetKeys.FormReason:
                // 事由详情由判类联动自动生成，仅在为空时补填诊断文本
                var diagnosis = record.GetField(OcrFieldKeys.Diagnosis);
                if (OcrAssignmentPlanner.IsBlank(ApplicationReasonDetail) && diagnosis.Length > 0)
                {
                    ApplicationReasonDetail = diagnosis;
                    written = true;
                }
                break;
        }

        if (written)
        {
            _logger.LogBusiness("OCR 回填低收入认定表单",
                ("Category", record.Category), ("Target", target.Key),
                ("IdCard", DataMasker.MaskIdCard(record.GetField(OcrFieldKeys.IdCard))));
        }
        return written;
    }

    /// <summary>回填到家庭成员行（姓名/证号只填空；残疾证号填后触发联动解析）</summary>
    private bool ApplyToMember(FamilyMember? member, OcrRecognizedRecord record)
    {
        if (member is null) return false;
        var written = false;

        if (record.Category == PhotoCategoryConstants.ID_CARD)
        {
            var name = record.GetField(OcrFieldKeys.Name);
            var idCard = record.GetField(OcrFieldKeys.IdCard);
            if (OcrAssignmentPlanner.IsBlank(member.Name) && name.Length > 0) { member.Name = name; written = true; }
            if (OcrAssignmentPlanner.IsBlank(member.IdCard) && idCard.Length > 0)
            {
                member.IdCard = idCard;
                written = true;
            }
        }
        else if (record.Category == PhotoCategoryConstants.DISABILITY_CERT)
        {
            var certNo = record.GetField(OcrFieldKeys.CertNo);
            if (OcrAssignmentPlanner.IsBlank(member.DisabilityCertificateNo) && certNo.Length > 0)
            {
                member.DisabilityCertificateNo = certNo;
                // 触发现有联动：解析残疾类型/等级（机制见 OnDisabilityCertificateChanged）
                OnDisabilityCertificateChanged(member);
                written = true;
            }
        }
        return written;
    }

    /// <summary>新增赡养人行（名单外人物记录的默认去向）</summary>
    private async Task<bool> AddOcrSupporterAsync(OcrRecognizedRecord record)
    {
        var name = record.GetField(OcrFieldKeys.Name);
        var idCard = record.GetField(OcrFieldKeys.IdCard);
        if (name.Length == 0 && idCard.Length == 0)
            return false;

        await AddSupporterAsync("赡养");
        var supporter = Supporters.LastOrDefault();
        if (supporter is null) return false;

        supporter.Name = name;
        supporter.IdCard = idCard;
        _logger.LogBusiness("OCR 新增赡养人",
            ("IdCard", DataMasker.MaskIdCard(idCard)));
        return true;
    }
}
