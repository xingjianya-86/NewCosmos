using NewCosmos.Models.Entities;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 文书基准分类（复核前旧分类）解析。
///
/// 家庭经济复核（含成员变更/户主死亡停旧建新）之后，新档的 classification_result 是**复核结论**
/// （如 收入超标 / 不符合认定条件），而文书描述的是救助对象**复核前**的身份。
/// 实例：奚德芳 231025197109015226 —— 旧档 app104=UrbanSubsistence（城市低保），复核后新档
/// app105=UrbanIncomeExceeded；入户调查表标题须出「林口县申请城乡最低生活保障对象入户调查表」，
/// 定期复核审批表{保障类型}=低保、{档案分类}=A类，而不是「收入超标」。
///
/// 取值顺序（逐级回退；任何一步失败只记日志并回落，绝不产出空分类）：
///  ① 停旧建新链：app.OriginalApplicationId &gt; 0 → 取原档 classification_result（复核/变更前分类）
///  ② 同档复核（分类原地更新、无新档）：最新一条复核/变更记录的 old_classification
///  ③ 回落 app.ClassificationResult —— 无变更记录的普通首次申请档，输出与改动前完全一致
///
/// ⚠️ 仅供**文书内容**（标题/享受类别/档案分类/调查走访情况等文字）使用。
///   模板选型用的 PrintNavigationData.Classification（= _currentClassification）必须继续取档案
///   自身分类，否则停保档的 档案_变更告知书/封面/目录（categories 全是停保类）在输出列表里选不中。
///   认定结果告知书(204){享受分类} 同样保留档案自身分类——它公告的是本次认定/复核结果。
/// </summary>
public partial class ArchiveProductionViewModel
{
    /// <summary>本次装配的文书基准分类（复核前旧分类）；每次装配开头重置，防上一户残留</summary>
    private string _docClassification = string.Empty;

    private async Task<string> ResolveDocumentClassificationAsync(ApplicationEntity app, CancellationToken ct)
    {
        var own = app.ClassificationResult ?? string.Empty;

        try
        {
            // ① 停旧建新链：原档分类即复核/变更前分类
            if (app.OriginalApplicationId > 0)
            {
                var oldRes = await _applicationService.GetByIdAsync(app.OriginalApplicationId, ct);
                var oldClassification = oldRes.Value?.ClassificationResult;
                if (oldRes.IsSuccess && !string.IsNullOrEmpty(oldClassification))
                {
                    if (!string.Equals(oldClassification, own, StringComparison.Ordinal))
                    {
                        _logger.LogBusiness("文书分类取复核前旧分类（停旧建新链）",
                            ("ApplicationId", app.Id.ToString()),
                            ("OriginalApplicationId", app.OriginalApplicationId.ToString()),
                            ("OldClassification", oldClassification),
                            ("NewClassification", own));
                    }
                    return oldClassification!;
                }

                _logger.Warn($"文书基准分类-原档分类缺失，回退档案自身分类: ApplicationId={app.Id}, "
                    + $"OriginalApplicationId={app.OriginalApplicationId}, ErrorCode={oldRes.ErrorCode ?? "-"}");
            }

            // ② 同档复核：变更记录 old_classification（覆盖经济复核/户主死亡/成员变更完整流程）
            var reviewRes = await _changeService.GetLatestReviewChangeRecordAsync(app.Id, ct);
            if (reviewRes.IsSuccess
                && reviewRes.Value is { } review
                && !string.IsNullOrEmpty(review.OldClassification)
                && !string.Equals(review.OldClassification, own, StringComparison.Ordinal))
            {
                _logger.LogBusiness("文书分类取复核前旧分类（变更记录）",
                    ("ApplicationId", app.Id.ToString()),
                    ("ChangeId", review.Id.ToString()),
                    ("OldClassification", review.OldClassification!),
                    ("NewClassification", own));
                return review.OldClassification!;
            }

            if (reviewRes.IsFailure)
            {
                _logger.Warn($"文书基准分类-复核记录查询失败，回退档案自身分类: ApplicationId={app.Id}, "
                    + $"ErrorCode={reviewRes.ErrorCode ?? "-"}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"文书基准分类解析异常，回退档案自身分类: ApplicationId={app.Id}, {ex.Message}");
        }

        return own;
    }
}
