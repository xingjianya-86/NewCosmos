using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace NewCosmos.ViewModels.TempRelief;

public partial class TempReliefFormViewModel
{
    /// <summary>
    /// 加载已有申请（编辑/查看模式）
    /// </summary>
    public async Task LoadAsync(long id)
    {
        _id = id;
        _suppressReload = true;
        var isDraft = false;
        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.GetByIdAsync(id, CancellationToken);
            if (result.IsFailure || result.Value == null)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "申请记录不存在");

            var app = result.Value;
            isDraft = string.Equals(app.Status, TempReliefConstants.StatusDraft, StringComparison.Ordinal);
            ReliefType = app.ReliefType;
            _sourceTable = app.SourceTable;
            _sourceFamilyId = app.SourceFamilyId;
            _hukouType = app.HukouType;

            ApplicantName = app.ApplicantName;
            ApplicantIdCard = app.ApplicantIdCard;
            ApplicantGender = app.Gender;
            ApplicantAge = app.Age;
            ApplicantPhone = app.Phone;
            ApplicantProvince = app.FamilyProvince;
            ApplicantCity = app.FamilyCity;
            ApplicantDistrict = app.FamilyDistrict;
            ApplicantTown = string.IsNullOrWhiteSpace(app.FamilyTown) ? app.Town : app.FamilyTown;
            ApplicantVillage = string.IsNullOrWhiteSpace(app.Village)
                ? TempReliefConstants.DefaultVillage
                : app.Village;
            ApplicantDetailAddress = app.FamilyDetail;
            ApplicantAddress = app.FamilyAddress;
            HukouProvince = app.HukouProvince;
            HukouCity = app.HukouCity;
            HukouDistrict = app.HukouDistrict;
            HukouTown = app.HukouTown;
            if (!isDraft)
            {
                ApplicantHukou = app.HukouAddress;
            }
            ApplicantFamilySize = app.FamilySize;
            ApplicantFamilyCategory = TempReliefConstants.NormalizeFamilyCategory(app.FamilyCategory);
            BankName = app.BankName;
            BankAccount = app.BankAccount;

            // 草稿读入：申请日期自动重置为当前日期（与新建一致）
            ApplyDate = isDraft ? DateTime.Today : (app.ApplyDate ?? DateTime.Today);
            DifficultyReason = app.DifficultyReason;
            DifficultyType = string.IsNullOrWhiteSpace(app.DifficultyType)
                ? TempReliefConstants.DifficultyTypeOther
                : app.DifficultyType;
            PolicyEnjoyed = app.PolicyEnjoyed;
            FamilyMemberStatus = app.FamilyMemberStatus;
            ReportUnit = app.ReportUnit;
            ReportTime = app.ReportTime;
            VerifyResult = app.VerifyResult;
            PublicizeStartDateValue = app.PublicizeStartDate ?? DateTime.Today;
            PublicizeEndDateValue = app.PublicizeEndDate ?? DateTime.Today;
            HasPublicizeDates = app.PublicizeStartDate.HasValue || app.PublicizeEndDate.HasValue;
            AcceptancePerson = app.AcceptancePerson;

            // 救助对象快照回显（旧数据为空 → 稍后默认户主补全）
            BeneficiaryName = app.BeneficiaryName ?? string.Empty;
            BeneficiaryIdCard = app.BeneficiaryIdCard ?? string.Empty;
            BeneficiaryGender = app.BeneficiaryGender ?? string.Empty;
            BeneficiaryAge = app.BeneficiaryAge;
            BeneficiaryRelation = app.BeneficiaryRelation ?? string.Empty;

            if (OperationMode == FormOperationMode.Edit)
            {
                IsCandidateSelected = true;
                CandidateSummary = $"已选择：{app.ApplicantName}（{TempReliefConstants.GetSourceName(app.SourceType)}·{TempReliefConstants.GetFamilyCategoryByTable(app.SourceTable)}）";
            }

            await LoadMembersAsync(id);
            // 成员装载后立即重建患病/受灾成员候选：必须早于疾病/灾害明细装载，
            // 否则明细行 Picker 的 ItemsSource 为空，已保存的 member_name 无法回显
            RebuildMemberNameOptions();
            await LoadDifficultyDetailsAsync(id);

            // 成员已加载 → 构建救助对象选项（优先匹配已存身份证；空则默认户主并补全快照）
            RebuildBeneficiaryOptions(string.IsNullOrWhiteSpace(BeneficiaryIdCard) ? null : BeneficiaryIdCard);

            return Result.Success();
        }, "加载申请信息...");
        _suppressReload = false;

        // 读入已有申请：救助原因取保存值，视为"已有内容"，类型/明细变化前不自动覆盖
        _reasonDirty = true;

        // 草稿读入：户籍地址按当前用户组织机构重新规划，公示日期按 C 线固定窗口重算
        if (isDraft)
        {
            await ResolveHukouFromOrganizationAsync(CancellationToken);
            await ApplyPublicizeDefaultsAsync();
        }
        else
        {
            // 非草稿：公示日期保留库中原值，仅把申请日期约束进其归属调查窗口（存量档案自愈，
            // 否则早于窗口起日的旧数据会被保存校验挡住）。须抑制联动，避免重算覆盖公示日期。
            _suppressReload = true;
            try
            {
                var simplified = TempReliefConstants.IsSimplifiedProcedure(ApplicantFamilyCategory, _sourceTable);
                ClampApplyDateIntoWindow(_serviceProvider.GetRequiredService<IBusinessTimelineService>()
                    .CalculateTempReliefForApplyDate(ApplyDate, simplified));
            }
            finally
            {
                _suppressReload = false;
            }
        }

        // 小额：加载档位并回显
        if (ReliefType == TempReliefConstants.ReliefTypeSmall)
        {
            await LoadSmallAmountLevelsAsync();
            var app0 = (await _applicationService.GetByIdAsync(id, CancellationToken)).Value;
            if (app0 != null && !string.IsNullOrEmpty(app0.SmallAmountLevel))
            {
                _suppressReload = true;
                SelectedSmallAmountLevel = app0.SmallAmountLevel;
                _confirmAmount = app0.ConfirmAmount ?? 0m;
                ConfirmAmountDisplay = _confirmAmount > 0 ? $"{_confirmAmount:F2} 元" : string.Empty;
                _suppressReload = false;
            }
        }
    }

    private async Task LoadMembersAsync(long id)
    {
        var membersResult = await _applicationService.GetMembersByApplicationIdAsync(id, CancellationToken);
        if (membersResult.IsFailure || membersResult.Value == null) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Members.Clear();
            foreach (var m in membersResult.Value)
            {
                if (IsApplicantItself(m)) continue;
                Members.Add(m);
            }
            if (Members.Count == 0)
            {
                Members.Add(new TempReliefMember());
            }
        });
    }

    /// <summary>
    /// 判定成员是否为户主本人（申请人）：身份证匹配优先，缺失时以关系+姓名兜底
    /// </summary>
    private bool IsApplicantItself(TempReliefMember m)
    {
        if (string.IsNullOrWhiteSpace(m.IdCard) && string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            return !string.IsNullOrWhiteSpace(m.Relation)
                && TempReliefConstants.GetRelationshipDisplayName(m.Relation) == TempReliefConstants.RelationHead
                && string.Equals(m.MemberName?.Trim(), ApplicantName?.Trim(), StringComparison.Ordinal);
        }
        return !string.IsNullOrWhiteSpace(m.IdCard)
            && string.Equals(m.IdCard.Trim(), ApplicantIdCard?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>同上，针对候选成员快照（TempReliefMemberSnapshot）</summary>
    private bool IsApplicantItself(TempReliefMemberSnapshot m)
    {
        if (string.IsNullOrWhiteSpace(m.IdCard) && string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            return !string.IsNullOrWhiteSpace(m.Relation)
                && TempReliefConstants.GetRelationshipDisplayName(m.Relation) == TempReliefConstants.RelationHead
                && string.Equals(m.MemberName?.Trim(), ApplicantName?.Trim(), StringComparison.Ordinal);
        }
        return !string.IsNullOrWhiteSpace(m.IdCard)
            && string.Equals(m.IdCard.Trim(), ApplicantIdCard?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadDifficultyDetailsAsync(long id)
    {
        var diseases = await _applicationService.GetDiseasesByApplicationIdAsync(id, CancellationToken);
        var accidents = await _applicationService.GetAccidentsByApplicationIdAsync(id, CancellationToken);
        var educations = await _applicationService.GetEducationsByApplicationIdAsync(id, CancellationToken);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Diseases.Clear();
            if (diseases.IsSuccess && diseases.Value != null)
                foreach (var d in diseases.Value) { SubscribeDetailChanges(d); Diseases.Add(d); }

            // 检测多医院模式：如果各条疾病记录的 Hospital 不全相同，则自动开启
            if (diseases.IsSuccess && diseases.Value != null && diseases.Value.Count > 1)
            {
                var distinctHospitals = diseases.Value
                    .Where(d => !string.IsNullOrWhiteSpace(d.Hospital))
                    .Select(d => d.Hospital!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                IsMultiHospitalMode = distinctHospitals.Count > 1;
            }
            else
            {
                IsMultiHospitalMode = false;
            }

            if (IsMultiHospitalMode)
            {
                // 多医院模式：每条记录各自保留值，共享字段留空
                Hospital = string.Empty;
            }
            else
            {
                // 共享模式：回填第一条非空记录到顶部共享字段
                Hospital = diseases.IsSuccess && diseases.Value != null
                    ? diseases.Value.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Hospital))?.Hospital ?? string.Empty
                    : string.Empty;

                if (diseases.IsSuccess && diseases.Value != null)
                {
                    var first = diseases.Value.FirstOrDefault();
                    if (first != null)
                    {
                        TreatStartDate = first.TreatStartDate == DateTime.MinValue ? DateTime.Today : first.TreatStartDate;
                        TreatEndDate = first.TreatEndDate == DateTime.MinValue ? DateTime.Today : first.TreatEndDate;
                        MedicalTotal = first.MedicalTotal;
                        InsurancePaid = first.InsurancePaid;
                        SelfPaid = first.SelfPaid;
                    }
                }
            }

            Accidents.Clear();
            if (accidents.IsSuccess && accidents.Value != null)
                foreach (var a in accidents.Value) { SubscribeDetailChanges(a); Accidents.Add(a); }

            Educations.Clear();
            if (educations.IsSuccess && educations.Value != null)
                foreach (var e in educations.Value) { SubscribeDetailChanges(e); Educations.Add(e); }
        });
    }

    [RelayCommand]
    private async Task SearchCandidatesAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            await _dialogService.DisplayAlertAsync("提示", "请输入姓名或身份证号进行检索", "确定");
            return;
        }

        if (IsBusy)
        {
            _logger.Warn("检索候选人被 IsBusy 占用拦截");
            await _dialogService.DisplayAlertAsync("提示", "正在处理其他操作，请稍候再试", "确定");
            return;
        }

        var kw = SearchKeyword.Trim();
        _logger.Info($"检索候选人开始: keyword={kw}");

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.SearchCandidatesAsync(kw, null, CancellationToken);
            if (result.IsFailure || result.Value == null)
            {
                _logger.Warn($"检索候选人失败: {result.ErrorCode} {result.Message}");
                await _dialogService.DisplayAlertAsync("提示", result.Message ?? "检索失败", "确定");
                return Result.Failure(ErrorCodes.NOT_FOUND, result.Message ?? "检索失败");
            }

            var list = result.Value;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Candidates.Clear();
                foreach (var c in list)
                {
                    Candidates.Add(c);
                }
            });

            _logger.Info($"检索候选人完成: keyword={kw}, 命中={list.Count}");
            if (list.Count == 0)
            {
                SafeFireAndForget(async () => await _dialogService.DisplayAlertAsync("提示", "未检索到符合条件的申请人", "确定"));
            }
            return Result.Success();
        }, "检索申请人...");
    }

    [RelayCommand]
    private async Task SelectCandidateAsync()
    {
        if (SelectedCandidate == null) return;

        if (SelectedCandidate.HasAppliedThisYear)
        {
            await _dialogService.DisplayAlertAsync("年度限制", $"该申请人本年度已申请过临时救助，每年仅限一次", "确定");
            return;
        }

        // 命中成员信息在详情接口不回传，选中瞬间先取（列表清除后不可再读）
        var matchedPersonInfo = SelectedCandidate.MatchedPersonInfo;
        var matchedMemberIdCard = SelectedCandidate.MatchedMemberIdCard;

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.GetCandidateDetailAsync(
                SelectedCandidate.SourceTable, SelectedCandidate.SourceFamilyId, null, CancellationToken);
            if (result.IsFailure || result.Value == null)
                return Result.Failure(ErrorCodes.NOT_FOUND, result.Message ?? "获取申请人详情失败");

            var c = result.Value;
            if (c.HasAppliedThisYear)
            {
                return Result.Failure(ErrorCodes.ANNUAL_LIMIT_EXCEEDED, $"该申请人本年度已申请过临时救助，每年仅限一次");
            }

            _sourceTable = c.SourceTable;
            _sourceFamilyId = c.SourceFamilyId;
            _hukouType = c.HukouType;

            ApplicantName = c.Name;
            ApplicantIdCard = c.IdCard;
            ApplicantGender = c.Gender;
            ApplicantPhone = c.Phone;
            ApplicantProvince = c.Province;
            ApplicantCity = c.City;
            ApplicantDistrict = c.District;
            ApplicantTown = c.Town;
            ApplicantVillage = string.IsNullOrWhiteSpace(c.Village)
                ? TempReliefConstants.DefaultVillage
                : c.Village;
            // 详细地址：从家庭住址（完整地址）提取村名之后的最后一部分，无则空
            ApplicantDetailAddress = ExtractDetailFromAddress(c.FamilyAddress);
            // 家庭住址文本保留来源原始完整地址（打印模板用，避免与分列拼接重复）
            ApplicantAddress = c.FamilyAddress;
            // 户籍地址统一从当前用户组织机构获取（新建/草稿时已回填，候选人不覆盖）
            ApplicantFamilySize = c.FamilySize;
            ApplicantFamilyCategory = TempReliefConstants.NormalizeFamilyCategory(c.FamilyCategory);
            // 家庭类别确定后重算公示窗口（特殊群体5个工作日/普通10个工作日会影响归属月份）
            await ApplyPublicizeDefaultsAsync();
            // 银行信息：来源台账预填（一卡通账号优先，其次银行卡号；开户行取 bank_name）
            BankName = c.BankName;
            BankAccount = string.IsNullOrWhiteSpace(c.OneCardAccount) ? c.BankAccount : c.OneCardAccount;

            var age = Helpers.IdCardValidator.ExtractAge(c.IdCard);
            if (age.HasValue)
                ApplicantAge = age.Value;

            // 选择新申请人：视为新表单，重置救助原因标志、困难类型与明细（避免沿用上一位申请人数据）
            _reasonDirty = false;
            _suppressReasonDirty = false;
            DifficultyType = TempReliefConstants.DifficultyTypeOther;
            Diseases.Clear();
            Accidents.Clear();
            Educations.Clear();
            // 救助原因：按家庭类别生成默认文本（其他困难类型）
            RegenerateDifficultyReason();

            if (string.IsNullOrWhiteSpace(PolicyEnjoyed))
            {
                var policyResult = await _applicationService.GetPolicySnapshotAsync(c.IdCard, CancellationToken);
                if (policyResult.IsSuccess && policyResult.Value is { Count: > 0 })
                    PolicyEnjoyed = string.Join("；", policyResult.Value) + "。";
            }

            if (string.IsNullOrWhiteSpace(FamilyMemberStatus))
            {
                var statusResult = await _applicationService.GetFamilyMemberStatusAsync(c.IdCard, CancellationToken);
                if (statusResult.IsSuccess && !string.IsNullOrWhiteSpace(statusResult.Value))
                    FamilyMemberStatus = statusResult.Value;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Members.Clear();
                if (c.Members != null && c.Members.Count > 0)
                {
                    foreach (var m in c.Members)
                    {
                        if (IsApplicantItself(m)) continue;
                        Members.Add(new TempReliefMember
                        {
                            MemberName = m.MemberName,
                            Gender = m.Gender,
                            Relation = m.Relation,
                            IdCard = m.IdCard,
                            WorkUnit = m.WorkUnit,
                            AnnualIncome = m.AnnualIncome
                        });
                    }
                }
                if (Members.Count == 0)
                {
                    Members.Add(new TempReliefMember());
                }

                // 救助对象：默认户主；按成员命中时预选该成员（无身份证则回退户主）
                RebuildBeneficiaryOptions(string.IsNullOrWhiteSpace(matchedMemberIdCard) ? null : matchedMemberIdCard);
                RebuildMemberNameOptions();

                IsCandidateSelected = true;
                CandidateSummary = string.IsNullOrEmpty(matchedPersonInfo)
                    ? $"已选择：{c.Name}（{c.SourceName}）"
                    : $"已选择：{c.Name}（{c.SourceName}）· {matchedPersonInfo}";
                Candidates.Clear();
            });

            _logger.LogBusiness("选择临时救助申请人",
                ("SourceTable", c.SourceTable), ("SourceFamilyId", c.SourceFamilyId),
                ("Name", Services.Core.DataMasker.MaskName(c.Name)));
            return Result.Success();
        }, "选择申请人...");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var validation = Validate();
        if (validation != null)
        {
            await _dialogService.DisplayAlertAsync("提示", validation, "确定");
            return;
        }

        await ExecuteAsync(async () =>
        {
            // 保存兜底：未手动编辑过救助原因时按当前困难类型与明细重新生成
            if (!_reasonDirty) RegenerateDifficultyReason();
            var app = BuildApplication();

            if (OperationMode == FormOperationMode.Create)
            {
                var result = await _applicationService.CreateAsync(app, Members.ToList(), CancellationToken);
                if (!result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
                _id = result.Value;
            }
            else
            {
                var result = await _applicationService.UpdateAsync(app, Members.ToList(), CancellationToken);
                if (!result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
            }

            _logger.LogBusiness("临时救助申请保存成功", ("ApplicationId", _id));
            await _dialogService.ShowSnackBarAsync("保存成功");
            await CloseAsync();
            return Result.Success();
        }, "保存申请...");
    }

    [RelayCommand]
    private async Task SaveAndConfirmAsync()
    {
        if (!IsSavable) return;

        var validation = Validate();
        if (validation != null)
        {
            await _dialogService.DisplayAlertAsync("提示", validation, "确定");
            return;
        }

        await ExecuteAsync(async () =>
        {
            // 保存兜底：未手动编辑过救助原因时按当前困难类型与明细重新生成
            if (!_reasonDirty) RegenerateDifficultyReason();
            var app = BuildApplication();

            if (OperationMode == FormOperationMode.Create)
            {
                var result = await _applicationService.CreateAsync(app, Members.ToList(), CancellationToken);
                if (!result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
                _id = result.Value;
            }
            else
            {
                var result = await _applicationService.UpdateAsync(app, Members.ToList(), CancellationToken);
                if (!result.IsSuccess)
                {
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
            }

            var confirmResult = await _applicationService.ConfirmAsync(_id, App.CurrentUserName, CancellationToken);
            if (!confirmResult.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("确认失败", confirmResult.Message ?? "确认失败，请重试", "确定");
                return confirmResult;
            }

            _logger.LogBusiness("临时救助申请保存并确认成功", ("ApplicationId", _id));
            await _dialogService.ShowSnackBarAsync("保存并确认成功");

            // 保存确认成功 → 跳转档案输出页（复用 ArchiveOutputPage）
            var savedResult = await _applicationService.GetByIdAsync(_id, CancellationToken);
            if (savedResult.IsSuccess && savedResult.Value != null)
            {
                var savedApp = savedResult.Value;

                // 加载困难明细子表（GetByIdAsync 仅主表，打印需携带明细）
                var diseaseResult = await _applicationService.GetDiseasesByApplicationIdAsync(_id, CancellationToken);
                if (diseaseResult.IsSuccess && diseaseResult.Value != null)
                    savedApp.Diseases = diseaseResult.Value;
                var accidentResult = await _applicationService.GetAccidentsByApplicationIdAsync(_id, CancellationToken);
                if (accidentResult.IsSuccess && accidentResult.Value != null)
                    savedApp.Accidents = accidentResult.Value;
                var educationResult = await _applicationService.GetEducationsByApplicationIdAsync(_id, CancellationToken);
                if (educationResult.IsSuccess && educationResult.Value != null)
                    savedApp.Educations = educationResult.Value;

                var membersResult = await _applicationService.GetMembersByApplicationIdAsync(_id, CancellationToken);
                var memberList = membersResult.IsSuccess ? membersResult.Value ?? new() : new List<TempReliefMember>();
                var contactUnitPhone = (await _applicationService.GetReportUnitPhoneAsync(savedApp.ReportUnit, CancellationToken)).Value ?? string.Empty;
                var timelineService = _serviceProvider.GetRequiredService<IBusinessTimelineService>();
                var (acceptanceDate, investigationDate) = await TempReliefPrintDataBuilder.ResolveScheduleAsync(timelineService, savedApp);
                var fields = TempReliefPrintDataBuilder.BuildSingleFields(
                    savedApp, memberList, contactUnitPhone, acceptanceDate, investigationDate);

                // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
                PrintNavigationData.ClearDocumentMode();
                PrintNavigationData.BusinessType = TempReliefConstants.BusinessType;
                PrintNavigationData.BusinessId = _id;
                PrintNavigationData.Classification = savedApp.ReliefType;
                PrintNavigationData.FieldData = fields;
                PrintNavigationData.TableData = new();
                PrintNavigationData.SupporterTableData = null;

                await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
            }
            else
            {
                await CloseAsync();
            }
            return Result.Success();
        }, "保存并确认...");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await CloseAsync();
    }

    /// <summary>
    /// 救助原因默认文本：以家庭类别填空（类别来自来源表/低保认定分类；取不到时用"困难群众"）
    /// </summary>
    private static string BuildDefaultDifficultyReason(string? familyCategory)
    {
        var category = string.IsNullOrWhiteSpace(familyCategory) ? "困难群众" : familyCategory;
        return $"申请人家庭为{category}对象，因突发困难导致基本生活暂时陷入困境，特申请临时救助。";
    }

    /// <summary>
    /// 救助原因自动生成入口：抑制 dirty 标记后按当前困难类型与明细重新生成
    /// </summary>
    private void RegenerateDifficultyReason()
    {
        _suppressReasonDirty = true;
        DifficultyReason = BuildDifficultyReasonText();
        _suppressReasonDirty = false;
    }

    /// <summary>
    /// 按困难类型及其明细生成救助原因文本；
    /// 疾病/意外灾害/教育支出类型在明细未填时也生成体现该类型的特征文本（切换类型立即可见变化）
    /// </summary>
    private string BuildDifficultyReasonText()
    {
        switch (DifficultyType)
        {
            case TempReliefConstants.DifficultyTypeDisease:
                var diseaseText = BuildDiseaseReasonText();
                if (!string.IsNullOrWhiteSpace(diseaseText)) return diseaseText;
                return BuildFallbackReasonHead() + "因患疾病，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。";
            case TempReliefConstants.DifficultyTypeAccident:
                var accidentText = BuildAccidentReasonText();
                if (!string.IsNullOrWhiteSpace(accidentText)) return accidentText;
                return BuildFallbackReasonHead() + "因意外灾害，家庭基本生活暂时陷入困境，特申请临时救助。";
            case TempReliefConstants.DifficultyTypeEducation:
                var educationText = BuildEducationReasonText();
                if (!string.IsNullOrWhiteSpace(educationText)) return educationText;
                return BuildFallbackReasonHead() + "因家庭子女就学，教育支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。";
        }
        return BuildDefaultDifficultyReason(ApplicantFamilyCategory);
    }

    /// <summary>
    /// 救助原因句主语：单个对象用其姓名，多对象或取不到姓名时仅"申请人家庭成员"（避免"申请人家庭成员家庭成员"叠字）
    /// </summary>
    private string BuildReasonHead(IEnumerable<string> ownerNames)
    {
        var names = ownerNames
            .Where(n => !string.IsNullOrWhiteSpace(n) && n != "家庭成员")
            .Select(n => n.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (names.Count == 1) return $"申请人家庭成员{names[0]}";
        if (names.Count > 1) return $"申请人家庭成员{string.Join("、", names)}";
        return "申请人家庭成员";
    }

    /// <summary>明细未填时的兜底句主语：申请人姓名可用时点名，否则仅"申请人家庭成员"</summary>
    private string BuildFallbackReasonHead()
        => string.IsNullOrWhiteSpace(ApplicantName) ? "申请人家庭成员" : $"申请人家庭成员{ApplicantName.Trim()}";

    /// <summary>
    /// 疾病救助原因（详细）：逐条列病名与编码；共享模式取顶部共享值，多医院模式逐条取记录值
    /// </summary>
    private string BuildDiseaseReasonText()
    {
        var diseases = Diseases
            .Where(d => !string.IsNullOrWhiteSpace(d.DiseaseName) || !string.IsNullOrWhiteSpace(d.DiseaseCode))
            .ToList();
        if (diseases.Count == 0) return string.Empty;

        // 患病人：明细归属成员优先，空则回退申请人（与审批表精简句口径一致）
        string SickName(TempReliefDisease d)
            => !string.IsNullOrWhiteSpace(d.MemberName)
                ? d.MemberName.Trim()
                : (string.IsNullOrWhiteSpace(ApplicantName) ? "家庭成员" : ApplicantName.Trim());

        var sickNames = diseases.Select(SickName).Distinct(StringComparer.Ordinal).ToList();
        // 主语：单一患病人点名；多患病人由句首"申请人家庭成员"+逐条前缀承担，句首不叠名
        var head = BuildReasonHead(sickNames.Count == 1 ? sickNames : Enumerable.Empty<string>());
        // 明细前缀：同一患病人（或主语已点名）不重复前缀；多患病人逐条标注归属
        string ItemOwner(TempReliefDisease d)
            => sickNames.Count == 1 ? string.Empty : OwnerPrefix(SickName(d));

        // 病名（含编码），多条时逐条列举
        var diseaseText = diseases.Count == 1
            ? diseases[0].DiseaseName
                + (string.IsNullOrWhiteSpace(diseases[0].DiseaseCode) ? "" : $"（ICD-10编码：{diseases[0].DiseaseCode}）")
            : "以下疾病：" + string.Join("；", diseases.Select((d, i) =>
                $"{i + 1}、{ItemOwner(d)}{d.DiseaseName}" + (string.IsNullOrWhiteSpace(d.DiseaseCode) ? "" : $"（编码{d.DiseaseCode}）")));

        if (IsMultiHospitalMode)
        {
            // 多医院模式：每条疾病与其医院/日期/费用合并为一条，编号 1、2… 逐条列出，避免两套编号混淆
            var segments = diseases.Select((d, i) =>
            {
                var diseasePart = ItemOwner(d) + d.DiseaseName
                    + (string.IsNullOrWhiteSpace(d.DiseaseCode) ? "" : $"（编码{d.DiseaseCode}）");
                var hospital = string.IsNullOrWhiteSpace(d.Hospital) ? "医院" : d.Hospital;
                var date = FormatTreatRange(d.TreatStartDate, d.TreatEndDate);
                var cost = BuildSingleDiseaseCostText(d);
                var seg = string.IsNullOrWhiteSpace(date)
                    ? $"于{hospital}住院治疗"
                    : $"于{date}在{hospital}住院治疗";
                if (!string.IsNullOrWhiteSpace(cost)) seg += $"，{cost}";
                return $"{diseasePart}，{seg}";
            }).ToList();

            var body = segments.Count == 1
                ? $"因患{segments[0]}"
                : "因患以下疾病：" + string.Join("；", segments.Select((s, i) => $"{i + 1}、{s}"));

            return $"{head}{body}，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。";
        }
        else
        {
            // 共享模式：取顶部共享值
            var dateText = FormatTreatRange(TreatStartDate, TreatEndDate);
            var hospitalText = string.IsNullOrWhiteSpace(Hospital) ? "医院" : Hospital;

            var body = string.IsNullOrWhiteSpace(dateText)
                ? $"于{hospitalText}住院治疗"
                : $"于{dateText}在{hospitalText}住院治疗";

            var costText = BuildDiseaseCostText();
            var tail = string.IsNullOrWhiteSpace(costText)
                ? "，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。"
                : $"，{costText}，个人自付费用较高，造成家庭基本生活暂时陷入困境，特申请临时救助。";

            return $"{head}因患{diseaseText}，{body}{tail}";
        }
    }

    /// <summary>
    /// 疾病费用构成文本（共享值，缺省段省略）；均未填时返回空串
    /// </summary>
    private string BuildDiseaseCostText()
    {
        var parts = new List<string>();
        if (MedicalTotal.HasValue) parts.Add($"本次医疗费用总额{MedicalTotal.Value:F2}元");
        if (InsurancePaid.HasValue) parts.Add($"经基本医疗保险及大病保险等报销{InsurancePaid.Value:F2}元");
        if (SelfPaid.HasValue) parts.Add($"个人自付{SelfPaid.Value:F2}元");
        return string.Join("，", parts);
    }

    /// <summary>单条疾病费用构成文本（多医院模式逐条用）</summary>
    private static string BuildSingleDiseaseCostText(TempReliefDisease d)
    {
        var parts = new List<string>();
        if (d.MedicalTotal.HasValue) parts.Add($"费用总额{d.MedicalTotal.Value:F2}元");
        if (d.InsurancePaid.HasValue) parts.Add($"报销{d.InsurancePaid.Value:F2}元");
        if (d.SelfPaid.HasValue) parts.Add($"自付{d.SelfPaid.Value:F2}元");
        return string.Join("，", parts);
    }

    /// <summary>明细条目归属成员姓名前缀（有值时"姓名 "，否则空）</summary>
    private static string OwnerPrefix(string? memberName)
        => string.IsNullOrWhiteSpace(memberName) ? "" : memberName.Trim() + " ";

    /// <summary>
    /// 意外灾害救助原因：按明细逐条列类型/时间/地点/伤亡/损失赔偿
    /// </summary>
    private string BuildAccidentReasonText()
    {
        var accidents = Accidents
            .Where(a => !string.IsNullOrWhiteSpace(a.AccidentType)
                || a.HappenDate != DateTime.MinValue
                || !string.IsNullOrWhiteSpace(a.HappenPlace)
                || !string.IsNullOrWhiteSpace(a.InjurySituation))
            .ToList();
        if (accidents.Count == 0) return string.Empty;

        // 受灾人：明细归属成员优先，空则回退申请人
        string AccidentOwner(TempReliefAccident a)
            => !string.IsNullOrWhiteSpace(a.MemberName)
                ? a.MemberName.Trim()
                : (string.IsNullOrWhiteSpace(ApplicantName) ? "家庭成员" : ApplicantName.Trim());

        var owners = accidents.Select(AccidentOwner).Distinct(StringComparer.Ordinal).ToList();
        var head = BuildReasonHead(owners.Count == 1 ? owners : Enumerable.Empty<string>());
        string ItemOwner(TempReliefAccident a)
            => owners.Count == 1 ? string.Empty : OwnerPrefix(AccidentOwner(a));

        var items = accidents.Select(a =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(a.AccidentType)) parts.Add(ItemOwner(a) + a.AccidentType);
            else if (!string.IsNullOrWhiteSpace(a.MemberName)) parts.Add(a.MemberName);
            if (a.HappenDate != DateTime.MinValue) parts.Add(a.HappenDate.ToString("yyyy年M月d日"));
            if (!string.IsNullOrWhiteSpace(a.HappenPlace)) parts.Add(a.HappenPlace);
            var seg = string.Join("，", parts);
            if (!string.IsNullOrWhiteSpace(a.InjurySituation)) seg += $"（{a.InjurySituation}）";
            var lossParts = new List<string>();
            if (a.PropertyLoss.HasValue) lossParts.Add($"财产损失{a.PropertyLoss.Value:F2}元");
            if (a.CompensationPaid.HasValue) lossParts.Add($"已获保险及赔偿{a.CompensationPaid.Value:F2}元");
            if (lossParts.Count > 0) seg += "，" + string.Join("，", lossParts);
            return seg;
        }).ToList();

        // 事件描述可能自带"因"前缀，重复前置会拼出"因因…"（与 FamilySituationTextBuilder 同类防护）
        var body = items.Count == 1
            ? (items[0].StartsWith("因", StringComparison.Ordinal) ? items[0] : $"因{items[0]}")
            : "因发生以下意外灾害：" + string.Join("；", items.Select((s, i) => $"{i + 1}、{s}"));

        return $"{head}{body}，家庭基本生活暂时陷入困境，特申请临时救助。";
    }

    /// <summary>
    /// 教育支出救助原因：按明细逐条列学生/阶段/学校/学费
    /// </summary>
    private string BuildEducationReasonText()
    {
        var educations = Educations
            .Where(e => !string.IsNullOrWhiteSpace(e.StudentName)
                || !string.IsNullOrWhiteSpace(e.EducationStage)
                || !string.IsNullOrWhiteSpace(e.SchoolName)
                || e.TuitionFee.HasValue)
            .ToList();
        if (educations.Count == 0) return string.Empty;

        // 就学生：明细学生姓名优先，空则回退申请人
        string StudentOf(TempReliefEducation e)
            => !string.IsNullOrWhiteSpace(e.StudentName)
                ? e.StudentName.Trim()
                : (string.IsNullOrWhiteSpace(ApplicantName) ? "家庭成员" : ApplicantName.Trim());

        var students = educations.Select(StudentOf).Distinct(StringComparer.Ordinal).ToList();
        var head = BuildReasonHead(students.Count == 1 ? students : Enumerable.Empty<string>());

        var items = educations.Select(e =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(e.StudentName)) parts.Add(e.StudentName);
            if (!string.IsNullOrWhiteSpace(e.EducationStage)) parts.Add($"就读{e.EducationStage}");
            if (!string.IsNullOrWhiteSpace(e.SchoolName)) parts.Add(e.SchoolName);
            if (!string.IsNullOrWhiteSpace(e.SchoolDurationDisplay)) parts.Add(e.SchoolDurationDisplay);
            var text = string.Join("，", parts);
            if (e.TuitionFee.HasValue) text += $"（学费{e.TuitionFee.Value:F2}元）";
            return text;
        }).ToList();

        var body = items.Count == 1
            ? $"因家庭子女就学，{items[0]}"
            : "因家庭子女就学，教育支出明细如下：" + string.Join("；", items.Select((s, i) => $"{i + 1}、{s}"));

        return $"{head}{body}，学费等教育支出较大，家庭基本生活暂时陷入困境，特申请临时救助。";
    }

    /// <summary>
    /// 治疗起止日期区间文本；均未填返回空串；起止同日时仅显示单日
    /// </summary>
    private static string FormatTreatRange(DateTime start, DateTime end)
    {
        if (start == DateTime.MinValue && end == DateTime.MinValue) return string.Empty;
        if (start == DateTime.MinValue) return end.ToString("yyyy年M月d日");
        if (end == DateTime.MinValue) return start.ToString("yyyy年M月d日");
        if (start.Date == end.Date) return start.ToString("yyyy年M月d日");
        return $"{start:yyyy年M月d日}至{end:yyyy年M月d日}";
    }

    private string? Validate()
    {
        if (!IsCandidateSelected && string.IsNullOrEmpty(ApplicantName))
            return "请先从导入台账或低保申请库中选择申请人";

        if (string.IsNullOrEmpty(ApplicantIdCard))
            return "申请人身份证号缺失";

        if (string.IsNullOrWhiteSpace(BeneficiaryIdCard) || string.IsNullOrWhiteSpace(BeneficiaryName))
            return "请选择申请成员作为救助对象";

        if (string.IsNullOrWhiteSpace(DifficultyReason))
            return "请填写申请救助原因/困难情况";

        if (DifficultyType == TempReliefConstants.DifficultyTypeDisease)
        {
            if (Diseases.Count == 0)
                return "困难类型为疾病，请至少添加一条疾病明细";
            if (Diseases.Any(d => string.IsNullOrWhiteSpace(d.DiseaseName) && string.IsNullOrWhiteSpace(d.DiseaseCode)))
                return "请填写每条疾病明细的诊断结果或疾病编码";
            if (IsMultiHospitalMode)
            {
                // 多医院模式：每条须填医院，每条自付不能为负
                if (Diseases.Any(d => string.IsNullOrWhiteSpace(d.Hospital)))
                    return "多医院模式下，请为每条疾病明细填写所在医院";
                if (Diseases.Any(d => d.SelfPaid.HasValue && d.SelfPaid.Value < 0))
                    return "自费费用不能为负数";
            }
            else
            {
                // 共享模式：原有验证
                if (SelfPaid.HasValue && SelfPaid.Value < 0)
                    return "自费费用不能为负数";
            }
        }

        if (DifficultyType == TempReliefConstants.DifficultyTypeAccident && Accidents.Count == 0)
            return "困难类型为意外灾害，请至少添加一条意外灾害明细";

        if (DifficultyType == TempReliefConstants.DifficultyTypeEducation && Educations.Count == 0)
            return "困难类型为教育支出，请至少添加一条教育支出明细";

        if (ReliefType == TempReliefConstants.ReliefTypeSmall && string.IsNullOrEmpty(SelectedSmallAmountLevel))
            return "小额快速救助请选择定额档位";

        if (HasPublicizeDates && PublicizeEndDateValue < PublicizeStartDateValue)
            return "公示结束日期不能早于开始日期";

        // 申请日期：不能晚于今天，且须落在归属调查窗口 [起日, 截止日]
        // （下界与申请日期约束同用"起日 ≤ 今天"这道闸：窗口未开启时不校验，今天新建不受挡）
        if (ApplyDate.Date > DateTime.Today)
            return "申请日期不能晚于今天";

        var cLine = _serviceProvider.GetRequiredService<IBusinessTimelineService>()
            .CalculateTempReliefForApplyDate(
                ApplyDate, TempReliefConstants.IsSimplifiedProcedure(ApplicantFamilyCategory, _sourceTable));
        if (ApplyDate.Date > cLine.InvestigationDeadline.Date)
            return $"申请日期须早于或等于调查截止日 {cLine.InvestigationDeadline:yyyy-MM-dd}";
        if (cLine.InvestigationStartDate.Date <= DateTime.Today && ApplyDate.Date < cLine.InvestigationStartDate.Date)
            return $"申请日期须在入户调查窗口 {cLine.InvestigationStartDate:yyyy-MM-dd} 至 {cLine.InvestigationDeadline:yyyy-MM-dd} 之间";

        return null;
    }

    private TempReliefApplication BuildApplication()
    {
        var app = new TempReliefApplication
        {
            Id = _id,
            ReliefType = ReliefType,
            SourceType = TempReliefConstants.IsAllowedSourceTable(_sourceTable) && _sourceTable == "nc_biz_applications"
                ? TempReliefConstants.SourceApplication
                : TempReliefConstants.SourceImportTab,
            SourceTable = _sourceTable,
            SourceFamilyId = _sourceFamilyId,
            HukouType = _hukouType,
            ApplicantName = ApplicantName,
            ApplicantIdCard = ApplicantIdCard,
            Gender = ApplicantGender,
            Age = ApplicantAge,
            Phone = ApplicantPhone,
            FamilyProvince = ApplicantProvince,
            FamilyCity = ApplicantCity,
            FamilyDistrict = ApplicantDistrict,
            FamilyTown = ApplicantTown,
            FamilyDetail = ApplicantDetailAddress,
            FamilyAddress = ApplicantAddress,
            HukouProvince = HukouProvince,
            HukouCity = HukouCity,
            HukouDistrict = HukouDistrict,
            HukouTown = HukouTown,
            HukouAddress = BuildHukouAddress(),
            Town = ApplicantTown,
            Village = ApplicantVillage,
            FamilySize = ApplicantFamilySize,
            FamilyCategory = ApplicantFamilyCategory,
            BankName = BankName?.Trim() ?? string.Empty,
            BankAccount = BankAccount?.Trim() ?? string.Empty,
            PolicyEnjoyed = PolicyEnjoyed,
            FamilyMemberStatus = FamilyMemberStatus,
            DifficultyReason = DifficultyReason,
            DifficultyType = string.IsNullOrWhiteSpace(DifficultyType)
                ? TempReliefConstants.DifficultyTypeOther
                : DifficultyType,
            ApplyDate = ApplyDate,
            Diseases = Diseases.ToList(),
            Accidents = Accidents.ToList(),
            Educations = Educations.ToList(),
            ReportUnit = ReportUnit,
            ReportTime = ReportTime,
            VerifyResult = VerifyResult,
            PublicizeStartDate = HasPublicizeDates ? PublicizeStartDateValue : null,
            PublicizeEndDate = HasPublicizeDates ? PublicizeEndDateValue : null,
            AcceptancePerson = AcceptancePerson,
            BeneficiaryName = BeneficiaryName,
            BeneficiaryIdCard = BeneficiaryIdCard,
            BeneficiaryGender = BeneficiaryGender,
            BeneficiaryAge = BeneficiaryAge,
            BeneficiaryRelation = BeneficiaryRelation
        };

        // 医院为明细区顶部共享值：仅共享模式保存前写入每条疾病明细
        if (!IsMultiHospitalMode)
        {
            foreach (var d in app.Diseases)
            {
                d.Hospital = Hospital;
            }

            // 治疗起止/费用/报销/自付为明细区顶部共享值：保存前写入每条疾病明细
            foreach (var d in app.Diseases)
            {
                d.TreatStartDate = TreatStartDate;
                d.TreatEndDate = TreatEndDate;
                d.MedicalTotal = MedicalTotal;
                d.InsurancePaid = InsurancePaid;
                d.SelfPaid = SelfPaid;
            }
        }
        // 多医院模式下，每条记录已各自填写，无需覆盖

        if (ReliefType == TempReliefConstants.ReliefTypeSmall)
        {
            app.SmallAmountLevel = SelectedSmallAmountLevel;
            app.ConfirmAmount = _confirmAmount > 0 ? _confirmAmount : null;
        }
        else
        {
            // 大额救助金额不填写
            app.SmallAmountLevel = string.Empty;
            app.ConfirmAmount = null;
        }

        return app;
    }

    private async Task CloseAsync()
    {
        await Helpers.WindowNavigator.CurrentPage!.Navigation.PopAsync();
        RestoreWindowTitleFromNavigation();
    }
}
