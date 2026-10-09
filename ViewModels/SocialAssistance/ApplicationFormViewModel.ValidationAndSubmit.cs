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

    #region 验证和保存

    protected override Task<Result> ValidateCurrentStepAsync()
    {
        return Task.FromResult(CurrentStep switch
        {
            1 => ValidateStep1(),
            2 => ValidateStep2(),
            3 => ValidateStep3(),
            4 => ValidateStep4(),
            5 => ValidateStep5(),
            _ => Result.Success()
        });
    }

    private Result ValidateStep1()
    {
        if (string.IsNullOrWhiteSpace(ApplicantName))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "申请人姓名不能为空");

        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "申请人身份证号不能为空");

        if (!Helpers.IdCardValidator.IsValid(ApplicantIdCard))
            return Result.Failure(ErrorCodes.INVALID_ID_CARD, "身份证号格式不正确");

        return Result.Success();
    }

    private Result ValidateStep2()
    {
        if (FamilySize < 1)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "家庭人数必须大于0");

        // 验证家庭成员
        foreach (var member in FamilyMembers)
        {
            if (string.IsNullOrWhiteSpace(member.Name))
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "家庭成员姓名不能为空");

            if (string.IsNullOrWhiteSpace(member.IdCard))
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"家庭成员 {member.Name} 的身份证号不能为空");

            if (!Helpers.IdCardValidator.IsValid(member.IdCard))
                return Result.Failure(ErrorCodes.INVALID_ID_CARD, $"家庭成员 {member.Name} 的身份证号格式不正确");

            if (string.IsNullOrWhiteSpace(member.RelationshipToHead))
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"家庭成员 {member.Name} 的与户主关系不能为空");
        }

        // 免于劳动力判定联动校验：任一成员勾选时，本户须存在重病或重残者（含户主本人）可供其照顾
        if (FamilyMembers.Any(m => m.IsLaborExempt))
        {
            var exemptNames = FamilyMembers.Where(m => m.IsLaborExempt)
                .Select(m => m.Name).ToList();

            // 照顾目标 = 户主本人（重病标记/重度残疾等级）+ 其他成员（重病/重残标记）
            bool headHasSevereCondition =
                IsSevereDisease   // 户主重病
                || (!string.IsNullOrEmpty(SelectedDisabilityLevelKey)
                    && _severeLevelKeys.Contains(SelectedDisabilityLevelKey));  // 户主重度残疾
            var hasCareTarget = headHasSevereCondition
                || FamilyMembers.Any(m =>
                    !m.IsLaborExempt && (m.IsSevereDisease || m.IsSevereDisability));
            if (!hasCareTarget)
            {
                var who = string.Join("、", exemptNames);
                return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                    $"家庭成员 [{who}] 勾选了\"因照顾重病重残亲属免于劳动力判定\"，但本户未勾选任何重病或重残成员，请先在其他成员的健康状况中如实标注");
            }
        }

        return Result.Success();
    }

    private Result ValidateStep3()
    {
        // 验证务工收入
        foreach (var income in LaborIncomes)
        {
            if (income.MonthlyIncome < 0)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "务工收入不能为负数");
        }

        // 验证经营收入
        foreach (var income in BusinessIncomes)
        {
            if (income.MonthlyIncome < 0)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "经营收入不能为负数");
        }

        // 验证刚性支出
        foreach (var expenditure in RigidExpenditures)
        {
            if (expenditure.Amount < 0)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "刚性支出不能为负数");
        }

        // 验证土地面积
        if (FamilyLandArea < 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "家庭土地面积不能为负数");

        return Result.Success();
    }

    private Result ValidateStep4()
    {
        // 验证调查日期
        if (!SurveyDate.HasValue)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请选择调查日期");

        // 验证调查员
        if (string.IsNullOrWhiteSpace(SurveyorName))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请填写调查员姓名");

        // 验证申请理由
        if (SelectedApplicationReasonObj == null)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请选择申请理由");

        // 验证申请原因详情
        if (string.IsNullOrWhiteSpace(ApplicationReasonDetail))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请填写申请原因详情");

        return Result.Success();
    }

    private Result ValidateStep5()
    {
        // 强校验：必须已进行分类判定
        if (string.IsNullOrEmpty(ClassificationResult))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请先进行分类判定");

        // 变更流程模式（复核/家庭修正/编辑家庭信息/成员变更）：必须在本次会话点过 Step5「分类判定」。
        // 分类/保障金只有判定按钮才落库（SaveClassificationResultAsync → UpdateAsync），
        // 跳过判定直接保存会让复核/变更结果丢失（只留变更记录、分类与保障金不落库），
        // 且 ChangeService 的"新旧对比"因缺少判定环节而无法判定是否需停旧建新。
        if (IsChangeMode && !IsClassificationDone)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                "请在 Step5 点击「分类判定」完成本次认定后再保存（变更流程必须以本次判定结果为准）");

        return Result.Success();
    }

    protected override Task<Result> ValidateAllStepsAsync()
    {
        var step1 = ValidateStep1();
        if (!step1.IsSuccess) return Task.FromResult(step1);

        var step2 = ValidateStep2();
        if (!step2.IsSuccess) return Task.FromResult(step2);

        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// 构建 Application 实体对象
    /// </summary>
    private Application BuildApplication(string status)
    {
        return new Application
        {
            Id = _applicationId,
            ApplicantName = ApplicantName,
            ApplicantIdCard = ApplicantIdCard,
            ApplicantPhone = ApplicantPhone,
            HukouType = HukouType?.Key ?? DefaultValuesConstants.HUKOU_TYPE_KEY,
            Gender = Gender,
            Province = SelectedProvince,
        City = SelectedCity,
            District = SelectedDistrict,
            Town = SelectedTown,
            Community = SelectedVillage,
            Address = Address,
            HukouAddress = HukouAddress,
            BankName = BankName?.Trim() ?? string.Empty,
            BankAccount = BankAccount?.Trim() ?? string.Empty,
            Ethnicity = SelectedEthnicity?.Key ?? DefaultValuesConstants.ETHNICITY_KEY,
            MaritalStatus = SelectedMaritalStatus?.Key ?? DefaultValuesConstants.MARITAL_STATUS_KEY,
            EducationLevel = SelectedEducationLevel?.Key ?? DefaultValuesConstants.EDUCATION_LEVEL_KEY,
            PoliticalStatus = SelectedPoliticalStatus?.Key ?? DefaultValuesConstants.POLITICAL_STATUS_KEY,
            DisabilityCardNo = DisabilityCardNo,
            DisabilityType = SelectedDisabilityTypeObj?.Key ?? "",
            DisabilityLevel = SelectedDisabilityLevelObj?.Key ?? "",
            HealthStatus = SelectedHealthStatusObj?.Key ?? DefaultValuesConstants.HEALTH_STATUS_KEY,
            DiseaseName = SelectedDiseaseCategoryObj?.Key ?? DefaultValuesConstants.DISEASE_CATEGORY_KEY,
            SecondaryDiseaseName = string.IsNullOrWhiteSpace(DiseaseNameText) ? (SelectedDiseaseNameObj?.Key ?? "") : DiseaseNameText,
            DiseaseCode = DiseaseCode ?? "",
            IsSevereDisease = IsSevereDisease,
            ApplicationReason = SelectedApplicationReasonObj?.Key ?? DefaultValuesConstants.APPLICATION_REASON_KEY,
            ApplicationReasonDetail = ApplicationReasonDetail,
            FamilySize = FamilySize,
            WorkIncomeTotal = WorkIncomeTotal,
            BusinessIncomeTotal = BusinessIncomeTotal,
            PropertyIncomeTotal = PropertyIncomeTotal,
            TransferIncomeTotal = TransferIncomeTotal,
            OtherIncomeTotal = OtherIncomeTotal,
            AlimonyIncome = AlimonyIncome,
            TotalFamilyIncome = TotalFamilyIncome,
            PerCapitaIncome = PerCapitaIncome,
            TotalAnnualIncome = TotalAnnualIncome,
            PerCapitaAnnualIncome = PerCapitaAnnualIncome,
            RigidExpenditure = RigidExpenditure,
            FamilyLandArea = FamilyLandArea,
            SelfFarmedLandArea = SelfFarmedLandArea,
            SubleasedLandArea = SubleasedLandArea,
            ContractedLandArea = ContractedLandArea,
            LandIncomeTotal = LandIncomeTotal,
            SubsidyTotal = SubsidyTotal,
            ClassificationResult = ClassificationResult,
            IsEligible = IsEligible,
            ClassifiedSubsidyType = ClassifiedSubsidyType,
            ClassifiedSubsidyAmount = ClassifiedSubsidyAmount,
            HouseholdMonthlyGuaranteeAmount = GuaranteeAmount,
            CaregiverSubsidyAmount = CaregiverSubsidyAmount,
            TotalGuaranteeAmount = TotalGuaranteeAmount,
            IsInGracePeriod = IsInGracePeriod,
            GracePeriodMonths = IsInGracePeriod ? GracePeriodMonths : null,
            GracePeriodStartDate = GracePeriodStartDate,
            GracePeriodEndDate = GracePeriodEndDate,
            // 变更链上游原分类优先（内存承载，主表列已删）；渐退确认后 VM 属性已有值时用 VM
            OriginalClassificationResult = _originalClassificationContext ?? OriginalClassificationResult,
            OriginalGuaranteeAmount = OriginalGuaranteeAmount,
            CaregiverType = CaregiverType,
            DestituteSupportType = DestituteSupportType,
            SupportInstitutionId = SelectedInstitution?.Id ?? _loadedSupportInstitutionId,
            SupportInstitutionName = SelectedInstitution?.Name ?? string.Empty,
            SupportInstitutionFee = SelectedInstitution?.TotalFee ?? 0,
            // 表单未编辑的主表字段：原样回写，避免保存时被默认值清零
            SupportMode = _loadedSupportMode,
            ConfirmedFamilySize = _loadedConfirmedFamilySize,
            PersonCategoryProtectionTotalAmount = _loadedPersonCategoryProtectionTotalAmount,
            IsSpecialApproval = _loadedIsSpecialApproval,
            SpecialApprovalId = _loadedSpecialApprovalId,
            // 补全模式保存时写回已建档标记（current_step=6），UI 归位5不落库；
            // 变更流程模式（复核/家庭修正/编辑家庭信息/成员变更）UI 步骤被强制归位（3/1/2），
            // 不代表档案真实进度 —— 回写加载时的库中步骤，否则已归档的 step=6 会被冲成 5，
            // 档案从「已完结档案」掉进「已建档未提交」。
            CurrentStep = IsCompletionMode ? 6
                : (IsChangeMode && _loadedCurrentStep > 0) ? _loadedCurrentStep
                : CurrentStep,
            Status = status,
            IsSingleRescue = IsSingleRescueApplication,
            UpdatedBy = "System",
            CreatedBy = OperationMode == FormOperationMode.Create ? "System" : null,
            // 乐观并发令牌约定：UpdatedAt 携带"加载时的 updated_at"；
            // default 表示无令牌（UpdateAsync 将跳过并发检查）
            UpdatedAt = _loadedUpdatedAt ?? default
        };
    }

    protected override async Task<Result> ExecuteSaveAsync()
    {
        // 查看模式禁止保存
        if (IsViewMode) return Result.Failure(ErrorCodes.VALIDATION_FAILED, "查看模式不可保存");

        var validation = ValidateStep1();
        if (!validation.IsSuccess) return validation;

        // 提交前强制 Step5 已分类判定（补全/成员变更走各自路径，不在这里拦）。
        // 复核模式必须拦：分类/保障金只有 Step5「分类判定」按钮才落库（SaveClassificationResultAsync），
        // 跳过判定直接保存会让复核结果丢失（只留变更记录、分类与保障金不落库）。
        if (!IsCompletionMode && !IsMemberChangeMode)
        {
            var step5 = ValidateStep5();
            if (!step5.IsSuccess) return step5;
        }

        // 补全模式：保存前提示是否仍有未补全的关键字段（不阻断，仅提醒）
        if (IsCompletionMode)
        {
            var reminder = BuildCompletionReminder();
            if (reminder != null)
            {
                var dialog = _serviceProvider.GetRequiredService<IDialogService>();
                var proceed = await dialog.DisplayAlertAsync("数据补全提醒", reminder, "继续保存", "返回补全");
                if (!proceed) return Result.Failure(ErrorCodes.VALIDATION_FAILED, "用户取消保存，返回继续补全");
            }
        }

        // 经济复核模式：走经济复核专用保存（重新判定 + 记录变更），完成后返回变更页
        // 家庭成员变更模式：走成员变更专用保存（成员增删落库 + 重新判定 + 停旧建新）
        // 家庭信息修正/编辑家庭信息模式走普通保存路径（直接更新原档案，不创建新档案）
        if (IsReviewMode)
        {
            return await ExecuteReviewSaveAsync();
        }
        if (IsMemberChangeMode)
        {
            return await ExecuteMemberChangeSaveAsync();
        }

        await ExecuteAsync(async ct =>
        {

            // 补全模式 / 经济复核（Review）/ 家庭修正/编辑家庭信息模式保存时保留原状态（Approved 等），其他模式按原逻辑保存为草稿
            var saveStatus = (IsCompletionMode || IsReviewMode || IsFamilyCorrectionMode || IsEditFamilyInfoMode)
                ? (string.IsNullOrEmpty(_originalStatus) ? ApplicationStatusCodes.APPROVED : _originalStatus)
                : ApplicationStatusCodes.DRAFT;
            await SaveApplicationInternalAsync(saveStatus, ct);
        }, "保存申请...");

        // 保存失败时不导航（ExecuteAsync 捕获异常后 ErrorMessage 非空）
        if (ErrorMessage != null) return Result.Failure(ErrorCodes.VALIDATION_FAILED, ErrorMessage);

        // 状态闭环：从资产核查任务进入并建档成功后，整户核查状态回写为 '2'（已建档），
        // 使工作流"有报告未建档"清单真实清零（失败容忍，不阻断保存流程）
        if (AssetCheckData != null && AssetCheckData.Id > 0 && _applicationId > 0)
        {
            try
            {
                // [CT 豁免] 保存收尾：核查状态闭环回写必须完成，不响应页面离开
                var statusResult = await _assetVerificationService.UpdateCheckStatusAsync(AssetCheckData.Id, AssetCheckStatusConstants.INCLUDED, CancellationToken.None);
                if (statusResult.IsSuccess)
                    _logger.LogBusiness("核查任务状态闭环: 已建档", ("AssetCheckId", AssetCheckData.Id), ("ApplicationId", _applicationId));
                else
                    _logger.Error($"回写核查状态'{AssetCheckStatusConstants.INCLUDED}'失败: {statusResult.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"回写核查状态'{AssetCheckStatusConstants.INCLUDED}'异常: {ex.Message}");
            }
        }

        // 保存成功后：补全模式返回上一页（变更页），其他模式导航到档案制作页面
        if (_applicationId > 0)
        {
            if (IsCompletionMode)
            {
                // 导入库建档档案补全保存后，标记已补全（此后经济复核进入复核模式，可重新判定分类）
                // [CT 豁免] 保存收尾：补全标记落库必须完成，不响应页面离开
                var markResult = await _applicationService.MarkDataCompletedAsync(_applicationId, CancellationToken.None);
                if (markResult.IsFailure)
                    _logger.Warn($"标记数据补全完成失败: {markResult.Message}");
                _logger.LogBusiness("数据补全保存完成，返回变更页", ("ApplicationId", _applicationId));

                // 数据补全并写入数据库后：删除对应的导入库家庭及人员行（防止重复建档/重复导入）
                // [CT 豁免] 保存收尾：删除导入库数据必须完成，不响应页面离开（中断会重新出现重复建档）
                var delResult = await _importedArchiveService.DeleteImportedFamilyByApplicationAsync(_applicationId, CancellationToken.None);
                if (delResult.IsFailure)
                    _logger.Warn($"删除导入库家庭失败: {delResult.Message}");

                var completionPage = Helpers.WindowNavigator.CurrentPage;
                if (completionPage != null)
                    await completionPage.Navigation.PopAsync();
                RestoreWindowTitleFromNavigation();
            }
            else if (IsInGracePeriod)
            {
                // 渐退期未满：提醒原/现保障金，然后进档案制作页（输出文书分类+预勾选，不进整档完成归档）
                await ShowGracePeriodSavedReminderAsync();
            }
            else
            {
                ClearDocumentOutputContext();
                await NavigateToArchiveProductionAsync();
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// 渐退封顶：原有享受额度 > 当前户口类型最低保障额×新家庭人数（不含分类施保）时，
    /// 渐退期内按上限发放；否则继续原额。原额优先取变更链上游档案（户主死亡停旧建新）。
    /// </summary>
    private async Task ApplyGraceCapAsync(decimal priorGuarantee, CancellationToken ct)
    {
        var original = _originalGuaranteeContext
            ?? (OriginalGuaranteeAmount is decimal og && og > 0 ? og : (decimal?)null)
            ?? (priorGuarantee > 0 ? priorGuarantee : (decimal?)null)
            ?? GuaranteeAmount;
        OriginalGuaranteeAmount = original;

        var standard = await GetCachedSubsistenceStandardAsync();
        if (standard <= 0 || FamilySize <= 0)
        {
            GraceGrantAmount = original;
            GuaranteeAmount = original;
            return;
        }

        var cap = standard * FamilySize;
        GraceGrantAmount = original > cap ? cap : original;
        // 渐退期内按原享受额（封顶后）发放，不按本次补差公式重算
        GuaranteeAmount = GraceGrantAmount.Value;

        _logger.LogBusiness("渐退封顶",
            ("原保障金", original),
            ("上限", cap),
            ("应发", GraceGrantAmount),
            ("人数", FamilySize));
    }

    /// <summary>
    /// 保存成功且处于渐退期：提醒原/现保障金与减发是否进月报，然后预置输出文书上下文并进档案制作页。
    /// </summary>
    private async Task ShowGracePeriodSavedReminderAsync()
    {
        var period = GracePeriodEndDate?.ToString("yyyy-MM-dd") ?? "—";
        var original = OriginalGuaranteeAmount ?? 0;
        var current = GraceGrantAmount ?? GuaranteeAmount;
        var reduce = original - current;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"已进入渐退期（{GracePeriodMonths} 个月，至 {period}）");
        sb.AppendLine($"原保障金：{original:F2} 元/月");
        if (reduce > 0)
        {
            sb.AppendLine($"现保障金：{current:F2} 元/月（超过本户口类型上限，已封顶）");
            sb.AppendLine($"减发金额：{reduce:F2} 元/月");
            sb.AppendLine("该减发将计入「保障金减发表」（月报）。");
        }
        else
        {
            sb.AppendLine($"现保障金：{current:F2} 元/月（与原额一致，无减发）");
        }
        sb.AppendLine("档案制作须待渐退期满后办理；可先在制作页「档案输出」打印所需文书。");

        var dialog = _serviceProvider.GetRequiredService<IDialogService>();
        await dialog.DisplayAlertAsync("渐退期确认", sb.ToString(), "确定");
        await OpenDocumentProductionAsync();
    }

    /// <summary>
    /// 预置输出文书上下文（OutputCategories/Prefilter/OperationOverride）并进档案制作页。
    /// 用户在制作页点「档案输出」→ Output 按变动分类加载并预勾选。
    /// 预勾选：告知书必选；有人员变动→增减员表；渐退→渐退审批表；有减发→保障金减少。
    /// </summary>
    [RelayCommand]
    private async Task ShowGraceDocumentSheetAsync()
    {
        if (_applicationId <= 0) return;
        await OpenDocumentProductionAsync();
    }

    private async Task OpenDocumentProductionAsync()
    {
        try
        {
            IsBusy = true;
            LoadingMessage = "正在准备文书输出...";

            // A1+：出文书前再同步一次——「只出文书不保存」也不带病出单
            if (IsInGracePeriod && GracePeriodMonths > 0)
                await SyncGracePeriodRecordAsync(CancellationToken);

            var preselect = new List<string> { DocumentTemplateNames.ChangeNotice }; // 告知书必选

            // 渐退核定 → 渐退审批表
            if (IsInGracePeriod)
                preselect.Add(DocumentTemplateNames.GraceApproval);

            // 有减发 → 保障金减少
            if (OriginalGuaranteeAmount is decimal og && GraceGrantAmount is decimal gg && og > gg && gg > 0)
                preselect.Add(DocumentTemplateNames.GrantReduce);

            // 有人员变动 → 增减员表（死亡链/成员增减记录存在）
            try
            {
                var changes = await _changeService.GetMemberChangeRecordsAsync(_applicationId, 5, CancellationToken);
                if (changes.IsSuccess && changes.Value is { Count: > 0 })
                    preselect.Add(DocumentTemplateNames.MemberChangeTable);
            }
            catch
            {
                // 取数失败不阻断：仅少预勾一张
            }

            ApplyDocumentOutputContext(preselect);
            await NavigateToArchiveProductionAsync();
        }
        catch (Exception ex)
        {
            // 导航失败：清文书模式上下文（ApplyDocumentOutputContext 已写入），防静态残留
            ClearDocumentOutputContext();
            _logger.LogError(ex, "打开文书输出失败");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"打开文书输出失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>写入输出文书上下文（进 Production 前调用；PrepareAndNavigateAsync 不会清这些字段）</summary>
    private void ApplyDocumentOutputContext(IEnumerable<string> preselectNames)
    {
        ClearDocumentOutputContext();
        PrintNavigationData.OutputCategories = Helpers.ArchiveCategoryResolver.DocumentOperationCategories;
        PrintNavigationData.OperationOverride = "人员变更";
        PrintNavigationData.PrefilterTemplateNames = preselectNames
            .Distinct(StringComparer.Ordinal).ToArray();
        PrintNavigationData.TemplateFilter = null; // 分类+预勾选，非强白名单
    }

    /// <summary>清理输出文书上下文（整档路径进入 Production 前调用）</summary>
    private void ClearDocumentOutputContext()
    {
        PrintNavigationData.OutputCategories = null;
        PrintNavigationData.OperationOverride = null;
        PrintNavigationData.PrefilterTemplateNames = null;
        PrintNavigationData.TemplateFilter = null;
    }

    /// <summary>
    /// 装配打印字段并进入档案输出页（保留：强白名单旁路，成员变更等若需直出仍可用）。
    /// 渐退/Step5 已改为 OpenDocumentProductionAsync → Production → Output。
    /// </summary>
    private async Task OpenDocumentOutputAsync(IReadOnlyList<string> templateNames, long applicationId)
    {
        if (applicationId <= 0 || templateNames == null || templateNames.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", "文书直出参数无效", "确定");
            return;
        }

        await _documentBuildGate.WaitAsync(CancellationToken);
        try
        {
            IsBusy = true;
            LoadingMessage = "正在装配文书字段...";

            // ArchiveProductionViewModel 为 Transient 且 BuildPrintDataAsync 使用实例内部字段状态（不可并发）——
            // 每次调用 DI 解析局部实例 + 静态 SemaphoreSlim 串行
            var production = _serviceProvider.GetRequiredService<ViewModels.ArchiveManagement.ArchiveProductionViewModel>();
            var printData = await production.BuildPrintDataAsync(applicationId);
            if (printData == null)
                throw new InvalidOperationException("文书字段装配失败，请确认档案数据完整");

            // 先清文书模式上下文（OutputCategories/OperationOverride/Prefilter 残留会污染白名单的候选分类集），下方再写 TemplateFilter
            PrintNavigationData.ClearDocumentMode();
            PrintNavigationData.BusinessType = string.IsNullOrEmpty(printData.BusinessType)
                ? "FamilyApplication"
                : printData.BusinessType;
            PrintNavigationData.BusinessId = printData.BusinessId ?? applicationId;
            PrintNavigationData.Classification = printData.Classification;
            PrintNavigationData.FieldData = new Dictionary<string, string>(printData.FieldData, StringComparer.Ordinal);
            PrintNavigationData.TableData = printData.TableData?.ToList() ?? new();
            PrintNavigationData.SupporterTableData = printData.SupporterTableData;
            PrintNavigationData.Status = ApplicationStatus ?? string.Empty;
            PrintNavigationData.TemplateFilter = templateNames.ToArray();

            _logger.Info($"文书直出: ApplicationId={applicationId}, 模板={string.Join("|", templateNames)}");

            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
        }
        catch (Exception ex)
        {
            // 导航失败时清 PII，避免驻留
            PrintNavigationData.Clear();
            _logger.LogError(ex, "文书直出打开档案输出页失败");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"打开文书输出失败: {ex.Message}", "确定");
        }
        finally
        {
            _documentBuildGate.Release();
            IsBusy = false;
        }
    }

    /// <summary>
    /// 渐退超限减发：补写 change_type=FundChange（old&gt;new、同分类），供月报保障金减发表捕获。
    /// 幂等：同户同额已存在则跳过；本会话已写过也跳过。
    /// </summary>
    private async Task RecordGraceCapFundChangeAsync(decimal oldAmount, decimal newAmount, CancellationToken ct)
    {
        if (_graceFundChangeRecorded) return;

        try
        {
            var result = await _changeService.CreateGraceCapFundChangeAsync(
                _applicationId,
                ClassificationResult ?? string.Empty,
                OriginalClassificationResult ?? ClassificationResult ?? string.Empty,
                oldAmount,
                newAmount,
                ct);
            if (result.IsSuccess)
            {
                _graceFundChangeRecorded = true;
                _logger.LogBusiness("渐退超限减发已记入FundChange",
                    ("ApplicationId", _applicationId),
                    ("原保障金", oldAmount),
                    ("现保障金", newAmount));
            }
            else
            {
                _logger.Warn($"渐退超限FundChange写入失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"渐退超限FundChange写入异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 补全模式下检查是否仍有未补全的关键字段，返回提醒文案；全部补全返回 null
    /// </summary>
    private string? BuildCompletionReminder()
    {
        var missing = new List<string>();

        // 户主基本信息（值为空才算未补全，导入库已带出的默认值不算缺失）
        if (SelectedEthnicity == null || string.IsNullOrWhiteSpace(SelectedEthnicity.Key))
            missing.Add("民族");
        if (SelectedHealthStatusObj == null || string.IsNullOrWhiteSpace(SelectedHealthStatusObj.Key))
            missing.Add("健康状况");

        // 经济分项（务工收入代表）
        if (WorkIncomeTotal <= 0 && BusinessIncomeTotal <= 0 && TransferIncomeTotal <= 0 && OtherIncomeTotal <= 0)
            missing.Add("经济分项收入");

        // 地区（乡镇/社区）
        if (string.IsNullOrWhiteSpace(SelectedTown) || string.IsNullOrWhiteSpace(SelectedVillage))
            missing.Add("地区（乡镇/村）");

        if (missing.Count == 0) return null;

        return $"以下关键信息尚未补全：{string.Join("、", missing)}。\n\n可点击「返回补全」继续完善，或「继续保存」暂时保留当前数据。";
    }

    /// <summary>
    /// 经济复核专用保存：构建复核上下文 → 执行重新判定并记录变更 → 返回变更页
    /// </summary>
    private async Task<Result> ExecuteReviewSaveAsync()
    {
        // 复核原因必填
        if (string.IsNullOrWhiteSpace(ApplicationReasonDetail))
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请填写复核原因", "确定");
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "复核原因不能为空");
        }

        try
        {
            IsBusy = true;
            LoadingMessage = "经济状况复核中...";

            _logger.LogBusiness("开始经济状况复核", ("ApplicationId", _applicationId));

            // ── 覆写前旧值：优先用向导入口固化的复核前快照 ──
            // 步骤保存/Step5 判定落库在进入本方法前已把新数据写回本档，此时再读库
            // 拿到的"旧值"已是覆写后值（Before 快照 / old_per_capita_income 失真）。
            // 入口快照缺失（未走加载路径的异常场景）才回退保存时读库（历史行为），并记 Warn。
            Application? oldSnapshot = _entryApplicationSnapshot;
            if (oldSnapshot == null)
            {
                var oldSnapshotResult = await _applicationService.GetByIdAsync(_applicationId, CancellationToken);
                if (oldSnapshotResult.IsSuccess && oldSnapshotResult.Value != null)
                {
                    oldSnapshot = oldSnapshotResult.Value;
                }
                else
                {
                    _logger.Warn($"经济复核-覆写前旧值捕获失败: {oldSnapshotResult.Message}");
                }
            }

            // 经济复核模式下 Step3 经济明细可编辑，先持久化用户修改，再执行分类重新判定。
            // 否则用户在复核模式下修改的经济数据（务工/经营/补贴等）不会被保存。
            try
            {
                await SaveEconomicDetailsAsync(CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"经济复核-保存经济明细失败: {ex.Message}");
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", $"经济明细保存失败：{ex.Message}", "确定");
                return Result.Failure(ErrorCodes.DB_QUERY_ERROR, $"经济明细保存失败：{ex.Message}");
            }

            var changeService = _changeService;
            var context = new EconomicReviewContext
            {
                ApplicationId = _applicationId,
                NewTotalFamilyIncome = TotalFamilyIncome,
                NewPerCapitaIncome = PerCapitaIncome,
                NewTotalAnnualIncome = TotalAnnualIncome,
                NewPerCapitaAnnualIncome = PerCapitaAnnualIncome,
                NewRigidExpenditure = RigidExpenditure,
                NewFamilySize = FamilySize,
                ReviewReason = ApplicationReasonDetail,
                OperatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName,
                IsFamilyCorrection = IsFamilyCorrectionMode,
                // 覆写前捕获的真旧值（未捕获到为 null，ChangeService 回退读库）
                OldFamilySize = oldSnapshot?.FamilySize,
                OldTotalFamilyIncome = oldSnapshot?.TotalFamilyIncome,
                OldTotalAnnualIncome = oldSnapshot?.TotalAnnualIncome,
                OldPerCapitaIncome = oldSnapshot?.PerCapitaIncome,
                OldRigidExpenditure = oldSnapshot?.RigidExpenditure,
                // 分类/保障金用加载时刻的 _loaded*（与入口快照同一时刻捕获，口径一致；
                // Step5 判定落库会覆写 classification_result/total_guarantee_amount，读库会恒判"无变化"）
                OldClassification = _loadedClassification,
                OldGuaranteeAmount = _loadedTotalGuaranteeAmount,
                OldComponents = oldSnapshot == null ? null : new IncomeComponentValues
                {
                    WorkIncomeTotal = oldSnapshot.WorkIncomeTotal,
                    BusinessIncomeTotal = oldSnapshot.BusinessIncomeTotal,
                    PropertyIncomeTotal = oldSnapshot.PropertyIncomeTotal,
                    TransferIncomeTotal = oldSnapshot.TransferIncomeTotal,
                    OtherIncomeTotal = oldSnapshot.OtherIncomeTotal,
                    RigidExpenditure = oldSnapshot.RigidExpenditure,
                    AlimonyIncome = oldSnapshot.AlimonyIncome,
                    LandIncomeTotal = oldSnapshot.LandIncomeTotal,
                    SubsidyTotal = oldSnapshot.SubsidyTotal
                }
            };

            var result = await changeService.ExecuteEconomicReviewAsync(context, CancellationToken);
            if (result.IsFailure || !result.IsSuccess)
            {
                ErrorMessage = result.Message ?? "经济状况复核失败";
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", ErrorMessage, "确定");
                return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, ErrorMessage);
            }

            // 复核完成后返回变更页，出口三选一：整套档案 / 仅出文书（渐退减发）/ 保持整档
            _logger.LogBusiness("经济状况复核完成",
                ("ApplicationId", _applicationId),
                ("OldClassification", result.Value.OldClassification),
                ("NewClassification", result.Value.NewClassification),
                ("OldAmount", result.Value.OldGuaranteeAmount),
                ("NewAmount", result.Value.NewGuaranteeAmount));

            var reviewPage = Helpers.WindowNavigator.CurrentPage;
            if (reviewPage != null)
                await reviewPage.Navigation.PopAsync();

            // 未生成新档案（保障金额/分类/停保三项均未变化）：提示后停在变更页，不进档案制作
            if (!result.Value.Rebuilt)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", "复核完成，保障金额无变化，未生成新档案", "确定");
                return Result.Success();
            }

            // 渐退/超限减发 → 仅出文书（档案制作+输出，预勾选）；否则保持整档 ArchiveProduction
            var hasGraceReduce = IsInGracePeriod
                || (OriginalGuaranteeAmount is decimal ro && GraceGrantAmount is decimal rg && ro > rg);
            if (hasGraceReduce)
            {
                var docs = new List<string> { DocumentTemplateNames.ChangeNotice };
                if (IsInGracePeriod)
                    docs.Add(DocumentTemplateNames.GraceApproval);
                if (OriginalGuaranteeAmount is decimal o2 && GraceGrantAmount is decimal g2 && o2 > g2)
                    docs.Add(DocumentTemplateNames.GrantReduce);
                try
                {
                    ApplyDocumentOutputContext(docs);
                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                        new ApplicationReviewArchiveParameter(_applicationId, null, "经济复核"));
                }
                catch (Exception navEx)
                {
                    _logger.Error($"导航到渐退文书输出失败: {navEx.Message}");
                }
                return Result.Success();
            }

            try
            {
                ClearDocumentOutputContext();
                // 参数先于 Push 注入，替代原"Push 后 InitializeFromApplicationReviewAsync"时序
                await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(new ApplicationReviewArchiveParameter(_applicationId));
            }
            catch (Exception navEx)
            {
                _logger.Error($"导航到经济复核档案输出失败: {navEx.Message}\n{navEx.StackTrace}");
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", $"经济状况复核失败：{ex.Message}", "确定");
            return Result.FromException(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 家庭成员变更专用保存：成员增删/赡养人/经济明细落库 → 重新判定并停旧建新 → 返回变更页并进入档案输出。
    /// 成员、赡养人、经济明细与变更服务共用同一环境事务：任一步失败整体回滚，
    /// 避免"成员已改、分类未重算"的中间状态入库。
    /// </summary>
    private async Task<Result> ExecuteMemberChangeSaveAsync()
    {
        // 变更原因必填（复用表单"原因详情"，写入变更记录/增减员调整表输出）
        if (string.IsNullOrWhiteSpace(ApplicationReasonDetail))
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请填写变更原因（入户调查步骤的\"原因详情\"）", "确定");
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "变更原因不能为空");
        }

        IsBusy = true;
        LoadingMessage = "家庭成员变更处理中...";

        try
        {
            _logger.LogBusiness("开始家庭成员变更", ("ApplicationId", _applicationId));

            // 强制重新计算收入，确保汇总字段是最新的
            RecalculateIncome();

            // 变更类型与摘要：加载时名单 vs 当前名单（增员/减员/既有增又有减）
            var loadedIdCardSet = new HashSet<string>(_loadedMembersByIdCard.Keys, StringComparer.OrdinalIgnoreCase);
            var currentMembers = FamilyMembers
                .Where(m => !string.IsNullOrWhiteSpace(m.IdCard))
                .GroupBy(m => m.IdCard.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var addedMembers = currentMembers.Values
                .Where(m => !loadedIdCardSet.Contains(m.IdCard!.Trim()))
                .ToList();
            // 减员以登记记录为准（身份证被重新加回的不算减员）
            var removedEntries = _removedMemberEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.Member.IdCard)
                    && !currentMembers.ContainsKey(e.Member.IdCard.Trim()))
                .ToList();

            // 新增行必须填全姓名/身份证号：否则会被保存静默跳过，增员登记与人数都会丢
            var blankAdded = FamilyMembers
                .Where(m => !_loadedMemberEntities.Contains(m, ReferenceEqualityComparer.Instance))
                .Where(m => string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.IdCard))
                .ToList();
            if (blankAdded.Count > 0)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", $"有 {blankAdded.Count} 名新增成员未填写姓名或身份证号，请补全后再提交", "确定");
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "新增成员信息不完整");
            }

            // 增员/减员必须逐人登记原因（弹窗登记；此处兜底校验，防止旁路修改列表）
            var missingReason = addedMembers.Where(m => !_addedMemberReasons.ContainsKey(m)).Select(m => m.Name).ToList();
            missingReason.AddRange(removedEntries.Where(e => e.Reason == null).Select(e => e.Member.Name));
            if (missingReason.Count > 0)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", $"以下成员缺少变更原因登记：{string.Join("、", missingReason)}", "确定");
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "成员变更原因未登记");
            }

            var changeType = addedMembers.Count > 0 && removedEntries.Count == 0
                ? DictionaryConstants.ChangeType.MEMBER_ADD
                : removedEntries.Count > 0 && addedMembers.Count == 0
                    ? DictionaryConstants.ChangeType.MEMBER_REMOVE
                    : DictionaryConstants.ChangeType.MEMBER_MODIFY;
            var summaryParts = new List<string>();
            if (addedMembers.Count > 0)
            {
                summaryParts.Add($"新增 {string.Join("、", addedMembers.Select(m => $"{m.Name}（{_addedMemberReasons[m].ReasonName}）"))}");
            }
            if (removedEntries.Count > 0)
            {
                summaryParts.Add($"减员 {string.Join("、", removedEntries.Select(e => $"{e.Member.Name}（{e.Reason.ReasonName}）"))}");
            }
            var changeSummary = string.Join("；", summaryParts);

            // 无增员/减员不允许提交：否则会无意义地停旧建新
            if (addedMembers.Count == 0 && removedEntries.Count == 0)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", "未检测到家庭成员变化（增员或减员），无需执行成员变更", "确定");
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "未检测到家庭成员变化");
            }

            // 逐人变更明细（快照 + nc_biz_change_details；死亡减员联动死亡记录）
            var changeEntries = new List<MemberChangeEntry>();
            changeEntries.AddRange(addedMembers.Select(m =>
            {
                var r = _addedMemberReasons[m];
                return new MemberChangeEntry
                {
                    Direction = DictionaryConstants.ChangeType.MEMBER_ADD,
                    MemberId = m.Id,
                    Name = m.Name ?? string.Empty,
                    IdCard = m.IdCard ?? string.Empty,
                    RelationshipToHead = m.RelationshipToHead ?? string.Empty,
                    MemberCategory = m.MemberCategory ?? string.Empty,
                    ReasonCode = r.ReasonCode,
                    ReasonName = r.ReasonName,
                    EventDate = r.EventDate,
                    Remark = r.Remark
                };
            }));
            changeEntries.AddRange(removedEntries.Select(e => new MemberChangeEntry
            {
                Direction = DictionaryConstants.ChangeType.MEMBER_REMOVE,
                MemberId = e.Member.Id,
                Name = e.Member.Name ?? string.Empty,
                IdCard = e.Member.IdCard ?? string.Empty,
                RelationshipToHead = e.Member.RelationshipToHead ?? string.Empty,
                MemberCategory = e.Member.MemberCategory ?? string.Empty,
                ReasonCode = e.Reason.ReasonCode,
                ReasonName = e.Reason.ReasonName,
                EventDate = e.Reason.EventDate,
                Remark = e.Reason.Remark
            }));

            ChangeResult? changeValue = null;
            var changeResult = await _applicationService.ExecuteInTransactionForResultAsync(async transactionCt =>
            {
                // 1) 成员增删/赡养人/经济明细落库（同一环境事务，失败整体回滚）
                await MergeSupportMembersIntoSupportersAsync();
                var memberIdCardToNewId = await SaveFamilyMembersAsync(transactionCt);
                await SaveSupportersAsync(memberIdCardToNewId, transactionCt);
                await SaveEconomicDetailsAsync(transactionCt);

                // 2) 重新判定分类 + 停旧建新 + 变更记录（含逐人明细/死亡减员联动）
                var context = new MemberChangeContext
                {
                    ApplicationId = _applicationId,
                    ChangeType = changeType,
                    ChangeSummary = changeSummary,
                    ChangeReason = ApplicationReasonDetail,
                    OldFamilySize = _loadedFamilySize,
                    OldClassification = _loadedClassification,
                    OldGuaranteeAmount = _loadedTotalGuaranteeAmount,
                    // 复核前真旧值（入口快照）：本事务 SaveEconomicDetailsAsync 已先写回新收入，
                    // 读库拿到的是覆写后值（Before 快照 / old_per_capita_income 失真）；null 回退读库
                    OldTotalFamilyIncome = _entryApplicationSnapshot?.TotalFamilyIncome,
                    OldPerCapitaIncome = _entryApplicationSnapshot?.PerCapitaIncome,
                    OldRigidExpenditure = _entryApplicationSnapshot?.RigidExpenditure,
                    NewTotalFamilyIncome = TotalFamilyIncome,
                    NewPerCapitaIncome = PerCapitaIncome,
                    NewTotalAnnualIncome = TotalAnnualIncome,
                    NewPerCapitaAnnualIncome = PerCapitaAnnualIncome,
                    NewRigidExpenditure = RigidExpenditure,
                    NewFamilySize = FamilySize,
                    Entries = changeEntries,
                    OperatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName
                };

                var result = await _changeService.ExecuteMemberChangeAsync(context, transactionCt);
                if (result.IsFailure || !result.IsSuccess)
                {
                    ErrorMessage = result.Message ?? "家庭成员变更失败";
                    return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, ErrorMessage);
                }

                changeValue = result.Value;
                return Result.Success();
            }, CancellationToken);

            if (changeResult.IsFailure)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", ErrorMessage ?? "家庭成员变更失败", "确定");
                return changeResult;
            }

            _logger.LogBusiness("家庭成员变更完成",
                ("ApplicationId", _applicationId),
                ("ChangeType", changeType),
                ("OldClassification", changeValue!.OldClassification),
                ("NewClassification", changeValue.NewClassification),
                ("NewApplicationId", changeValue.NewApplicationId));

            // 返回变更页，出口三选一：整套档案 / 仅出文书 / 稍后
            var navigationPage = Helpers.WindowNavigator.CurrentPage;
            if (navigationPage != null)
                await navigationPage.Navigation.PopAsync();

            var choice = await _serviceProvider.GetRequiredService<IDialogService>().DisplayActionSheetAsync(
                "成员变更完成",
                "取消",
                null,
                "整套档案",
                "仅出文书",
                "稍后再说");

            if (choice == "整套档案")
            {
                ClearDocumentOutputContext();
                try
                {
                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                        new ApplicationReviewArchiveParameter(changeValue.NewApplicationId, _applicationId, "成员变更"));
                }
                catch (Exception navEx)
                {
                    _logger.Error($"导航到家庭成员变更档案输出失败: {navEx.Message}\n{navEx.StackTrace}");
                }
            }
            else if (choice == "仅出文书")
            {
                try
                {
                    ApplyDocumentOutputContext(new[]
                    {
                        DocumentTemplateNames.MemberChangeTable,
                        DocumentTemplateNames.ChangeNotice
                    });
                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                        new ApplicationReviewArchiveParameter(changeValue.NewApplicationId, _applicationId, "成员变更"));
                }
                catch (Exception navEx)
                {
                    _logger.Error($"导航到成员变更文书输出失败: {navEx.Message}");
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", $"家庭成员变更失败：{ex.Message}", "确定");
            return Result.FromException(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected override async Task OnCancelAsync()
    {
        _logger.LogBusiness("取消编辑申请");
        await Task.CompletedTask;
    }

    #endregion

    #region 放弃申请

    /// <summary>
    /// 放弃申请：五步表单第一步原本没有任何退出途径（上一步在首步隐藏），
    /// 此命令提供带确认的放弃入口——确认后直接返回上一页，不保存已录入内容。
    /// </summary>
    [RelayCommand]
    private async Task AbandonAsync()
    {
        var dialog = _serviceProvider.GetRequiredService<IDialogService>();
        var confirm = await dialog.DisplayAlertAsync(
            "放弃申请",
            "确定要放弃本次操作吗？\n已录入但未保存的信息将会丢失。",
            "放弃并返回", "继续填写");
        if (!confirm)
            return;

        await GoBackAsync();
    }

    #endregion
}
