using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.SpecialApproval;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.ArchiveManagement;

public partial class ArchiveProductionViewModel
{
    private Task<bool> LoadMonthlyReportDataAsync(long? businessId)
    {
        if (!businessId.HasValue) return Task.FromResult(false);

        var yearMonth = businessId.Value.ToString();
        if (yearMonth.Length != 6) return Task.FromResult(false);

        var year = int.Parse(yearMonth[..4]);
        var month = int.Parse(yearMonth[4..]);

        _currentBusinessType = "AssetVerificationMonthlyReport";
        _currentBusinessId = businessId;
        _currentClassification = ClassificationConstants.AssetVerification;

        BusinessTypeDisplay = "经济核对月报表";
        DataSourceType = "经济核对月报表";
        ApplicantName = $"{year}年{month}月";
        ApplicantIdCard = "-";
        FamilyInfo = "月度汇总";
        HasBusinessData = true;
        IsReady = true;
        StatusText = $"已加载 {year}年{month}月报表";

        _fieldData = new Dictionary<string, string>
        {
            ["REPORT_PERIOD"] = $"{year}年{month}月",
            ["YEAR"] = year.ToString(),
            ["MONTH"] = month.ToString(),
            ["GENERATED_AT"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["GENERATED_BY"] = App.CurrentUserName ?? "System"
        };

        _tableData = new List<Dictionary<string, string>>();

        SidebarGroups.Clear();
        AutoFilledCount = 0;
        SidebarTotalCount = 0;

        return Task.FromResult(true);
    }

    private Task<bool> LoadGenericDataAsync(string businessType, long? businessId)
    {
        _currentBusinessType = businessType;
        _currentBusinessId = businessId;
        _currentClassification = businessType;

        BusinessTypeDisplay = OutputPathHelper.GetCategoryDisplayName(businessType);
        DataSourceType = BusinessTypeDisplay;
        ApplicantName = businessId?.ToString() ?? "-";
        ApplicantIdCard = "-";
        FamilyInfo = "-";
        HasBusinessData = true;
        IsReady = true;
        StatusText = $"已加载 {BusinessTypeDisplay}";

        _fieldData = new Dictionary<string, string>
        {
            ["BUSINESS_TYPE"] = businessType,
            ["BUSINESS_ID"] = businessId?.ToString() ?? ""
        };

        _tableData = new List<Dictionary<string, string>>();

        SidebarGroups.Clear();
        AutoFilledCount = 0;
        SidebarTotalCount = 0;

        return Task.FromResult(true);
    }

    // ========================
    //  侧边栏构建 + 自动填充
    // ========================

    private void BuildSidebarFromAssetCheck(AssetVerificationDetail detail, string fullAddress, string civilAssistantName, OrganizationInfoDto orgInfo)
    {
        SidebarGroups.Clear();
        AutoFilledCount = 0;

        var fieldMapping = new Dictionary<string, string>
        {
            ["申请人姓名"] = FieldKeys.APPLICANT_NAME,
            ["身份证号"] = FieldKeys.APPLICANT_ID_CARD,
            ["证件类型"] = FieldKeys.APPLICANT_ID_TYPE,
            ["联系电话"] = FieldKeys.CONTACT_PHONE,
            ["家庭地址"] = FieldKeys.FAMILY_ADDRESS,
            ["社区"] = FieldKeys.COMMUNITY,
            ["申请原因"] = FieldKeys.APPLICATION_REASON,
            ["申请日期"] = FieldKeys.APPLICATION_DATE,
            ["经办人"] = FieldKeys.OPERATOR_NAME,
            ["经办单位"] = FieldKeys.OPERATOR_UNIT,
            ["代理人姓名"] = FieldKeys.AGENT_NAME,
            ["代理人身份证"] = FieldKeys.AGENT_ID_CARD,
            ["代理关系"] = FieldKeys.AGENT_RELATION,
            ["代理人证件类型"] = FieldKeys.AGENT_CERT_TYPE,
            ["是否委托代理"] = FieldKeys.IS_AGENT,
            ["区县"] = FieldKeys.DISTRICT,
            ["乡镇"] = FieldKeys.TOWN,
            ["民政助理"] = FieldKeys.CIVIL_ASSISTANT_NAME,
            ["区县民政局"] = FieldKeys.DISTRICT_CIVIL_BUREAU,
            ["区县民政局电话"] = FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE,
            ["经办单位电话"] = FieldKeys.OPERATOR_UNIT_PHONE
        };

        // 基本信息组
        var basicGroup = new SidebarGroup("基本信息");
        basicGroup.Items.Add(CreateSidebarItem("申请人姓名", detail.ApplicantName, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("身份证号", detail.ApplicantIdCard, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("证件类型", GetIdTypeDisplay(detail.ApplicantIdType), fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("联系电话", detail.ContactPhone, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("申请原因", GetApplicationReasonDisplay(detail.ApplicationReason), fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("申请日期", detail.ApplicationDate.ToString("yyyy-MM-dd"), fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("核查状态", detail.StatusDisplay, fieldMapping));
        SidebarGroups.Add(basicGroup);

        // 地址信息组
        var addressGroup = new SidebarGroup("地址信息");
        addressGroup.Items.Add(CreateSidebarItem("家庭地址", fullAddress, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("社区", detail.Community, fieldMapping));
        SidebarGroups.Add(addressGroup);

        // 代理人信息组
        if (detail.HasAgent)
        {
            var agentGroup = new SidebarGroup("代理人信息");
            agentGroup.Items.Add(CreateSidebarItem("是否委托代理", "是", fieldMapping));
            agentGroup.Items.Add(CreateSidebarItem("代理人姓名", detail.AgentName, fieldMapping));
            agentGroup.Items.Add(CreateSidebarItem("代理人身份证", detail.AgentIdCard, fieldMapping));
            agentGroup.Items.Add(CreateSidebarItem("代理关系", detail.AgentRelationship, fieldMapping));
            agentGroup.Items.Add(CreateSidebarItem("代理人证件类型", GetIdTypeDisplay(detail.AgentIdType), fieldMapping));
            SidebarGroups.Add(agentGroup);
        }

        // 经办人信息组
        var operatorGroup = new SidebarGroup("经办人信息");
        operatorGroup.Items.Add(CreateSidebarItem("经办人", detail.OperatorName, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("经办单位", detail.OperatorUnitName, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("民政助理", civilAssistantName, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("区县", orgInfo.District, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("乡镇", orgInfo.Town, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("区县民政局", orgInfo.DistrictCivilBureau, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("区县民政局电话", orgInfo.DistrictCivilBureauPhone, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("经办单位电话", orgInfo.OperatorUnitPhone, fieldMapping));
        SidebarGroups.Add(operatorGroup);

        // 家庭成员组
        if (detail.FamilyMembers.Count > 0)
        {
            var memberGroup = new SidebarGroup($"家庭成员 ({detail.FamilyMembers.Count}人)");
            for (int i = 0; i < detail.FamilyMembers.Count; i++)
            {
                var m = detail.FamilyMembers[i];
                var prefix = $"{i + 1}. {m.Name}";
                memberGroup.Items.Add(new SidebarItem
                {
                    Label = $"{prefix} 身份证",
                    Value = m.IdCard,
                    FieldKey = "",
                    IsMatchable = false,
                    IsAutoFilled = false
                });
                memberGroup.Items.Add(new SidebarItem
                {
                    Label = $"{prefix} 关系",
                    Value = m.Relationship,
                    FieldKey = "",
                    IsMatchable = false,
                    IsAutoFilled = false
                });
            }
            SidebarGroups.Add(memberGroup);
        }

        SidebarTotalCount = SidebarGroups.Sum(g => g.Items.Count);
    }

    private async Task BuildSidebarFromApplicationAsync(ApplicationEntity app, string civilAssistantName, OrganizationInfoDto orgInfo,
        decimal totalIncome, decimal workIncome, decimal businessIncome, decimal propertyIncome,
        decimal transferIncome, decimal alimonyIncome, decimal otherIncome, decimal rigidExpenditure)
    {
        SidebarGroups.Clear();
        AutoFilledCount = 0;

        var fieldMapping = new Dictionary<string, string>
        {
            ["申请人姓名"] = FieldKeys.APPLICANT_NAME,
            ["身份证号"] = FieldKeys.APPLICANT_ID_CARD,
            ["联系电话"] = FieldKeys.CONTACT_PHONE,
            ["家庭地址"] = FieldKeys.FAMILY_ADDRESS,
            ["社区"] = FieldKeys.COMMUNITY,
            ["申请原因"] = FieldKeys.APPLICATION_REASON,
            ["经办人"] = FieldKeys.OPERATOR_NAME,
            ["区县"] = FieldKeys.DISTRICT,
            ["乡镇"] = FieldKeys.TOWN,
            ["民政助理"] = FieldKeys.CIVIL_ASSISTANT_NAME,
            ["户籍类型"] = FieldKeys.CLASSIFICATION_RESULT,
            ["性别"] = "GENDER",
            ["民族"] = "ETHNICITY",
            ["婚姻状况"] = "MARITAL_STATUS",
            ["文化程度"] = "EDUCATION_LEVEL",
            ["申请状态"] = FieldKeys.STATUS,
            ["家庭地址"] = FieldKeys.FAMILY_ADDRESS,
            ["户籍地址"] = FieldKeys.HUKOU_ADDRESS,
            ["家庭人数"] = FieldKeys.HEAD_FAMILY_SIZE,
            ["家庭总收入"] = FieldKeys.INCOME_TOTAL,
            ["人均收入"] = "PER_CAPITA_INCOME",
            ["务工收入"] = FieldKeys.INCOME_LABOR,
            ["经营收入"] = FieldKeys.INCOME_BUSINESS,
            ["财产性收入"] = FieldKeys.INCOME_PROPERTY,
            ["转移性收入"] = FieldKeys.INCOME_TRANSFER,
            ["农业补贴"] = FieldKeys.INCOME_SUBSIDY,
            ["土地收入"] = FieldKeys.INCOME_LAND,
            ["刚性支出"] = FieldKeys.INCOME_RIGID_EXPENDITURE
        };

        // 基本信息组
        var basicGroup = new SidebarGroup("基本信息");
        basicGroup.Items.Add(CreateSidebarItem("申请人姓名", app.ApplicantName, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("身份证号", app.ApplicantIdCard, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("联系电话", app.ApplicantPhone, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("申请原因", app.ApplicationReason, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("户籍类型", app.HukouType, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("性别", app.Gender, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("民族", app.Ethnicity, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("婚姻状况", app.MaritalStatus, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("文化程度", app.EducationLevel, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("申请状态", app.Status, fieldMapping));
        SidebarGroups.Add(basicGroup);

        // 地址信息组
        var addressGroup = new SidebarGroup("地址信息");
        addressGroup.Items.Add(CreateSidebarItem("家庭地址", app.Address, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("社区", app.Community, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("乡镇", app.Town, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("区县", app.District, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("户籍地址", app.HukouAddress, fieldMapping));
        SidebarGroups.Add(addressGroup);

        // 经济信息组（使用从经济明细计算的值，而非 Application 表汇总字段）
        var economicGroup = new SidebarGroup("经济信息");
        var perCapitaIncome = app.FamilySize > 0 ? Math.Round(totalIncome / app.FamilySize, 2) : 0;
        economicGroup.Items.Add(new SidebarItem { Label = "家庭人数", Value = app.FamilySize.ToString(), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "家庭总收入", Value = FormatDecimal(totalIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "人均收入", Value = FormatDecimal(perCapitaIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "务工收入", Value = FormatDecimal(workIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "经营收入", Value = FormatDecimal(businessIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "财产性收入", Value = FormatDecimal(propertyIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "转移性收入", Value = FormatDecimal(transferIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "农业补贴", Value = FormatDecimal(app.SubsidyTotal), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "刚性支出", Value = FormatDecimal(rigidExpenditure), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "赡养费收入", Value = FormatDecimal(Math.Round(alimonyIncome / 12m, 2)), IsMatchable = false });
        SidebarGroups.Add(economicGroup);

        // 分类结果组
        if (!string.IsNullOrEmpty(app.ClassificationResult))
        {
            var classGroup = new SidebarGroup("分类结果");
            classGroup.Items.Add(new SidebarItem { Label = "分类结果", Value = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""), IsMatchable = false });
            classGroup.Items.Add(new SidebarItem { Label = "补贴类型", Value = app.ClassifiedSubsidyType, IsMatchable = false });
            classGroup.Items.Add(new SidebarItem { Label = "补贴金额", Value = FormatDecimal(app.ClassifiedSubsidyAmount), IsMatchable = false });
            classGroup.Items.Add(new SidebarItem { Label = "保障金总额", Value = FormatDecimal(app.TotalGuaranteeAmount), IsMatchable = false });
            // 渐退期审批表基础 8 项：只依赖主档，不依赖渐退行（无行时也有姓名/证号等）
            _fieldData[FieldKeys.GP_HEAD_NAME] = app.ApplicantName ?? "";
            _fieldData[FieldKeys.GP_HEAD_GENDER] = app.Gender ?? "";
            _fieldData[FieldKeys.GP_HEAD_ID_CARD] = app.ApplicantIdCard ?? "";
            _fieldData[FieldKeys.GP_HEAD_AGE] = (IdCardValidator.ExtractAge(app.ApplicantIdCard ?? "") ?? 0).ToString();
            _fieldData[FieldKeys.GP_FAMILY_SIZE] = app.FamilySize.ToString();
            _fieldData[FieldKeys.GP_CLASSIFICATION] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "");
            _fieldData[FieldKeys.GP_ADDRESS] = app.Address ?? "";
            _fieldData[FieldKeys.GP_PHONE] = app.ApplicantPhone ?? "";
            _fieldData[FieldKeys.GP_AUDIT_TIME] = DateTime.Now.ToString("yyyy年M月d日");

            // 渐退取最近一条（含已退出）：退出后补打审批表仍需起止/退出说明
            var graceRes = await _gracePeriodService.GetLatestAsync(app.Id, CancellationToken);
            if (graceRes.IsSuccess && graceRes.Value != null)
            {
                if (graceRes.Value.IsActive)
                {
                    classGroup.Items.Add(new SidebarItem { Label = "渐退期", Value = $"{graceRes.Value.GracePeriodMonths}个月", IsMatchable = false });
                    classGroup.Items.Add(new SidebarItem { Label = "渐退开始", Value = graceRes.Value.StartDate?.ToString("yyyy-MM-dd") ?? "", IsMatchable = false });
                    classGroup.Items.Add(new SidebarItem { Label = "渐退结束", Value = graceRes.Value.EndDate?.ToString("yyyy-MM-dd") ?? "", IsMatchable = false });
                }

                // 渐退期审批表：依赖渐退行的字段（变动说明/月数/起止/退出句）
                _fieldData[FieldKeys.GP_CHANGE_DETAIL] = await BuildGraceChangeDetailAsync(app, graceRes.Value);
                _fieldData[FieldKeys.GP_PERIOD_MONTHS] = graceRes.Value.GracePeriodMonths?.ToString() ?? "";
                _fieldData[FieldKeys.GP_START] = graceRes.Value.StartDate?.ToString("yyyy年M月d日") ?? "";
                _fieldData[FieldKeys.GP_END] = graceRes.Value.EndDate?.ToString("yyyy年M月d日") ?? "";
                var gpStartText = graceRes.Value.StartDate?.ToString("yyyy年M月d日") ?? "";
                var gpEndText = graceRes.Value.EndDate?.ToString("yyyy年M月d日") ?? "";
                _fieldData[FieldKeys.GP_PERIOD_RANGE] =
                    string.IsNullOrEmpty(gpStartText) && string.IsNullOrEmpty(gpEndText) ? ""
                    : string.IsNullOrEmpty(gpStartText) ? $"渐退期至{gpEndText}为止"
                    : string.IsNullOrEmpty(gpEndText) ? $"渐退期自{gpStartText}起"
                    : $"渐退期自{gpStartText}起，至{gpEndText}为止";
                _fieldData[FieldKeys.GP_EXIT_SITUATION] = await BuildGraceExitSituationAsync(app, graceRes.Value);

                // 档案_保障金减少：渐退封顶减发时填充（原额 > 现应发额）
                var graceOriginal = graceRes.Value.OriginalGuaranteeAmount ?? 0;
                var graceCurrent = graceRes.Value.GraceGrantAmount ?? app.HouseholdMonthlyGuaranteeAmount;
                if (graceOriginal > 0 && graceCurrent < graceOriginal)
                {
                    var decrease = graceOriginal - graceCurrent;
                    var capBasis = $"{(app.HukouType?.Contains("Urban") == true ? "城市" : "农村")}低保标准×{app.FamilySize}人";
                    _fieldData[FieldKeys.GR_HEAD_NAME] = app.ApplicantName ?? "";
                    _fieldData[FieldKeys.GR_ID_CARD] = app.ApplicantIdCard ?? "";
                    _fieldData[FieldKeys.GR_FAMILY_SIZE] = app.FamilySize.ToString();
                    _fieldData[FieldKeys.GR_OLD_AMOUNT] = FormatDecimal(graceOriginal);
                    _fieldData[FieldKeys.GR_NEW_AMOUNT] = FormatDecimal(graceCurrent);
                    _fieldData[FieldKeys.GR_DECREASE_AMOUNT] = FormatDecimal(decrease);
                    _fieldData[FieldKeys.GR_CAP_BASIS] = capBasis;
                    _fieldData[FieldKeys.GR_GRACE_START] = graceRes.Value.StartDate?.ToString("yyyy年M月d日") ?? "";
                    _fieldData[FieldKeys.GR_GRACE_END] = graceRes.Value.EndDate?.ToString("yyyy年M月d日") ?? "";
                    _fieldData[FieldKeys.GR_REASON] = "原保障金超过本户口类型最低保障额上限，渐退期内封顶减发";
                    _fieldData[FieldKeys.GR_AUDIT_TIME] = DateTime.Now.ToString("yyyy年M月d日");
                }
            }

            // 增减员调整表：人员变动类变更（MemberChange 类别 + HouseholdDeath 等类型兜底；
            // 记录可挂在旧档，取数经 new_application_id 关联到新档）
            var changeRes = await _changeService.GetMemberChangeRecordsAsync(app.Id, 5, CancellationToken);

            // 头部公共字段不依赖变更记录：即使本户无增减员，表头也必须有值
            //（性别按身份证号推导，与模板1/模板10 同口径，登记值仅兜底）
            _fieldData[FieldKeys.ADJ_HEAD_NAME] = app.ApplicantName ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_GENDER] = ResolveGender(app.ApplicantIdCard, app.Gender);
            _fieldData[FieldKeys.ADJ_HEAD_BIRTH] = IdCardValidator.ExtractBirthDate(app.ApplicantIdCard ?? "")?.ToString("yyyy-MM-dd") ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_NATION] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, app.Ethnicity ?? "");
            _fieldData[FieldKeys.ADJ_HEAD_FAMILY_SIZE] = app.FamilySize.ToString();
            _fieldData[FieldKeys.ADJ_HEAD_FAMILY_TYPE] = app.HukouType ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_ADDRESS] = app.Address ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_CLASSIFICATION] = app.ClassificationResult ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_HUKOU] = await ResolveHukouAddressAsync(app);
            _fieldData[FieldKeys.ADJ_HEAD_ID_CARD] = app.ApplicantIdCard ?? "";
            var adjAuditTime = DateTime.Now;
            _fieldData[FieldKeys.ADJ_AUDIT_TIME] = adjAuditTime.ToString("yyyy年M月d日");
            // 执行时间 = 审批时间的次月 1 号（与模板1 复核组 {审核次月日期} 同口径）
            _fieldData[FieldKeys.ADJ_EFFECTIVE_DATE] =
                new DateTime(adjAuditTime.Year, adjAuditTime.Month, 1).AddMonths(1).ToString("yyyy年M月1日");

            if (changeRes.IsSuccess && changeRes.Value != null && changeRes.Value.Count > 0)
            {
                // 统计增员/减员人数和金额差
                int addCount = 0, removeCount = 0;
                decimal amountDiff = 0;

                static bool IsRemoveType(string t) =>
                    t is "MemberDeath" or "MemberRemove" or "HouseholdDeath";

                foreach (var ch in changeRes.Value)
                {
                    string chType = ch.ChangeType ?? "";
                    DateTime chDate = ch.ChangeDate;
                    decimal oldAmt = ch.OldGuaranteeAmount ?? 0;
                    decimal newAmt = ch.NewGuaranteeAmount ?? 0;

                    if (IsRemoveType(chType))
                        removeCount++;
                    else if (chType is "MemberAdd" or "MemberModify" or "HouseholdHeadChange")
                        addCount++;

                    amountDiff += (newAmt - oldAmt);
                }

                // 增减金额（差额）；渐退期内待遇固定，增减员不产生增发/减发，两格都固定 0.00
                var inGracePeriod = graceRes.IsSuccess && graceRes.Value != null && graceRes.Value.IsActive;
                if (inGracePeriod)
                {
                    _fieldData[FieldKeys.ADJ_INCREASE_AMOUNT] = "0.00";
                    _fieldData[FieldKeys.ADJ_DECREASE_AMOUNT] = "0.00";
                }
                else if (amountDiff > 0)
                {
                    _fieldData[FieldKeys.ADJ_INCREASE_AMOUNT] = amountDiff.ToString("N2");
                    _fieldData[FieldKeys.ADJ_DECREASE_AMOUNT] = "0.00";
                }
                else
                {
                    _fieldData[FieldKeys.ADJ_INCREASE_AMOUNT] = "0.00";
                    _fieldData[FieldKeys.ADJ_DECREASE_AMOUNT] = Math.Abs(amountDiff).ToString("N2");
                }

                // ── 逐人增减明细（增员/减员槽）──
                // 权威源 nc_biz_change_details（成员增减流程逐人登记）；
                // 成员死亡/户主死亡流程无明细，服务按 change_reason 的"动词: 姓名(身份证)"解析兜底，
                // 并回查成员表补齐性别/关系/身体状况/工作单位/收入（减员行可能只剩软删除历史行）
                var adjustEntries = await GetMemberAdjustEntriesSafeAsync(app.Id);

                var addAll = adjustEntries
                    .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_ADD, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var removeAll = adjustEntries
                    .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_REMOVE, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 人数口径 = 逐人明细人数（一次变更可含多人，按变更记录数会少算）；无明细时回退按记录数
                if (addAll.Count > 0) addCount = addAll.Count;
                if (removeAll.Count > 0) removeCount = removeAll.Count;

                // 增员槽（最多 3 人，按变更日期倒序）
                for (var slot = 0; slot < addAll.Count && slot < 3; slot++)
                    FillAdjustSlot(isAdd: true, slot + 1, addAll[slot]);

                // 减员槽（最多 3 人，按变更日期倒序）
                for (var slot = 0; slot < removeAll.Count && slot < 3; slot++)
                    FillAdjustSlot(isAdd: false, slot + 1, removeAll[slot]);

                // 镇政府意见-家庭具体情况 = 逐人增减摘要 + 定期复核情况全文（变化情况+核算过程，含中文类别全称）
                // 复核组在 ApplyReviewGroupAsync 已先行装配（LoadApplicationDataAsync/分步路径均先于侧边栏）；
                // 无复核记录时回退中文描述，禁止拼 ClassificationResult 原始码（曾打出 RuralLowIncome 英文）
                var summaryParts = new List<string>();
                foreach (var e in removeAll.Take(3))
                    summaryParts.Add($"减员：{e.Name}（{ResolveAdjustShortReason(e)}）");
                foreach (var e in addAll.Take(3))
                    summaryParts.Add($"增员：{e.Name}（{ResolveAdjustShortReason(e)}）");
                var adjustSummary = string.Join("；", summaryParts);
                var reviewSituation = _fieldData.GetValueOrDefault(FieldKeys.REVIEW_SITUATION, "");
                var familyFallback = $"{app.ApplicantName}户，家庭人口{app.FamilySize}人，" +
                    $"{ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "")}，月保障金{FormatDecimal(app.TotalGuaranteeAmount)}元";
                _fieldData[FieldKeys.ADJ_FAMILY_DETAIL] = string.IsNullOrWhiteSpace(reviewSituation)
                    ? (string.IsNullOrWhiteSpace(adjustSummary) ? familyFallback : $"{adjustSummary}。{familyFallback}")
                    : string.IsNullOrWhiteSpace(adjustSummary) ? reviewSituation : $"{adjustSummary}。{reviewSituation}";
                _fieldData[FieldKeys.ADJ_CHANGE_SUMMARY] = $"增员{addCount}人，减员{removeCount}人";
                _fieldData[FieldKeys.ADJ_ADD_COUNT] = addCount.ToString();
                _fieldData[FieldKeys.ADJ_REMOVE_COUNT] = removeCount.ToString();
            }

            SidebarGroups.Add(classGroup);
        }

        // 经办人信息组
        var operatorGroup = new SidebarGroup("经办人信息");
        operatorGroup.Items.Add(CreateSidebarItem("经办人", app.UpdatedBy, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("民政助理", civilAssistantName, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("区县", orgInfo.District, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("乡镇", orgInfo.Town, fieldMapping));
        SidebarGroups.Add(operatorGroup);

        SidebarTotalCount = SidebarGroups.Sum(g => g.Items.Count);
    }

    /// <summary>
    /// 取逐人增/减员明细（增减员调整表槽位用）。
    /// 加载失败记错误日志并返回空列表：槽位留空、人数回退按变更记录数，绝不伪造数据。
    /// </summary>
    private async Task<List<MemberAdjustEntry>> GetMemberAdjustEntriesSafeAsync(long applicationId)
    {
        var result = await _changeService.GetMemberAdjustEntriesAsync(applicationId, 5, CancellationToken);
        if (result.IsSuccess && result.Value != null)
            return result.Value;

        _logger.Error($"增减员逐人明细加载失败: ApplicationId={applicationId}, ErrorCode={result.ErrorCode}, Message={result.Message}");
        return new List<MemberAdjustEntry>();
    }

    /// <summary>
    /// 增/减员槽位填充（槽位 1..3）：
    /// 关系与身体状况走字典显示值、性别按身份证号推导、收入按年值展示——与模板1/模板10 同口径。
    /// 增减原因逐行独立（ADJ_ADD_REASON_N/ADJ_REMOVE_REASON_N），写该行人员的短原因（≤6 字）。
    /// </summary>
    private void FillAdjustSlot(bool isAdd, int slot, MemberAdjustEntry entry)
    {
        var name = entry.Name ?? "";
        var idCard = entry.IdCard ?? "";
        var gender = ResolveGender(idCard, entry.Gender);
        var relation = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, entry.RelationshipToHead ?? "");
        var health = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, entry.HealthStatus ?? "");
        var workplace = entry.WorkUnit ?? "";
        var income = entry.AnnualIncome > 0 ? $"{entry.AnnualIncome:N2}元/年" : "";
        var reason = ResolveAdjustShortReason(entry);

        if (isAdd)
        {
            switch (slot)
            {
                case 1:
                    _fieldData[FieldKeys.ADJ_ADD_NAME_1] = name;
                    _fieldData[FieldKeys.ADJ_ADD_ID_CARD_1] = idCard;
                    _fieldData[FieldKeys.ADJ_ADD_GENDER_1] = gender;
                    _fieldData[FieldKeys.ADJ_ADD_RELATION_1] = relation;
                    _fieldData[FieldKeys.ADJ_ADD_HEALTH_1] = health;
                    _fieldData[FieldKeys.ADJ_ADD_WORKPLACE_1] = workplace;
                    _fieldData[FieldKeys.ADJ_ADD_REASON_1] = reason;
                    _fieldData[FieldKeys.ADJ_ADD_INCOME_1] = income;
                    break;
                case 2:
                    _fieldData[FieldKeys.ADJ_ADD_NAME_2] = name;
                    _fieldData[FieldKeys.ADJ_ADD_ID_CARD_2] = idCard;
                    _fieldData[FieldKeys.ADJ_ADD_GENDER_2] = gender;
                    _fieldData[FieldKeys.ADJ_ADD_RELATION_2] = relation;
                    _fieldData[FieldKeys.ADJ_ADD_HEALTH_2] = health;
                    _fieldData[FieldKeys.ADJ_ADD_WORKPLACE_2] = workplace;
                    _fieldData[FieldKeys.ADJ_ADD_REASON_2] = reason;
                    _fieldData[FieldKeys.ADJ_ADD_INCOME_2] = income;
                    break;
                case 3:
                    _fieldData[FieldKeys.ADJ_ADD_NAME_3] = name;
                    _fieldData[FieldKeys.ADJ_ADD_ID_CARD_3] = idCard;
                    _fieldData[FieldKeys.ADJ_ADD_GENDER_3] = gender;
                    _fieldData[FieldKeys.ADJ_ADD_RELATION_3] = relation;
                    _fieldData[FieldKeys.ADJ_ADD_HEALTH_3] = health;
                    _fieldData[FieldKeys.ADJ_ADD_WORKPLACE_3] = workplace;
                    _fieldData[FieldKeys.ADJ_ADD_REASON_3] = reason;
                    _fieldData[FieldKeys.ADJ_ADD_INCOME_3] = income;
                    break;
            }
            return;
        }

        switch (slot)
        {
            case 1:
                _fieldData[FieldKeys.ADJ_REMOVE_NAME_1] = name;
                _fieldData[FieldKeys.ADJ_REMOVE_ID_CARD_1] = idCard;
                _fieldData[FieldKeys.ADJ_REMOVE_GENDER_1] = gender;
                _fieldData[FieldKeys.ADJ_REMOVE_RELATION_1] = relation;
                _fieldData[FieldKeys.ADJ_REMOVE_HEALTH_1] = health;
                _fieldData[FieldKeys.ADJ_REMOVE_WORKPLACE_1] = workplace;
                _fieldData[FieldKeys.ADJ_REMOVE_REASON_1] = reason;
                _fieldData[FieldKeys.ADJ_REMOVE_INCOME_1] = income;
                break;
            case 2:
                _fieldData[FieldKeys.ADJ_REMOVE_NAME_2] = name;
                _fieldData[FieldKeys.ADJ_REMOVE_ID_CARD_2] = idCard;
                _fieldData[FieldKeys.ADJ_REMOVE_GENDER_2] = gender;
                _fieldData[FieldKeys.ADJ_REMOVE_RELATION_2] = relation;
                _fieldData[FieldKeys.ADJ_REMOVE_HEALTH_2] = health;
                _fieldData[FieldKeys.ADJ_REMOVE_WORKPLACE_2] = workplace;
                _fieldData[FieldKeys.ADJ_REMOVE_REASON_2] = reason;
                _fieldData[FieldKeys.ADJ_REMOVE_INCOME_2] = income;
                break;
            case 3:
                _fieldData[FieldKeys.ADJ_REMOVE_NAME_3] = name;
                _fieldData[FieldKeys.ADJ_REMOVE_ID_CARD_3] = idCard;
                _fieldData[FieldKeys.ADJ_REMOVE_GENDER_3] = gender;
                _fieldData[FieldKeys.ADJ_REMOVE_RELATION_3] = relation;
                _fieldData[FieldKeys.ADJ_REMOVE_HEALTH_3] = health;
                _fieldData[FieldKeys.ADJ_REMOVE_WORKPLACE_3] = workplace;
                _fieldData[FieldKeys.ADJ_REMOVE_REASON_3] = reason;
                _fieldData[FieldKeys.ADJ_REMOVE_INCOME_3] = income;
                break;
        }
    }

    /// <summary>
    /// 增减员槽位短原因（模板格子仅 6 字，禁止写 change_reason 原文长句）：
    /// 优先逐人登记的固定选项短语（成员增减流程，均 ≤6 字）；死亡类流程无登记值，
    /// 按变更类型兜底；其余留空（打印端显示 "-"，不编造原因）。
    /// </summary>
    private static string ResolveAdjustShortReason(MemberAdjustEntry entry)
    {
        var reason = entry.ReasonName?.Trim() ?? "";
        if (reason.Length == 0)
        {
            reason = entry.ChangeType switch
            {
                DictionaryConstants.ChangeType.HOUSEHOLD_DEATH => "原户主死亡",
                DictionaryConstants.ChangeType.MEMBER_DEATH => "人员死亡",
                _ => ""
            };
        }
        return reason.Length > 6 ? reason[..6] : reason;
    }

    /// <summary>性别展示值：身份证号推导优先，无有效证件号时回退登记值</summary>
    private static string ResolveGender(string? idCard, string? fallback)
    {
        var gender = AddressResolver.ExtractGenderFromIdCard(idCard);
        return gender == "-" ? (fallback ?? "") : gender;
    }

    private SidebarItem CreateSidebarItem(string label, string value, Dictionary<string, string> fieldMapping)
    {
        var fieldKey = fieldMapping.GetValueOrDefault(label, "");
        var isMatchable = !string.IsNullOrEmpty(fieldKey);
        var isAutoFilled = isMatchable && !string.IsNullOrWhiteSpace(value);

        if (isAutoFilled)
            AutoFilledCount++;

        return new SidebarItem
        {
            Label = label,
            Value = value ?? string.Empty,
            FieldKey = fieldKey,
            IsMatchable = isMatchable,
            IsAutoFilled = isAutoFilled
        };
    }

    // ========================
    //  辅助方法
    // ========================

    private async Task<string> GetCivilAssistantNameAsync()
    {
        try
        {
            var userId = App.CurrentUserId;
            if (!userId.HasValue) return "未配";

            var userService = _serviceProvider.GetRequiredService<IUserService>();
            var userResult = await userService.GetByIdAsync(userId.Value);
            if (userResult.IsSuccess && userResult.Value != null)
                return userResult.Value.FullName ?? "未配";

            return "未配";
        }
        catch
        {
            return "未配";
        }
    }

    private async Task<OrganizationInfoDto> GetOrganizationInfoAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return new OrganizationInfoDto("-", "-", "-", "-", "-", "-");

        try
        {
            var orgResult = await _organizationService.GetByIdAsync(orgId.Value);
            if (orgResult.IsFailure || orgResult.Value == null)
                return new OrganizationInfoDto("-", "-", "-", "-", "-", "-");

            var org = orgResult.Value;
            var district = org.CountyName ?? "-";
            var town = org.TownName ?? "-";
            var unitName = org.Name ?? "-";
            var unitPhone = org.Phone ?? "-";
            var parentName = org.ParentName ?? "-";
            var parentPhone = "-";

            if (org.ParentId.HasValue)
            {
                var parentResult = await _organizationService.GetByIdAsync(org.ParentId.Value);
                if (parentResult.IsSuccess && parentResult.Value != null)
                    parentPhone = parentResult.Value.Phone ?? "-";
            }

            return new OrganizationInfoDto(district, town, parentName, parentPhone, unitName, unitPhone);
        }
        catch
        {
            return new OrganizationInfoDto("-", "-", "-", "-", "-", "-");
        }
    }

    private string GenerateNoticeNumber()
    {
        var nextMonth = DateTime.Now.AddMonths(1);
        return $"{nextMonth.Year}-{nextMonth.Month}-1";
    }

    private string GetIdTypeDisplay(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return IdTypeConstants.DefaultIdType;
        return _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, code);
    }

    private string GetApplicationReasonDisplay(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return PickerConstants.ApplicationReason.OtherDisplay;
        return _dictCacheService.GetValue(DictionaryTypeCodes.ApplicationReasons, code);
    }

    /// <summary>
    /// 构建户主条目（用于 _tableData 第 1 位）
    /// </summary>
    private Dictionary<string, string> BuildHeadMemberEntry(string name, string idCard, string address)
    {
        return new Dictionary<string, string>
        {
            [FieldKeys.FAMILY_MEMBER_NAME] = name,
            [FieldKeys.FAMILY_MEMBER_ID_CARD] = idCard,
            [FieldKeys.FAMILY_MEMBER_CERT_TYPE] = "居民身份证",
            [FieldKeys.FAMILY_MEMBER_RELATION] = "本人/户主",
            [FieldKeys.FAMILY_MEMBER_ADDRESS] = address,
            [FieldKeys.HEAD_ID_CARD] = idCard
        };
    }

    /// <summary>
    /// 构建家庭成员条目
    /// </summary>
    private Dictionary<string, string> BuildMemberEntry(string name, string idCard, string relationship, string address, string headIdCard)
    {
        return new Dictionary<string, string>
        {
            [FieldKeys.FAMILY_MEMBER_NAME] = name,
            [FieldKeys.FAMILY_MEMBER_ID_CARD] = idCard,
            [FieldKeys.FAMILY_MEMBER_CERT_TYPE] = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, "ResidentIdCard"),
            [FieldKeys.FAMILY_MEMBER_RELATION] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, relationship),
            [FieldKeys.FAMILY_MEMBER_ADDRESS] = address,
            [FieldKeys.HEAD_ID_CARD] = headIdCard
        };
    }

    /// <summary>
    /// 格式化数值：始终输出带两位小数的格式
    /// </summary>
    private static string FormatDecimal(decimal value)
    {
        return value.ToString("N2");
    }

    /// <summary>
    /// 构建土地收入情况（复合字段）
    /// 格式：本人申请{享受类别}待遇，由{所在村屯}出具土地收入证明...
    /// includeLead=false 时跳过"本人申请…口人。"引言段（定期复核审批表专用，从"土地情况"开始）。
    /// </summary>
    private async Task<string> BuildLandIncomeSituationAsync(ApplicationEntity app, bool includeLead = true)
    {
        var classificationDesc = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? "");
        var village = app.Community ?? app.Town ?? "";
        var headName = app.ApplicantName;
        var familySize = app.FamilySize;

        // 土地面积
        var selfFarmedArea = app.SelfFarmedLandArea;
        var subleasedArea = app.SubleasedLandArea;
        var contractedArea = app.ContractedLandArea;

        // 土地作物收入：直接从数据库读取（已由经济明细核算）
        var cropIncome = app.LandIncomeTotal;

        // 补贴金额（从经济明细查询）
        decimal landFertilityAmount = 0;   // 地力补贴
        decimal soybeanAmount = 0;         // 大豆补贴
        decimal rotationAmount = 0;        // 轮作补贴
        decimal riceAmount = 0;            // 种植补贴

        try
        {
            var economicDetailService = _serviceProvider.GetRequiredService<IEconomicDetailService>();
            var detailResult = await economicDetailService.LoadAllAsync(app.Id, CancellationToken);
            if (detailResult.IsSuccess && detailResult.Value?.Subsidies != null)
            {
                foreach (var subsidy in detailResult.Value.Subsidies)
                {
                    switch (subsidy.SubsidyType)
                    {
                        case "地力补贴": landFertilityAmount += subsidy.Amount; break;
                        case "大豆补贴": soybeanAmount += subsidy.Amount; break;
                        case "轮作补贴": rotationAmount += subsidy.Amount; break;
                        case "地表水水稻":
                        case "地下水水稻":
                        case "玉米补贴": riceAmount += subsidy.Amount; break;
                    }
                }
            }
        }
        catch { }

        // 全家土地总计收入 = 作物收入（数据库）+ 补贴
        var totalLandIncome = cropIncome + landFertilityAmount + soybeanAmount + rotationAmount + riceAmount;

        var sb = new System.Text.StringBuilder();
        if (includeLead)
        {
            sb.Append($"本人申请{classificationDesc}待遇，由{village}（村/居民委员会）出具土地收入证明，");
            sb.Append($"户主{headName}申请享受{familySize}口人。");
            sb.AppendLine();
        }
        sb.Append($"土地情况：自种{selfFarmedArea:F2}亩；转包{subleasedArea:F2}亩；承包{contractedArea:F2}亩。");
        sb.AppendLine();
        sb.Append($"土地收入情况：土地作物收入：{FormatDecimal(cropIncome)}元；");
        sb.Append($"粮食补贴:{FormatDecimal(riceAmount)}元；");
        sb.Append($"土地轮作补贴：{FormatDecimal(rotationAmount)}元；");
        sb.Append($"高脂高油补贴：{FormatDecimal(soybeanAmount)}元；");
        sb.Append($"地力补贴：{FormatDecimal(landFertilityAmount)}元。");
        sb.AppendLine();
        sb.Append($"全家土地总计收入为{FormatDecimal(totalLandIncome)}元。（标准亩666.7方）—特此证明。");

        return sb.ToString();
    }

    /// <summary>
    /// 定期复核审批表复核组装配：{待遇变化分类}/{待遇变化金额}/{复核时间}/{定期复核情况}。
    /// 取最近一条复核/变更记录（经济复核、户主死亡、成员变更等完整流程均覆盖）；
    /// 无记录时保持初始化器默认值 ""（打印端按占位符缺省显示 "-"，不会报未映射）。
    /// </summary>
    private async Task ApplyReviewGroupAsync(ApplicationEntity app, long applicationId)
    {
        var reviewRes = await _changeService.GetLatestReviewChangeRecordAsync(applicationId, CancellationToken);
        if (reviewRes.IsSuccess && reviewRes.Value != null)
        {
            var rv = reviewRes.Value;
            var (action, amountTail) = BuildReviewAmountChange(app, rv);
            _fieldData[FieldKeys.REVIEW_CLASSIFICATION_CHANGE] = action;
            _fieldData[FieldKeys.REVIEW_AMOUNT_CHANGE] = amountTail;
            _fieldData[FieldKeys.REVIEW_TIME] = rv.ChangeDate?.ToString("yyyy年M月d日") ?? "";

            // 定期复核情况 = 复核原因 + 变化内容（人口/月人均收入/类别，只列变化项）+ 核算过程
            _fieldData[FieldKeys.REVIEW_SITUATION] = await BuildReviewChangeDetailAsync(app, rv);
        }
        else if (reviewRes.IsFailure)
        {
            _logger.Warn($"定期复核审批表取复核记录失败: ApplicationId={applicationId}, {reviewRes.Message}");
        }
    }

    /// <summary>
    /// 待遇变化句：返回 {待遇变化分类} 动作词 + {待遇金额变化} 句尾（以「，」起头，2026-10 起仅含类别变化）。
    /// 金额句已整体删除（避免"停发待遇…停止发放"等同义重复）：原/现月保障金对比统一写入
    /// {定期复核情况} 变化情况段（BuildReviewChangeDetailAsync），决定句只表达「自X日起 + 动作 + 类别变化」。
    /// 户主死亡流程同理只留动作词；类别在「保障类型」里已有，死亡分支不再追加类别句（历史行为保持）。
    /// </summary>
    private (string Action, string AmountTail) BuildReviewAmountChange(ApplicationEntity app, ReviewChangeRecord rv)
    {
        var oldClsFull = ClassificationConstants.ConvertToFullName(rv.OldClassification ?? "");
        var newClsFull = ClassificationConstants.ConvertToFullName(rv.NewClassification ?? "");
        var oldAmt = rv.OldGuaranteeAmount;
        var newAmt = rv.NewGuaranteeAmount;

        if (rv.ChangeType == "HouseholdDeath" || rv.ChangeReasonType == "户主死亡")
        {
            return ("停发", "");
        }

        string action;
        if (ClassificationConstants.IsCodeStop(rv.NewClassification ?? ""))
        {
            action = "停发";
        }
        else if (oldAmt.HasValue && newAmt.HasValue && newAmt.Value > oldAmt.Value)
        {
            action = "增发";
        }
        else if (oldAmt.HasValue && newAmt.HasValue && newAmt.Value < oldAmt.Value)
        {
            action = "减发";
        }
        else if (oldAmt.HasValue && newAmt.HasValue)
        {
            action = "保持";
        }
        else if (newAmt.HasValue)
        {
            // 类别增类（停旧与建新各一条记录，本条只带新档金额）
            action = "认定";
        }
        else
        {
            _logger.Warn($"待遇变化句缺值降级: ApplicationId={app.Id}, ChangeType={rv.ChangeType}, OldAmt={(oldAmt.HasValue ? oldAmt.Value.ToString() : "null")}, NewAmt={(newAmt.HasValue ? newAmt.Value.ToString() : "null")}");
            action = "保持";
        }

        // 金额句已移至{定期复核情况}，此处只留类别变化
        var amountTail = "";

        if (!string.IsNullOrEmpty(oldClsFull) && !string.IsNullOrEmpty(newClsFull) && oldClsFull != newClsFull)
        {
            amountTail += $"，享受类别由{oldClsFull}调整为{newClsFull}";
        }
        else if (string.IsNullOrEmpty(oldClsFull) && !string.IsNullOrEmpty(newClsFull))
        {
            amountTail += $"，类别认定为{newClsFull}";
        }

        return (action, amountTail);
    }

    /// <summary>
    /// 读取变更快照 JSON 的整型键（缺键/非数字/坏 JSON 返回 null，不抛）。
    /// </summary>
    private static int? ReadSnapshotInt(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var v)
                && v.ValueKind == JsonValueKind.Number
                && v.TryGetInt32(out var i))
            {
                return i;
            }
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>
    /// 读取变更快照 JSON 的字符串键（缺键/非字符串/坏 JSON 返回 null，不抛）。
    /// </summary>
    private static string? ReadSnapshotString(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var v)
                && v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>
    /// 户主户籍地址：优先取主表 HukouAddress 原文；为空时按户籍区域ID经 IRegionService 逐级取名拼「市+县+镇+村」
    /// （与既有数据格式一致，不含省）；ID 也为空返回 ""（打印端按占位符缺省显示 "-"）。
    /// 只补户籍侧、绝不回退现住地址成分；查询失败记 Warn 后返回 ""。
    /// </summary>
    private async Task<string> ResolveHukouAddressAsync(ApplicationEntity app)
    {
        var direct = app.HukouAddress?.Trim();
        if (!string.IsNullOrEmpty(direct)) return direct;

        var hasAnyId = app.HukouCityId is > 0 || app.HukouCountyId is > 0 || app.HukouTownId is > 0 || app.HukouVillageId is > 0;
        if (!hasAnyId) return "";

        try
        {
            var regionService = _serviceProvider.GetRequiredService<IRegionService>();
            var sb = new StringBuilder(64);

            if (app.HukouCityId is > 0)
            {
                var r = await regionService.GetCityByIdAsync(app.HukouCityId.Value);
                if (r.IsSuccess && r.Value != null) sb.Append(r.Value.CityName);
                else if (r.IsFailure) _logger.Warn($"户籍地址-市级区域名查询失败（ApplicationId={app.Id}, CityId={app.HukouCityId}, {r.Message}）");
            }
            if (app.HukouCountyId is > 0)
            {
                var r = await regionService.GetCountyByIdAsync(app.HukouCountyId.Value);
                if (r.IsSuccess && r.Value != null) sb.Append(r.Value.CountyName);
                else if (r.IsFailure) _logger.Warn($"户籍地址-县级区域名查询失败（ApplicationId={app.Id}, CountyId={app.HukouCountyId}, {r.Message}）");
            }
            if (app.HukouTownId is > 0)
            {
                var r = await regionService.GetTownByIdAsync(app.HukouTownId.Value);
                if (r.IsSuccess && r.Value != null) sb.Append(r.Value.TownName);
                else if (r.IsFailure) _logger.Warn($"户籍地址-乡镇区域名查询失败（ApplicationId={app.Id}, TownId={app.HukouTownId}, {r.Message}）");
            }
            if (app.HukouVillageId is > 0)
            {
                var r = await regionService.GetVillageByIdAsync(app.HukouVillageId.Value);
                if (r.IsSuccess && r.Value != null) sb.Append(r.Value.VillageName);
                else if (r.IsFailure) _logger.Warn($"户籍地址-村级区域名查询失败（ApplicationId={app.Id}, VillageId={app.HukouVillageId}, {r.Message}）");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Warn($"户籍地址区域名拼接失败（ApplicationId={app.Id}）: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 定期复核审批表：变化内容拼句 = 短原因。变化情况：人口/死亡/月人均收入/月保障金/类别（只列变化项，全无变化则"无明显变化"）。
    /// 原因整段是家庭情况说明长文，本表只保留到「…基本生活出现严重困难。」（找不到该句则保留原文）。
    /// 过去收入锚定本次复核记录 old_per_capita_income：NULL=无历史收入记录按 0.00 元显示
    ///（复核向导先改数后抓快照，历史真值可能缺失——贺满程单复核周期即此情形）。
    /// 快照挂 change_record.application_id：停旧建新链在旧档（OriginalApplicationId），同档复核在本档。
    /// 查询失败显式降级为原因原文并记业务日志（不吞异常、不返回空串）。
    /// </summary>
    private async Task<string> BuildReviewChangeDetailAsync(ApplicationEntity app, ReviewChangeRecord review)
    {
        var reason = string.IsNullOrWhiteSpace(review.ChangeReason) ? "定期复核" : review.ChangeReason;
        const string reasonEnding = "基本生活出现严重困难。";
        var reasonEndIdx = reason.IndexOf(reasonEnding, StringComparison.Ordinal);
        if (reasonEndIdx >= 0)
        {
            reason = reason.Substring(0, reasonEndIdx + reasonEnding.Length);
        }
        // 句号统一由 core 补一个（存量 change_reason 多以句号结尾，直接拼会得到"。。"）
        if (reason.EndsWith("。"))
        {
            reason = reason.Substring(0, reason.Length - 1);
        }
        var parts = new List<string>();
        string core;

        try
        {
            var snapOwner = app.OriginalApplicationId > 0 ? app.OriginalApplicationId : app.Id;
            var snapRes = await _changeService.GetLatestBeforeSnapshotAsync(snapOwner, CancellationToken);
            var snap = snapRes.IsSuccess ? snapRes.Value : null;
            if (snapRes.IsFailure)
            {
                _logger.LogBusiness("定期复核变化内容-快照查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", snapRes.ErrorCode ?? ""));
            }
            else if (snap != null && snap.OldFamilySize is > 0 && snap.OldFamilySize != app.FamilySize)
            {
                parts.Add($"家庭人口由{snap.OldFamilySize}人变为{app.FamilySize}人");
            }

            // Before/After 快照：家庭人口与死亡原因（死亡/成员变更链的快照无 Components 键，
            // GetLatestBeforeSnapshotAsync 取不到，须按 change_id 另取）
            var hasFamilySizePart = parts.Any(p => p.StartsWith("家庭人口"));
            var pairRes = await _changeService.GetChangeSnapshotsAsync(review.Id, CancellationToken);
            if (pairRes.IsFailure)
            {
                _logger.LogBusiness("定期复核变化内容-变更快照查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", pairRes.ErrorCode ?? ""));
            }
            else
            {
                if (pairRes.Value is null && app.OriginalApplicationId > 0)
                {
                    // 停旧建新链兜底：当前复核记录可能是无快照的 CategoryAdd（经济复核跨类行/Step5 跨类补写行），
                    // 死亡原因与家庭人口快照在链上发起记录（如户主死亡记录）——按本档案关联记录回退取一次
                    var linkedRes = await _changeService.GetLinkedChangeSnapshotsAsync(app.Id, CancellationToken);
                    if (linkedRes.IsFailure)
                        _logger.LogBusiness("定期复核变化内容-链上快照兜底查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", linkedRes.ErrorCode ?? ""));
                    else
                        pairRes = linkedRes;
                }

                if (pairRes.Value is { } pair)
                {
                    var beforeSize = ReadSnapshotInt(pair.BeforeJson, "FamilySize");
                    if (!hasFamilySizePart && beforeSize is > 0 && beforeSize != app.FamilySize)
                    {
                        parts.Add($"家庭人口由{beforeSize}人变为{app.FamilySize}人");
                    }

                    var deathReason = ReadSnapshotString(pair.AfterJson, "DeathReason");
                    if (!string.IsNullOrEmpty(deathReason))
                    {
                        parts.Add($"死亡原因：{deathReason}");
                    }
                }
            }

            // 过去收入：锚定本次复核记录，NULL=无历史收入记录按 0.00 元显示；新值取本档案人均月收入
            var oldIncome = review.OldPerCapitaIncome ?? 0m;
            if (app.PerCapitaIncome != oldIncome)
            {
                parts.Add($"月人均收入由{FormatDecimal(oldIncome)}元变为{FormatDecimal(app.PerCapitaIncome)}元");
            }

            // 月保障金对比（决定句已删金额句，原/现保障金信息统一在此承载）
            var oldGuarantee = review.OldGuaranteeAmount;
            var newGuarantee = review.NewGuaranteeAmount ?? app.HouseholdMonthlyGuaranteeAmount;
            if (oldGuarantee.HasValue && oldGuarantee.Value != newGuarantee)
            {
                parts.Add($"月保障金由{FormatDecimal(oldGuarantee.Value)}元变为{FormatDecimal(newGuarantee)}元");
            }

            var oldCls = ClassificationConstants.ConvertToFullName(review.OldClassification ?? "");
            var newCls = ClassificationConstants.ConvertToFullName(review.NewClassification ?? "");
            if (!string.IsNullOrEmpty(oldCls) && !string.IsNullOrEmpty(newCls) && oldCls != newCls)
            {
                parts.Add($"类别由{oldCls}变为{newCls}");
            }
            else if (!string.IsNullOrEmpty(oldCls) && string.IsNullOrEmpty(newCls))
            {
                // 死亡/停保链：新类别为空表示旧档待遇终止
                parts.Add($"原{oldCls}待遇停止");
            }

            core = parts.Count > 0
                ? $"{reason}。变化情况：{string.Join("；", parts)}。"
                : $"{reason}。家庭情况较上期无明显变化。";
        }
        catch (Exception ex)
        {
            _logger.Warn($"定期复核变化内容拼句降级: ApplicationId={app.Id}, {ex.Message}");
            core = reason;
        }

        return core + await BuildReviewCalculationAsync(app);
    }

    /// <summary>
    /// 定期复核审批表：核算过程只留结论句（2026-10 整改：删口径括号、等式推导与低保补差算式——
    /// 只展示不重算，保障金属审批结果，见 AGENTS §7.5）。
    /// 低保标准单点读 IStandardConfigService；缺失/无效时 LogWarn 并省略该子句，禁止硬编码回退值。
    /// </summary>
    private async Task<string> BuildReviewCalculationAsync(ApplicationEntity app)
    {
        try
        {
            var isRural = ClassificationConstants.HukouType.IsHukouRural(app.HukouType);
            var currentCls = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "");
            var sb = new StringBuilder(160);

            sb.Append($"核算过程：家庭年收入{FormatDecimal(app.TotalAnnualIncome)}元，"
                + $"人均年收入{FormatDecimal(app.PerCapitaAnnualIncome)}元，"
                + $"人均月收入{FormatDecimal(app.PerCapitaIncome)}元；");

            var stdType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
            var stdHukou = isRural ? "Rural" : "Urban";
            var stdResult = await _standardConfigService.GetStandardValueAsync(stdType, stdHukou);
            if (stdResult.IsSuccess && stdResult.Value > 0)
            {
                sb.Append($"{(isRural ? "农村" : "城市")}低保标准{FormatDecimal(stdResult.Value)}元/月；");
            }
            else
            {
                _logger.Warn($"定期复核核算过程-低保标准缺失或无效，省略标准子句（{stdType}/{stdHukou}）: {stdResult.Message}");
            }

            sb.Append($"该家庭现类别{currentCls}，家庭月总收入{FormatDecimal(app.TotalFamilyIncome)}元，"
                + $"月保障金{FormatDecimal(app.HouseholdMonthlyGuaranteeAmount)}元。");
            return " " + sb;
        }
        catch (Exception ex)
        {
            _logger.Warn($"定期复核核算过程拼句降级: ApplicationId={app.Id}, {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 构建单人保情况
    /// </summary>
    private string BuildSingleRescueSituation(ApplicationEntity app)
    {
        if (app.ClassificationResult?.Contains("Single") == true)
        {
            var amount = FormatDecimal(app.HouseholdMonthlyGuaranteeAmount);
            return $"该家庭成员因存在重度残疾、重大疾病或三级智力/精神残疾等特殊情况，符合单人保相关政策，纳入单人保保障范围，月保障金额{amount}元。";
        }
        return "-";
    }

    /// <summary>
    /// 构建家庭财产豁免情况（列出具体豁免财产明细）
    /// </summary>
    private string BuildPropertyExemptionSituation(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        try
        {
            if (economicDetail == null)
                return "-";

            var exemptions = new List<string>();

            // 1. 车辆豁免
            if (economicDetail.Vehicles != null && economicDetail.Vehicles.Count > 0)
            {
                foreach (var v in economicDetail.Vehicles)
                {
                    var value = FormatDecimal(v.EstimatedValue);
                    exemptions.Add($"车辆：{v.Brand} {v.Model}（{v.LicensePlate}），估值{value}元，因生活必需用途予以豁免");
                }
            }

            // 2. 金融资产豁免
            if (economicDetail.FinancialAssets != null && economicDetail.FinancialAssets.Count > 0)
            {
                var fa = economicDetail.FinancialAssets[0];
                if (fa.HasBankDeposit && fa.BankDepositAmount > 0)
                {
                    exemptions.Add($"银行存款：{FormatDecimal(fa.BankDepositAmount)}元，因符合低保/低收入存款限额标准予以豁免");
                }
                if (fa.HasSecurities && fa.SecuritiesAmount > 0)
                {
                    exemptions.Add($"有价证券：{FormatDecimal(fa.SecuritiesAmount)}元，因符合低保/低收入证券限额标准予以豁免");
                }
            }

            // 3. 房产豁免
            if (economicDetail.FamilyProperties != null && economicDetail.FamilyProperties.Count > 0)
            {
                foreach (var p in economicDetail.FamilyProperties)
                {
                    var area = FormatDecimal(p.Area);
                    exemptions.Add($"房产：{p.Address}（{p.HousingStructure}，{area}㎡），因属唯一住房且面积符合标准予以豁免");
                }
            }

            if (exemptions.Count > 0)
            {
                return $"经核查，该家庭以下财产符合豁免条件：\n{string.Join("\n", exemptions.Select((e, i) => $"  {i + 1}. {e}"))}。";
            }

            return "-";
        }
        catch
        {
            return "-";
        }
    }

    /// <summary>
    /// 构建近亲属备案情况
    /// </summary>
    private string BuildKinshipFilingSituation()
    {
        return "";
    }
}
