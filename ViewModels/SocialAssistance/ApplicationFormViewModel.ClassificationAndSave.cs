using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Exceptions;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;

using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ChangeManagement;
using System.Collections.ObjectModel;
using System.ComponentModel;

using Application = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

public partial class ApplicationFormViewModel
{
    #region 收入计算

    /// <summary>
    /// 从土地登记信息更新土地面积汇总字段
    /// </summary>
    private void UpdateLandAreasFromRegistrations()
    {
        // 有归户表时，不从土地登记更新（归户表优先）
        if (LandConfirmationGroups.Count > 0)
            return;

        decimal selfFarmed = 0, subleased = 0, contracted = 0;

        foreach (var reg in LandRegistrations)
        {
            switch (reg.LandUsage)
            {
                case DictionaryConstants.LandUsage.SELF_FARM:
                    selfFarmed += (decimal)reg.Area;
                    break;
                case DictionaryConstants.LandUsage.SUBLEASE:
                    subleased += (decimal)reg.Area;
                    break;
                case DictionaryConstants.LandUsage.CONTRACT:
                    contracted += (decimal)reg.Area;
                    break;
            }
        }

        SelfFarmedLandArea = selfFarmed;
        SubleasedLandArea = subleased;
        ContractedLandArea = contracted;
    }

    /// <summary>
    /// 重新计算收入
    /// </summary>
    [RelayCommand]
    private void RecalculateIncome()
    {
        try
        {
            RecalculateIncomeCore();
        }
        catch (Exception ex)
        {
            // 收入重算绝不因异常崩溃：记录错误，保留上一轮计算结果
            _logger.LogError(ex, "收入重算失败");
        }
    }

    /// <summary>收入重算核心（异常由调用方捕获）</summary>
    private void RecalculateIncomeCore()
    {
        // 从土地登记信息更新汇总字段
        UpdateLandAreasFromRegistrations();

        // 【月收入】计算务工收入：Σ(每人月收入)
        WorkIncomeTotal = _incomeCalculationService.CalculateLaborIncome(LaborIncomes.ToList());

        // 【月收入】计算经营净收入：Σ(每人月收入)
        BusinessIncomeTotal = _incomeCalculationService.CalculateBusinessNetIncome(BusinessIncomes.ToList());

        // 【月收入】计算土地收入（年收入，不除以12）
        if (LandConfirmationGroups.Count == 0)
        {
            LandIncomeTotal = _incomeCalculationService.CalculateLandIncome(
                SelfFarmedLandArea, (double)SelfFarmUnitPrice,
                SubleasedLandArea, (double)SubleaseUnitPrice,
                ContractedLandArea, (double)ContractUnitPrice);
        }

        // 【年收入】计算农业补贴（年总额，不除以12）
        SubsidyTotal = _incomeCalculationService.CalculateSubsidyIncome(Subsidies.ToList());

        // 【年收入】计算赡养费（年值 = Σ每笔年赡养费）
        AlimonyIncome = _incomeCalculationService.CalculateAlimonyAnnual(Supporters.ToList());

        // 【月收入】计算刚性支出（月支出）
        RigidExpenditure = _incomeCalculationService.CalculateRigidExpenditure(RigidExpenditures.ToList());

        // 【月收入】计算财产净收入（月收入）
        PropertyIncomeTotal = PropertyIncomes.Sum(p => p.Amount);

        // 【月收入】计算转移净收入（月收入）
        TransferIncomeTotal = TransferIncomes.Sum(t => t.TotalAmount);

        // 【月收入】计算其他收入（月收入）
        OtherIncomeTotal = OtherIncomes.Sum(o => o.Amount);

        // 【年值基准】年家庭总收入 = Σ(月项×12) + 赡养年值 + 土地年值 + 补贴年值 − 刚性×12，一次性舍入
        TotalAnnualIncome = _incomeCalculationService.CalculateAnnualFamilyIncome(
            WorkIncomeTotal,
            BusinessIncomeTotal,
            PropertyIncomeTotal,
            TransferIncomeTotal,
            OtherIncomeTotal,
            AlimonyIncome,       // 年值
            LandIncomeTotal,     // 年值
            SubsidyTotal,        // 年值
            RigidExpenditure);   // 月值

        // 月值 = 年值÷12（分解显示口径，禁止从月值×12 反推年值）
        TotalFamilyIncome = _incomeCalculationService.MonthlyFromAnnual(TotalAnnualIncome);

        // 年人均 / 月人均（一次舍入，判定与落库同口径）
        PerCapitaAnnualIncome = _incomeCalculationService.CalculatePerCapitaAnnual(TotalAnnualIncome, FamilySize);
        PerCapitaIncome = _incomeCalculationService.PerCapitaMonthly(TotalAnnualIncome, FamilySize);

        // 通知刚性支出明细变化
        OnPropertyChanged(nameof(MedicalExpenditure));
        OnPropertyChanged(nameof(EducationExpenditure));
        OnPropertyChanged(nameof(DisabilityRehabExpenditure));
        OnPropertyChanged(nameof(LivingExpenditure));
        OnPropertyChanged(nameof(IncomeSubtotal));
        OnPropertyChanged(nameof(TotalFamilyIncomeAnnual));
        OnPropertyChanged(nameof(PerCapitaIncomeAnnual));
    }

    #endregion

    #region 分类判定

    [RelayCommand]
    private async Task ClassifyAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("开始分类判定");

            // 构建申请对象（必须包含身份证号和健康状况，用于推导年龄和判断劳动能力）
            var application = new Application
            {
                Id = _applicationId,
                ApplicantIdCard = ApplicantIdCard,
                HealthStatus = SelectedHealthStatusObj?.Key ?? "",
                HukouType = HukouType?.Key ?? "",
                FamilySize = FamilySize,
                TotalFamilyIncome = TotalFamilyIncome,
                PerCapitaIncome = PerCapitaIncome,
                TotalAnnualIncome = TotalAnnualIncome,
                PerCapitaAnnualIncome = PerCapitaAnnualIncome,
                RigidExpenditure = RigidExpenditure,
                ClassificationResult = ClassificationResult,
                OriginalClassificationResult = _originalClassificationContext ?? ClassificationResult,
                CaregiverType = CaregiverType,
                DestituteSupportType = DestituteSupportType
            };

            // 执行分类判定
            var result = await _classificationService.DetermineClassificationAsync(
                application,
                FamilyMembers.ToList(),
                Supporters.ToList(),
                Caregivers.ToList(),
                ct);

            if (result.IsSuccess && result.Value != null)
            {
                var classification = result.Value;

                ClassificationResult = classification.Classification;
                ClassificationDescription = classification.Description;
                IsEligible = classification.IsEligible;
                IneligibleReason = classification.IneligibleReason;
                var priorGuaranteeForGrace = GuaranteeAmount;
                GuaranteeAmount = classification.GuaranteeAmount;

                // 分类施保
                if (classification.ClassifiedSubsidy != null)
                {
                    ClassifiedSubsidyType = classification.ClassifiedSubsidy.Types;
                    ClassifiedSubsidyAmount = classification.ClassifiedSubsidy.TotalAmount;
                }

                // 渐退期（1B：判定命中先弹确认页，确认后才应用；取消则本次不进渐退、可改数据再判定）
                if (classification.GracePeriod != null && classification.GracePeriod.IsEligible)
                {
                    // 链档案渐退期内按原分类待遇重算分类施保（低收入判定不算分类施保，死亡/变更链新档需保住幸存成员份额）
                    var graceClassified = await TryCalcChainGraceClassifiedAsync(application, ct);
                    var selectedMonths = await ShowGracePeriodConfirmAsync(classification, graceClassified, ct);
                    if (selectedMonths is int months && GracePeriodConstants.IsValidMonths(months))
                    {
                        IsInGracePeriod = true;
                        GracePeriodMonths = months;
                        GracePeriodStartDate = classification.GracePeriod.StartDate;
                        GracePeriodEndDate = classification.GracePeriod.StartDate.AddMonths(months).AddDays(-1);
                        OriginalClassificationResult = classification.GracePeriod.OriginalClassification;
                        if (graceClassified != null)
                            ApplyGraceClassifiedSubsidy(graceClassified);
                        await ApplyGraceCapAsync(priorGuaranteeForGrace, ct);
                    }
                    else
                    {
                        IsInGracePeriod = false;
                        GraceGrantAmount = null;
                    }
                }
                else
                {
                    IsInGracePeriod = false;
                    GraceGrantAmount = null;
                }
                _gracePeriodEvaluated = true;

                // 特困：自动计算照料护理费（集中供养不发钱，护理费为 0；仅分散供养按能力鉴定计算）
                // 非特困：清零旧照料费（H2——分类离开特困后禁止沿用旧值进总额）
                if (ClassificationConstants.IsCodeDestitute(ClassificationResult))
                {
                    CaregiverSubsidyAmount = IsCentralizedSupport
                        ? 0
                        : await _classificationService.CalculateCareAllowanceAsync(
                            _applicationId, ct);
                }
                else
                {
                    CaregiverSubsidyAmount = 0;
                }

                // 计算保障金总额
                TotalGuaranteeAmount = _guaranteeAmountService.CalculateTotalGuaranteeAmount(
                    GuaranteeAmount,
                    ClassifiedSubsidyAmount,
                    CaregiverSubsidyAmount);

                // 立即保存分类结果到数据库
                await SaveClassificationResultAsync();

                _logger.LogBusiness("分类判定完成",
                    ("分类", classification.Classification),
                    ("描述", classification.Description),
                    ("保障金额", TotalGuaranteeAmount));
            }
            else
            {
                _logger.Error($"Classification 失败: {result.Message}");
            }
        }, "分类判定中...");
    }

    [RelayCommand]
    private async Task DetermineClassificationAsync()
    {
        if (IsDetermining) return;

        // 补全模式：认定结果锁定，不允许重新判定（补全不应改变认定结果）
        if (IsCompletionMode)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "补全模式下认定结果已锁定，不允许重新分类判定", "确定");
            return;
        }

        // 单人保申请锁定分类结果，不允许重新判定
        if (IsSingleRescueApplication)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "该申请已标记为单人保，分类结果已锁定，无需重新判定", "确定");
            return;
        }

        // 已确定档案（Approved/Stopped）仅允许在合法变更流程模式下重算
        if (IsClassificationLocked)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "已确定的档案不允许重新分类判定，请通过经济复核/家庭信息修正等变更流程办理", "确定");
            return;
        }

        try
        {
            IsDetermining = true;
            _logger.LogBusiness("开始分类判定（Step5）");

            // 强制重新计算收入，确保汇总字段是最新的
            RecalculateIncome();

            var application = BuildApplication(ApplicationStatusCodes.DRAFT);
            // 变更链（户主死亡停旧建新等）：带上上游原分类，否则渐退期"低保→低收入"判不出
            application.OriginalClassificationResult =
                _originalClassificationContext ?? application.OriginalClassificationResult;

            var result = await _classificationService.DetermineClassificationAsync(
                application,
                FamilyMembers.ToList(),
                Supporters.ToList(),
                Caregivers.ToList(),
                CancellationToken);

            if (result.IsSuccess && result.Value != null)
            {
                var classification = result.Value;
                _lastClassificationResult = classification;

                ClassificationResult = classification.Classification;
                ClassificationDescription = classification.Description;
                IsEligible = classification.IsEligible;
                IneligibleReason = classification.IneligibleReason;
                var priorGuaranteeForGrace = GuaranteeAmount;
                GuaranteeAmount = classification.GuaranteeAmount;
                DeterminationBasis = classification.DeterminationBasis;

                DeterminationDetails.Clear();
                foreach (var detail in classification.DeterminationDetails)
                {
                    DeterminationDetails.Add(detail);
                }

                // 分类施保（Types=界面显示；Type=落库字段，BuildApplication 保存用，两者须同步）
                if (classification.ClassifiedSubsidy != null)
                {
                    ClassifiedSubsidyTypes = classification.ClassifiedSubsidy.Types;
                    ClassifiedSubsidyType = classification.ClassifiedSubsidy.Types;
                    ClassifiedSubsidyCount = classification.ClassifiedSubsidy.Count;
                    ClassifiedSubsidyPerPerson = classification.ClassifiedSubsidy.PerPersonAmount;
                    ClassifiedSubsidyAmount = classification.ClassifiedSubsidy.TotalAmount;
                }

                // 渐退期（1B：判定命中先弹确认页，确认后才应用；取消则本次不进渐退、可改数据再判定）
                if (classification.GracePeriod != null && classification.GracePeriod.IsEligible)
                {
                    // 链档案渐退期内按原分类待遇重算分类施保（低收入判定不算分类施保，死亡/变更链新档需保住幸存成员份额）
                    var graceClassified = await TryCalcChainGraceClassifiedAsync(application, CancellationToken);
                    var selectedMonths = await ShowGracePeriodConfirmAsync(classification, graceClassified, CancellationToken);
                    if (selectedMonths is int months && GracePeriodConstants.IsValidMonths(months))
                    {
                        IsInGracePeriod = true;
                        GracePeriodMonths = months;
                        GracePeriodStartDate = classification.GracePeriod.StartDate;
                        GracePeriodEndDate = classification.GracePeriod.StartDate.AddMonths(months).AddDays(-1);
                        OriginalClassificationResult = classification.GracePeriod.OriginalClassification;
                        if (graceClassified != null)
                            ApplyGraceClassifiedSubsidy(graceClassified);
                        await ApplyGraceCapAsync(priorGuaranteeForGrace, CancellationToken);
                    }
                    else
                    {
                        IsInGracePeriod = false;
                        GraceGrantAmount = null;
                    }
                }
                else
                {
                    IsInGracePeriod = false;
                    GraceGrantAmount = null;
                }
                _gracePeriodEvaluated = true;

                // 特困：自动计算照料护理费（集中供养不发钱，护理费为 0；仅分散供养按能力鉴定计算）
                // 非特困：清零旧照料费（H2——分类离开特困后禁止沿用旧值进总额）
                if (ClassificationConstants.IsCodeDestitute(ClassificationResult))
                {
                    CaregiverSubsidyAmount = IsCentralizedSupport
                        ? 0
                        : await _classificationService.CalculateCareAllowanceAsync(
                            _applicationId, CancellationToken);
                }
                else
                {
                    CaregiverSubsidyAmount = 0;
                }

                TotalGuaranteeAmount = _guaranteeAmountService.CalculateTotalGuaranteeAmount(
                    GuaranteeAmount,
                    ClassifiedSubsidyAmount,
                    CaregiverSubsidyAmount);

                IsClassificationDone = true;

                // 单人保信息
                NeedsSingleRescueDraft = classification.NeedsSingleRescueDraft;
                SingleRescueMembers.Clear();
                if (classification.SingleRescueMembers != null)
                {
                    foreach (var member in classification.SingleRescueMembers)
                    {
                        SingleRescueMembers.Add(member);
                    }
                }
                OnPropertyChanged(nameof(SingleRescueHint));

                // 通知特困分类属性变更
                OnPropertyChanged(nameof(IsDestituteClassification));

                // 立即保存分类结果到数据库
                await SaveClassificationResultAsync();

                _logger.LogBusiness("分类判定完成",
                    ("Classification", ClassificationResult),
                    ("Description", ClassificationDescription),
                    ("IsEligible", IsEligible),
                    ("GuaranteeAmount", GuaranteeAmount));
            }
            else
            {
                _logger.Error($"分类判定失败: {result.Message}");
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("错误", $"分类判定失败: {result.Message}", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"分类判定异常: {ex.Message}");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"分类判定异常: {ex.Message}", "确定");
        }
        finally
        {
            IsDetermining = false;
        }
    }

    /// <summary>
    /// 弹出渐退期确认页（modal，模式与 MemberChangeReasonPopup 一致）：
    /// 确认 => 所选月数（调用方应用渐退状态并继续保存）；取消/异常 => null（本次不进渐退）。
    /// graceClassified：链档案渐退口径的分类施保预估（null=非链/预估失败，确认页不显示分类施保行）。
    /// </summary>
    private async Task<int?> ShowGracePeriodConfirmAsync(
        Services.Domain.SocialAssistance.ClassificationResult classification,
        ClassifiedSubsidyResult? graceClassified,
        CancellationToken ct)
    {
        try
        {
            var standard = await GetCachedSubsistenceStandardAsync();
            var grace = classification.GracePeriod;
            var parameter = new GracePeriodConfirmParameter(
                ClassificationConstants.ConvertFromCode(grace.OriginalClassification),
                ClassificationConstants.ConvertFromCode(classification.Classification),
                standard,
                PerCapitaIncome,
                TotalAnnualIncome,
                FamilySize,
                classification.GuaranteeAmount,
                grace.Months,
                grace.StartDate,
                grace.EndDate,
                _originalClassifiedContext,
                graceClassified?.TotalAmount);

            ContentPage popup;
            Task<GracePeriodConfirmResult> resultTask;
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                var mobilePopup = _serviceProvider.GetRequiredService<Pages.Mobile.MobileGracePeriodConfirmPage>();
                mobilePopup.Initialize(parameter);
                popup = mobilePopup;
                resultTask = mobilePopup.Result;
            }
            else
            {
                var desktopPopup = _serviceProvider.GetRequiredService<Pages.ChangeManagement.GracePeriodConfirmPage>();
                desktopPopup.Initialize(parameter);
                popup = desktopPopup;
                resultTask = desktopPopup.Result;
            }

            var navigation = Helpers.WindowNavigator.CurrentPage?.Navigation;
            if (navigation == null)
            {
                _logger.Warn("当前页面为空，无法弹出渐退期确认页");
                return null;
            }
            await navigation.PushModalAsync(popup);
            var result = await resultTask.WaitAsync(ct);
            if (navigation.ModalStack.Contains(popup))
            {
                await navigation.PopModalAsync();
            }
            return result.Confirmed ? result.SelectedMonths : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"渐退期确认页异常: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 链档案渐退期内分类施保预估：低收入分类不计算分类施保（ClassificationService 口径），
    /// 渐退期内待遇按原分类（低保）对幸存成员延续，故按权威 CalculateClassifiedSubsidyAsync 重算。
    /// 非链档案返回 null（沿用判定值，行为不变）；标准配置缺失等异常返回 null 并告警——
    /// 预估只用于确认页展示与"确认"后应用，异常禁止按 0 写库。
    /// </summary>
    private async Task<ClassifiedSubsidyResult?> TryCalcChainGraceClassifiedAsync(
        Application application, CancellationToken ct)
    {
        if (_originalApplicationId <= 0) return null;
        try
        {
            var isRural = ClassificationConstants.HukouType.IsHukouRural(application.HukouType ?? "");
            var sub = await _classificationService.CalculateClassifiedSubsidyAsync(
                isRural, application, FamilyMembers.ToList(), ct);
            if (sub == null || sub.PerPersonAmount <= 0)
            {
                _logger.Warn($"渐退分类施保预估无效（标准缺失或空结果），不应用: ApplicationId={_applicationId}");
                return null;
            }
            return sub;
        }
        catch (Exception ex)
        {
            _logger.Warn($"渐退分类施保预估失败，不应用: ApplicationId={_applicationId}, {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 应用渐退期内分类施保（确认进入渐退期后调用）：Types/Count/PerPerson/Amount 与判定路径四项同步覆写，
    /// 保障金总额由调用方后续统一合成。
    /// </summary>
    private void ApplyGraceClassifiedSubsidy(ClassifiedSubsidyResult sub)
    {
        ClassifiedSubsidyTypes = sub.Types;
        ClassifiedSubsidyType = sub.Types;
        ClassifiedSubsidyCount = sub.Count;
        ClassifiedSubsidyPerPerson = sub.PerPersonAmount;
        ClassifiedSubsidyAmount = sub.TotalAmount;
    }

    /// <summary>
    /// 创建单人保草稿
    /// </summary>
    [RelayCommand]
    private async Task CreateSingleRescueDraftAsync()
    {
        if (!NeedsSingleRescueDraft || SingleRescueMembers.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "当前没有符合单人保条件的成员", "确定");
            return;
        }

        // 先保存当前申请
        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先保存当前申请，再创建单人保草稿", "确定");
            return;
        }

        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();

        foreach (var member in SingleRescueMembers)
        {
            var result = await _applicationService.CreateSingleRescueDraftAsync(
                _applicationId, member, CancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogBusiness("单人保草稿已创建",
                    ("SourceApplicationId", _applicationId),
                    ("NewApplicationId", result.Value),
                    ("MemberName", NewCosmos.Services.Core.DataMasker.MaskName(member.Name)));

                await dialogService.DisplayAlertAsync("成功",
                    $"已为 {member.Name} 创建单人保草稿申请，请在工作流中继续处理", "确定");
            }
            else
            {
                _logger.Error($"创建单人保草稿失败: {result.Message}");
                await dialogService.DisplayAlertAsync("错误",
                    $"为 {member.Name} 创建单人保草稿失败: {result.Message}", "确定");
            }
        }
    }

    /// <summary>
    /// 编辑能力鉴定
    /// </summary>
    [RelayCommand]
    private async Task EditCapabilityAssessmentAsync()
    {
        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先保存申请", "确定");
            return;
        }

        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.CapabilityAssessmentPage, ApplicationScopedParameter>(new ApplicationScopedParameter(_applicationId));
        }
        catch (Exception ex)
        {
            _logger.Error($"打开能力鉴定失败: {ex.Message}");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"打开能力鉴定失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 发起一事一议（Step5 分类判定不符合时）
    /// </summary>
    [RelayCommand]
    private async Task OpenSpecialApprovalAsync()
    {
        // 补全模式：认定结果锁定、不改变认定状态，不触发一事一议（完成补全后在复核模式重新认定再申报）
        if (IsCompletionMode)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "数据补全模式下不可发起一事一议。请先完成补全保存，后续在经济状况复核模式重新认定后，按程序申报。", "确定");
            return;
        }

        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先保存申请再发起一事一议", "确定");
            return;
        }

        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.SpecialApprovalFormPage, ApplicationScopedParameter>(new ApplicationScopedParameter(_applicationId));
        }
        catch (Exception ex)
        {
            _logger.Error($"打开一事一议申报表失败: {ex.Message}");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"打开一事一议申报表失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 保存分类结果到数据库
    /// </summary>
    private async Task SaveClassificationResultAsync()
    {
        if (_applicationId <= 0) return;

        try
        {
            // 保留原状态（Approved 等）——UpdateAsync 不写 status 列，此处为语义一致；
            // 新建/普通草稿兜底 Draft
            var originalStatus = string.IsNullOrEmpty(_originalStatus) ? ApplicationStatusCodes.DRAFT : _originalStatus;
            var application = BuildApplication(originalStatus);
            application.Id = _applicationId;
            // 变更流程模式（复核/成员变更等）必须放行非 Draft 档案：
            // UpdateAsync 默认只允许 Draft（ApplicationStateMachine.IsEditable），
            // 已归档 Approved 档案在 Step5 判定保存会报"当前状态不允许编辑"
            var allowNonEditable = IsCompletionMode || IsChangeMode;
            var updateResult = await _applicationService.UpdateAsync(application, CancellationToken, allowNonEditable);
            if (updateResult.IsSuccess)
            {
                // 本次更新改变了 updated_at，刷新并发令牌，避免后续保存误报冲突
                var refreshed = await _applicationService.GetByIdAsync(_applicationId, CancellationToken);
                if (refreshed.IsSuccess && refreshed.Value != null && refreshed.Value.UpdatedAt != default)
                    _loadedUpdatedAt = refreshed.Value.UpdatedAt;

                _logger.LogBusiness("分类结果已保存到数据库",
                    ("ApplicationId", _applicationId),
                    ("Classification", ClassificationResult ?? ""));

                // A1：Step5 分类确认即同步渐退行——不依赖完整保存，避免「只出文书不保存」时库里无行
                if (IsInGracePeriod && GracePeriodMonths > 0)
                    await SyncGracePeriodRecordAsync(CancellationToken);

                // 接续链（户主死亡/成员变更等停旧建新）Step5 判定跨大类时补写 CategoryAdd：
                // 月报「新增救助明细」跨类新增行与本档变更记录列表依赖此行（服务幂等，失败不阻断分类保存）
                await EnsureChainCategoryAddAsync(CancellationToken);

                // 户主死亡链进入渐退期：分类施保减发（上游原额>现额）补写减发记录挂旧档，
                // 供变更历史展示、统计排除与月报「分类施保金减发人员表」取数（服务幂等，失败不阻断分类保存）
                await EnsureChainClassifiedSubsidyReduceAsync(CancellationToken);
            }
            else
            {
                _logger.Error($"保存分类结果失败: {updateResult.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"保存分类结果异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 接续链跨大类 CategoryAdd 同步（服务端幂等：一致跳过/变化更新/回同大类软删）。
    /// 分类已落库，同步失败仅告警不抛出，避免分类保存被报告类记录拖失败。
    /// </summary>
    private async Task EnsureChainCategoryAddAsync(CancellationToken ct)
    {
        try
        {
            var result = await _changeService.EnsureChainCategoryAddAsync(
                _applicationId,
                string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName,
                ct);
            if (result.IsSuccess)
            {
                if (result.Value is long changeId && changeId > 0)
                    _logger.LogBusiness("接续链跨类新增CategoryAdd已同步",
                        ("ApplicationId", _applicationId),
                        ("ChangeId", changeId));
            }
            else
            {
                _logger.Warn($"接续链跨类新增CategoryAdd同步失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"接续链跨类新增CategoryAdd同步异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 户主死亡链渐退分类施保减发同步（服务端幂等：一致跳过/变化更新/条件消失软删）。
    /// 分类已落库，同步失败仅告警不抛出，避免分类保存被报告类记录拖失败。
    /// </summary>
    private async Task EnsureChainClassifiedSubsidyReduceAsync(CancellationToken ct)
    {
        try
        {
            var result = await _changeService.EnsureChainClassifiedSubsidyReduceAsync(
                _applicationId,
                string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName,
                ct);
            if (result.IsSuccess)
            {
                if (result.Value is long changeId && changeId > 0)
                    _logger.LogBusiness("户主死亡渐退分类施保减发记录已同步",
                        ("ApplicationId", _applicationId),
                        ("ChangeId", changeId));
            }
            else
            {
                _logger.Warn($"户主死亡渐退分类施保减发记录同步失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"户主死亡渐退分类施保减发记录同步异常: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetClassification()
    {
        // 补全模式：认定结果锁定，不允许重置（补全不应改变认定结果）
        if (IsCompletionMode) return;

        // 已确定档案（Approved/Stopped）在非变更流程模式下不允许重置
        if (IsClassificationLocked) return;

        IsClassificationDone = false;
        ClassificationResult = null;
        ClassificationDescription = null;
        IsEligible = false;
        IneligibleReason = null;
        GuaranteeAmount = 0;
        DeterminationBasis = string.Empty;
        DeterminationDetails.Clear();
        ClassifiedSubsidyTypes = string.Empty;
        ClassifiedSubsidyCount = 0;
        ClassifiedSubsidyPerPerson = 0;
        ClassifiedSubsidyAmount = 0;
        TotalGuaranteeAmount = 0;
        IsInGracePeriod = false;
        GracePeriodMonths = 0;
        GracePeriodStartDate = null;
        GracePeriodEndDate = null;
        OriginalClassificationResult = null;
        OriginalGuaranteeAmount = null;
        // 重置分类结果 ≠ 结清服务端渐退期：保存前须重新判定，禁止内存默认 false 触发 Clear
        _gracePeriodEvaluated = false;
        OnPropertyChanged(nameof(IsDestituteClassification));
        _logger.LogBusiness("重置分类判定结果");
    }

    /// <summary>
    /// 更新供养方式
    /// </summary>
    [RelayCommand]
    private async Task UpdateSupportModeAsync()
    {
        if (SelectedSupportModeObj == null)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请选择供养方式", "确定");
            return;
        }

        DestituteSupportType = SelectedSupportModeObj.Key;
        _logger.LogBusiness("更新供养方式", ("Type", DestituteSupportType));

        // 重新执行分类判定
        await DetermineClassificationAsync();

        // 保存到数据库
        await SaveClassificationResultAsync();
    }

    /// <summary>
    /// 导航到档案制作页面
    /// </summary>
    private async Task NavigateToArchiveProductionAsync()
    {
        try
        {
            _logger.LogBusiness("开始导航到档案制作页面", ("ApplicationId", _applicationId));
            _logger.LogBusiness("档案制作页面导航开始", ("ApplicationId", _applicationId));
            // 参数先于 Push 注入：页面 OnAppearing 时数据已就绪（原"先推页再加载数据"的时序收敛）
            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationArchiveParameter>(new ApplicationArchiveParameter(_applicationId));
            _logger.LogBusiness("档案制作页面导航完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"导航到档案制作页面失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    #endregion

    #region 渐退期命令

    [RelayCommand]
    private void SetGracePeriod()
    {
        var info = _gracePeriodService.SetGracePeriod(GracePeriodMonths, OriginalGuaranteeAmount);

        IsInGracePeriod = info.IsInGracePeriod;
        GracePeriodMonths = info.GracePeriodMonths;
        GracePeriodStartDate = info.GracePeriodStartDate;
        GracePeriodEndDate = info.GracePeriodEndDate;
        _gracePeriodEvaluated = true;

        _logger.LogBusiness("设置渐退期",
            ("月数", GracePeriodMonths),
            ("开始", GracePeriodStartDate?.ToString("yyyy-MM-dd") ?? string.Empty),
            ("结束", GracePeriodEndDate?.ToString("yyyy-MM-dd") ?? string.Empty));
    }

    [RelayCommand]
    private void ClearGracePeriod()
    {
        IsInGracePeriod = false;
        GracePeriodMonths = 0;
        GracePeriodStartDate = null;
        GracePeriodEndDate = null;
        OriginalClassificationResult = null;
        OriginalGuaranteeAmount = null;
        GraceGrantAmount = null;
        // 用户显式清除 → 允许保存时落库 Clear
        _gracePeriodEvaluated = true;

        _logger.LogBusiness("清除渐退期");
    }

    #endregion

    #region 保存草稿

    /// <summary>
    /// 保存草稿命令
    /// </summary>
    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        // 查看模式禁止保存
        if (IsViewMode) return;

        // 草稿最小验证：姓名和身份证号不能为空
        if (string.IsNullOrWhiteSpace(ApplicantName))
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请填写申请人姓名", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请填写身份证号", "确定");
            return;
        }

        // 如果是特困分类，验证照料人信息
        if (IsDestituteClassification && Caregivers.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "特困供养需要填写照料人信息，请先添加照料人", "确定");
            return;
        }

        // 如果是特困分类，验证供养方式（以 Picker 实际选择为准）
        if (IsDestituteClassification && SelectedSupportModeObj == null)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "特困供养需要选择供养方式，请先选择集中供养或分散供养", "确定");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("保存草稿");
            // 补全模式 / 经济复核（Review）/ 家庭修正/编辑家庭信息模式：保存草稿保持原状态（Approved 等），不降级为 Draft；
            // 契合经济复核流程（ChangeViewModel.NavigateToEconomicReviewAsync 以 Review 模式打开表单）
            var saveStatus = (IsCompletionMode || IsReviewMode || IsFamilyCorrectionMode || IsEditFamilyInfoMode)
                ? (string.IsNullOrEmpty(_originalStatus) ? ApplicationStatusCodes.APPROVED : _originalStatus)
                : ApplicationStatusCodes.DRAFT;
            await SaveApplicationInternalAsync(saveStatus, ct);
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("成功", "草稿保存成功", "确定");
        }, "保存草稿中...");
    }

    /// <summary>
    /// 统一的申请保存内部方法（SaveDraft 和 ExecuteSave 共用）。
    /// 整个保存过程运行在同一个数据库事务中：主表 + 家庭成员 + 赡养人 + 入户调查/照料人 + 经济明细
    /// 任一环节失败即抛 BusinessException → 事务自动回滚，ExecuteAsync 捕获后向用户展示错误
    /// （成功提示只会在本方法无异常返回后出现）。
    /// 内部各服务通过 BeginTransactionScopeAsync 嵌套加入本环境事务（嵌套作用域提交权归最外层）。
    /// </summary>
    private async Task SaveApplicationInternalAsync(string status, CancellationToken ct)
    {
        // Step2 新增的赡养抚养扶养人合并进 Supporters（未点"导入家庭成员"也不丢；赡养费计入收入）
        await MergeSupportMembersIntoSupportersAsync();

        // 强制重新计算收入，确保汇总字段是最新的
        LoadingMessage = "正在计算收入...";
        RecalculateIncome();

        var application = BuildApplication(status);

        var wasCreateMode = _applicationId <= 0;
        try
        {
            await _applicationService.ExecuteInTransactionAsync(
                transactionCt => SaveApplicationCoreAsync(application, transactionCt), ct);
        }
        catch
        {
            // 新建流程失败时事务已回滚（记录不存在），还原为新建状态，
            // 避免用户重试时误走"更新不存在记录"的路径
            if (wasCreateMode)
            {
                _applicationId = 0;
                OperationMode = FormOperationMode.Create;
                _loadedUpdatedAt = null;
                _loadedCurrentStep = 0;
            }
            throw;
        }

        _logger.LogBusiness("申请保存成功", ("ApplicationId", _applicationId), ("Status", status));
    }

    /// <summary>
    /// 申请保存事务体（主表 + 家庭成员 + 赡养人 + 入户调查 + 经济明细 + 渐退期）。
    /// 由 <see cref="IApplicationService.ExecuteInTransactionAsync"/> 包裹：正常完成提交，异常回滚。
    /// </summary>
    private async Task SaveApplicationCoreAsync(Application application, CancellationToken ct)
    {
        LoadingMessage = "正在保存申请信息...";
        if (_applicationId > 0)
        {
            // 编辑模式：更新已有记录（携带乐观并发令牌）；补全模式允许更新已建档的 Approved 档案
            application.Id = _applicationId;
            var updateResult = await _applicationService.UpdateAsync(application, ct, IsCompletionMode || IsEditFamilyInfoMode || IsFamilyCorrectionMode);
            if (!updateResult.IsSuccess)
            {
                _logger.Error($"申请保存失败: {updateResult.Message}");
                throw new BusinessException(
                    string.IsNullOrEmpty(updateResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : updateResult.ErrorCode,
                    updateResult.Message ?? "申请保存失败");
            }
        }
        else
        {
            // 新建模式：删除同身份证旧草稿 + 创建新记录
            var existsResult = await _applicationService.GetByIdCardAsync(ApplicantIdCard, ct);
            if (existsResult.IsSuccess && existsResult.Value?.Count > 0)
            {
                foreach (var existing in existsResult.Value)
                {
                    if (existing.Status == ApplicationStatusCodes.DRAFT)
                    {
                        var deleteResult = await _applicationService.DeleteAsync(existing.Id, ct);
                        if (!deleteResult.IsSuccess)
                        {
                            _logger.Error($"删除旧草稿失败: {deleteResult.Message}");
                            throw new BusinessException(
                                string.IsNullOrEmpty(deleteResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : deleteResult.ErrorCode,
                                $"删除旧草稿失败: {deleteResult.Message}");
                        }
                        _logger.LogBusiness("已删除旧草稿", ("OldApplicationId", existing.Id));
                    }
                }
            }

            var createResult = await _applicationService.CreateAsync(application, ct);
            if (!createResult.IsSuccess)
            {
                _logger.Error($"申请创建失败: {createResult.Message}");
                throw new BusinessException(
                    string.IsNullOrEmpty(createResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : createResult.ErrorCode,
                    createResult.Message ?? "申请创建失败");
            }
            _applicationId = createResult.Value;
            OperationMode = FormOperationMode.Edit;
        }

        // 保存家庭成员（失败抛异常），返回 IdCard → 新 ID 映射
        LoadingMessage = "正在保存家庭成员...";
        var memberIdCardToNewId = await SaveFamilyMembersAsync(ct);

        // 保存赡养人（失败抛异常），传入新 ID 映射避免 DB 查询
        LoadingMessage = "正在保存赡养人...";
        await SaveSupportersAsync(memberIdCardToNewId, ct);

        // 保存入户调查（失败抛异常）
        LoadingMessage = "正在保存入户调查...";
        await SaveHouseholdSurveyAsync(ct);

        // 保存经济明细（可能需要较长时间，失败抛异常）
        LoadingMessage = "正在保存经济明细...";
        await SaveEconomicDetailsAsync(ct);

        // 同步渐退期记录到独立表 nc_biz_grace_periods（与主表同事务提交）
        await SyncGracePeriodRecordAsync(ct);

        // 刷新乐观并发令牌：在事务内读取本次写入的 updated_at，
        // 避免下一次保存因令牌过期而误报并发冲突
        var refreshedResult = await _applicationService.GetByIdAsync(_applicationId, ct);
        if (!refreshedResult.IsSuccess)
        {
            throw new BusinessException(
                string.IsNullOrEmpty(refreshedResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : refreshedResult.ErrorCode,
                refreshedResult.Message ?? "读取保存结果失败");
        }
        if (refreshedResult.Value != null && refreshedResult.Value.UpdatedAt != default)
            _loadedUpdatedAt = refreshedResult.Value.UpdatedAt;
    }

    /// <summary>
    /// 同步渐退期记录到独立表 nc_biz_grace_periods（与主表同事务）
    /// M1：仅当本会话已判定/加载过渐退期状态（_gracePeriodEvaluated）才允许 Clear——
    /// 内存默认 false 不得误清服务端活动记录（户主死亡新建档等）。
    /// </summary>
    private async Task SyncGracePeriodRecordAsync(CancellationToken ct)
    {
        if (IsInGracePeriod)
        {
            var start = GracePeriodStartDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var months = GracePeriodMonths > 0 ? GracePeriodMonths : GracePeriodConstants.DEFAULT_MONTHS;
            var end = GracePeriodEndDate ?? start.AddMonths(months).AddDays(-1);

            var actRes = await _gracePeriodService.ActivateAsync(
                _applicationId, months, start, end,
                string.IsNullOrEmpty(OriginalClassificationResult) ? null : OriginalClassificationResult,
                OriginalGuaranteeAmount > 0 ? OriginalGuaranteeAmount : null,
                GraceGrantAmount,
                ct);
            if (!actRes.IsSuccess)
            {
                throw new BusinessException(
                    string.IsNullOrEmpty(actRes.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : actRes.ErrorCode,
                    actRes.Message ?? "渐退期记录保存失败");
            }
            _gracePeriodEvaluated = true;

            // 超限封顶产生减发 → 补写 FundChange，供月报「保障金减发表」捕获（幂等：同户同额只写一次）
            if (OriginalGuaranteeAmount is decimal orig && orig > 0
                && GraceGrantAmount is decimal grant && grant > 0
                && orig > grant)
            {
                await RecordGraceCapFundChangeAsync(orig, grant, ct);
            }
        }
        else if (_gracePeriodEvaluated)
        {
            var clrRes = await _gracePeriodService.ClearAsync(_applicationId, ct);
            if (!clrRes.IsSuccess)
            {
                throw new BusinessException(
                    string.IsNullOrEmpty(clrRes.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : clrRes.ErrorCode,
                    clrRes.Message ?? "渐退期记录清除失败");
            }
        }
    }

    /// <summary>
    /// 保存赡养人（失败抛 BusinessException，由外层统一事务回滚）
    /// </summary>
    private async Task SaveSupportersAsync(Dictionary<string, long> memberIdCardToNewId, CancellationToken ct)
    {
        var supporterService = _supporterService;

        // 用 SaveFamilyMembersAsync 返回的 IdCard→新ID 映射同步 Supporters 的 ID，
        // 不再查 DB——新 ID 已在内存中，直接使用。
        // 身份证无匹配的家庭成员不再移除：可能是 Step3 手动新增或用户改过身份证的独立赡养人
        //（Step2 删除赡养人时已在 RemoveFamilyMemberAsync 同步移除对应 Supporter），
        // 静默移除会丢数据（曾导致"部分数据保存失败"）。
        foreach (var supporter in Supporters.ToList())
        {
            if (string.IsNullOrWhiteSpace(supporter.IdCard)) continue;

            var idCard = supporter.IdCard.Trim();
            if (memberIdCardToNewId.TryGetValue(idCard, out var newId))
            {
                supporter.Id = newId;
            }
        }

        // 直接保存当前内存中的赡养人列表（含用户编辑的赡养费等字段）
        var supporters = Supporters.ToList();
        var result = await supporterService.SaveAsync(_applicationId, supporters, ct);
        if (!result.IsSuccess)
        {
            _logger.Error($"赡养人保存失败: {result.Message}");
            throw new BusinessException(
                string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                $"赡养人保存失败: {result.Message}");
        }
        _logger.LogBusiness("赡养人保存完成", ("Count", supporters.Count));
    }

    /// <summary>
    /// 保存入户调查数据
    /// </summary>
    private async Task SaveHouseholdSurveyAsync(CancellationToken ct)
    {
        // 入户调查无数据时仅跳过调查表本身；照料人必须无条件保存
        // （此前早退连带跳过照料人，特困申请只填照料人时会静默丢失）
        if (SurveyDate.HasValue || !string.IsNullOrWhiteSpace(SurveyorName))
        {
            var surveyService = _householdSurveyService;
            var survey = new HouseholdSurvey
            {
                ApplicationId = _applicationId,
                // 补全模式：入户调查日期强制取纳入时间（锁定值），防止绕过 UI 改动
                SurveyDate = _lockedSurveyDate ?? SurveyDate ?? DateTime.Today,
                SurveyorName = SurveyorName,
                SurveyorOrganization = SurveyorOrganization,
                RespondentName = RespondentName,
                RespondentRelation = RespondentRelation,
                ApplicationReason = SelectedApplicationReasonObj?.Key ?? "",
                ApplicationReasonDetail = ApplicationReasonDetail,
                SurveyConclusion = SurveyConclusion,
                SurveyNotes = SurveyNotes
            };
            var surveyResult = await surveyService.SaveAsync(_applicationId, survey, ct);
            if (!surveyResult.IsSuccess)
            {
                _logger.Error($"入户调查保存失败: {surveyResult.Message}");
                throw new BusinessException(
                    string.IsNullOrEmpty(surveyResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : surveyResult.ErrorCode,
                    $"入户调查保存失败: {surveyResult.Message}");
            }
        }

        // 保存照料人
        var caregiverService = _caregiverService;
        var caregiverResult = await caregiverService.SaveAsync(_applicationId, Caregivers.ToList(), ct);
        if (!caregiverResult.IsSuccess)
        {
            _logger.Error($"照料人保存失败: {caregiverResult.Message}");
            throw new BusinessException(
                string.IsNullOrEmpty(caregiverResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : caregiverResult.ErrorCode,
                $"照料人保存失败: {caregiverResult.Message}");
        }

        _logger.LogBusiness("入户调查+照料人保存完成");
    }

    /// <summary>
    /// 保存经济明细
    /// </summary>
    private async Task SaveEconomicDetailsAsync(CancellationToken ct)
    {
        var result = await _economicDetailService.SaveAllAsync(_applicationId,
            LaborIncomes.ToList(),
            BusinessIncomes.ToList(),
            PropertyIncomes.ToList(),
            TransferIncomes.ToList(),
            OtherIncomes.ToList(),
            Subsidies.ToList(),
            BreedingIncomes.ToList(),
            RigidExpenditures.ToList(),
            FamilyProperties.ToList(),
            Vehicles.ToList(),
            Machineries.ToList(),
            FinancialAssets.ToList(),
            LandRegistrations.ToList(),
            LandConfirmationGroups.ToList(),
            ct);

        if (result.IsSuccess)
        {
            _logger.LogBusiness("经济明细保存完成");

            // 同步更新 Application 表的收入汇总字段
            await UpdateApplicationIncomeAsync(ct);
        }
        else
        {
            _logger.Error($"经济明细保存失败: {result.Message}");
            // 注意：Result.ErrorCode 非 null（默认空串），必须用 IsNullOrEmpty 判断
            throw new BusinessException(
                string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                result.Message ?? "经济明细保存失败");
        }
    }

    /// <summary>
    /// 更新 Application 表的收入汇总字段（失败抛 BusinessException，由外层统一事务回滚；
    /// 此前失败被静默吞掉，会造成明细与汇总不一致）
    /// </summary>
    private async Task UpdateApplicationIncomeAsync(CancellationToken ct)
    {
        var request = new EconomicInfoUpdateRequest
        {
            ApplicationId = _applicationId,
            WageIncome = WorkIncomeTotal,
            BusinessIncome = BusinessIncomeTotal,
            PropertyIncome = PropertyIncomeTotal,
            TransferIncome = TransferIncomeTotal,
            OtherIncome = OtherIncomeTotal,
            SupportIncome = AlimonyIncome,
            RigidExpenditure = RigidExpenditure,
            LandIncome = LandIncomeTotal,
            SubsidyIncome = SubsidyTotal,
            TotalAnnualIncome = TotalAnnualIncome,
            PerCapitaAnnualIncome = PerCapitaAnnualIncome,
            FamilySize = FamilySize,
            UpdatedBy = App.CurrentUserId?.ToString() ?? "system"
        };

        var updateResult = await _applicationService.UpdateEconomicInfoAsync(request, ct);
        if (!updateResult.IsSuccess)
        {
            _logger.Error($"Application收入汇总更新失败: {updateResult.Message}");
            throw new BusinessException(
                string.IsNullOrEmpty(updateResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : updateResult.ErrorCode,
                $"收入汇总更新失败: {updateResult.Message}");
        }

        _logger.LogBusiness("Application收入汇总已更新",
            ("ApplicationId", _applicationId),
            ("TotalIncome", TotalFamilyIncome),
            ("PerCapitaIncome", PerCapitaIncome));
    }

    /// <summary>
    /// 保存家庭成员（先删除旧的，再重新插入）。
    /// 任一成员保存失败会聚合为一个 BusinessException 抛出，由外层统一事务回滚，
    /// 避免"删了旧成员、只插入了一半新成员"的中间状态被提交。
    /// </summary>
    private async Task<Dictionary<string, long>> SaveFamilyMembersAsync(CancellationToken ct)
    {
        var idCardToNewId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // ── 第1步：先软删该申请的全部现有成员（同事务，失败整体回滚，零数据丢失风险）──
        // 这样做解决"先插后删+AddAsync防重复校验"的逻辑死锁：
        // 先插旧记录未删 → COUNT(*) > 0 → 校验必失败 → 保存必回滚
        var deleteResult = await _familyMemberService.DeleteByApplicationIdAsync(_applicationId, ct);
        if (deleteResult.IsFailure)
        {
            _logger.Error($"删除旧家庭成员失败: {deleteResult.Message}");
            throw new BusinessException(
                string.IsNullOrEmpty(deleteResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : deleteResult.ErrorCode,
                $"删除旧家庭成员失败: {deleteResult.Message}");
        }
        if (deleteResult.Value > 0)
            _logger.LogBusiness("已软删旧家庭成员", ("DeletedCount", deleteResult.Value.ToString()));

        // ── 第2步：插入所有新家庭成员（旧记录已删，AddAsync 防重复校验自然通过）──
        // 防重复：同批表单内可能携带重复身份证（历史导入脏数据、用户重复录入），
        // 保留首个出现、跳过后续重复并记日志——否则 AddAsync 的批内防重会命中刚插入的
        // 兄弟行，把整单保存卡死在"该身份证号已在此申请中存在"。
        var seenIdCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedDuplicates = new List<string>();
        var savedCount = 0;
        var failures = new List<string>();
        foreach (var member in FamilyMembers)
        {
            if (string.IsNullOrWhiteSpace(member.Name) || string.IsNullOrWhiteSpace(member.IdCard))
                continue;

            var idCard = member.IdCard.Trim();
            if (!seenIdCards.Add(idCard))
            {
                var masked = DataMasker.MaskIdCard(idCard);
                _logger.Info($"跳过重复身份证成员: {DataMasker.MaskName(member.Name)}({masked})");
                skippedDuplicates.Add($"{DataMasker.MaskName(member.Name)}({masked})");
                continue;
            }

            var request = new FamilyMemberCreateRequest
            {
                ApplicationId = _applicationId,
                Name = member.Name,
                IdCard = member.IdCard,
                Relation = member.RelationshipToHead,
                Gender = member.Gender,
                Age = member.Age,
                Ethnicity = member.Ethnicity,
                Phone = member.Phone,
                HukouType = member.HukouType,
                HukouAddress = member.HukouAddress,
                HomeProvince = member.HomeProvince,
                HomeCity = member.HomeCity,
                HomeDistrict = member.HomeDistrict,
                HomeTown = member.HomeTown,
                HomeVillage = member.HomeVillage,
                HomeAddress = member.HomeAddress,
                HukouProvince = member.HukouProvince,
                HukouCity = member.HukouCity,
                HukouDistrict = member.HukouDistrict,
                HukouTown = member.HukouTown,
                MaritalStatus = member.MaritalStatus,
                EducationLevel = member.EducationLevel,
                PoliticalStatus = member.PoliticalStatus,
                HealthStatus = member.HealthStatus,
                HasSevereIllness = member.IsSevereDisability,
                HasDisability = member.IsDisabled,
                BirthDate = member.BirthDate,
                DisabilityType = member.DisabilityType,
                DisabilityLevel = member.DisabilityLevel,
                DisabilityCertificateNo = member.DisabilityCertificateNo,
                DiseaseCategory = member.DiseaseCategory,
                DiseaseName = member.DiseaseName,
                SecondaryDiseaseName = member.SecondaryDisease,
                DiseaseCode = member.DiseaseCode,
                IsSevereDisease = member.IsSevereDisease,
                IsLaborExempt = member.IsLaborExempt,
                IsHouseholdHead = member.IsHouseholdHead,
                MemberCategory = member.MemberCategory,
                EmploymentStatus = member.EmploymentStatus,
                WorkUnit = member.WorkUnit,
                MainIncomeSource = member.MainIncomeSource,
                AnnualIncome = member.AnnualIncome,
                WorkCapacity = member.WorkCapacity,
                MonthlyIncomeCapacity = member.MonthlyIncomeCapacity,
                FamilySize = member.FamilySize,
                PersonType = member.PersonType,
                AnnualSupportFee = member.AnnualSupportFee,
                MonthlySupportFee = member.MonthlySupportFee,
                SupportMonths = member.SupportMonths,
                IsSupportAbility = member.IsSupportAbility,
            };

            var result = await _familyMemberService.AddAsync(request, ct);
            if (result.IsSuccess)
            {
                savedCount++;
                // 收集 IdCard → 新 ID 映射，供 SaveSupportersAsync 使用（避免额外 DB 查询）
                if (result.Value > 0)
                    idCardToNewId[idCard] = result.Value;
            }
            else
            {
                _logger.Error($"保存家庭成员失败: {DataMasker.MaskName(member.Name)}, 错误: {result.Message}");
                failures.Add($"{member.Name}：{result.Message}");
            }
        }

        if (skippedDuplicates.Count > 0)
            _logger.LogBusiness("已跳过重复身份证成员", ("Skipped", string.Join("；", skippedDuplicates)));

        if (failures.Count > 0)
        {
            throw new BusinessException(ErrorCodes.DB_QUERY_ERROR,
                $"家庭成员保存失败（{failures.Count}/{FamilyMembers.Count}）：{string.Join("；", failures)}");
        }

        _logger.LogBusiness("家庭成员保存完成", ("Total", FamilyMembers.Count.ToString()), ("Saved", savedCount.ToString()));
        return idCardToNewId;
    }

    #endregion
}
