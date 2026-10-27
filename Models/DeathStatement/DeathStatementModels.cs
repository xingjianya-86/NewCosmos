namespace NewCosmos.Models.DeathStatement;

/// <summary>
/// 本村数据范围（死亡情况说明唯一过滤口径）。
/// 由当前登录用户 org 解析（IOrganizationService.GetByIdAsync），账号未绑定村社区时解析失败——
/// 调用方必须显式失败，禁止静默放开全库。
/// </summary>
public sealed class VillageScope
{
    /// <summary>
    /// 查询层级：账号绑了村 → 村级（VillageId 有效）；只绑到乡镇 → 镇级降级（IsTownScope=true，按 town_id 过滤）。
    /// 两级都没有 → ResolveScopeAsync 显式失败。
    /// </summary>
    public bool IsTownScope { get; set; }

    /// <summary>所属村/社区ID（nc_regions_villages.id；镇级降级时为 0）</summary>
    public int VillageId { get; set; }

    /// <summary>所属村/社区名称（原文，展示用；镇级降级时为空）</summary>
    public string VillageName { get; set; } = string.Empty;

    /// <summary>村名核心（去「村民委员会/村委会/社区」等后缀，文本村字段归一化比对用）</summary>
    public string VillageCoreName { get; set; } = string.Empty;

    /// <summary>所属乡镇ID</summary>
    public int? TownId { get; set; }

    /// <summary>所属乡镇名称（展示用）</summary>
    public string TownName { get; set; } = string.Empty;

    /// <summary>所属省名称（组织 province_id JOIN 而来；落款 {所在省} 用，未填为空）</summary>
    public string ProvinceName { get; set; } = string.Empty;

    /// <summary>所属市名称（组织 CityName；落款 {所在市}）</summary>
    public string CityName { get; set; } = string.Empty;

    /// <summary>所属县区名称（组织 CountyName；落款 {所在县区} 用，FieldKey 复用既有 DISTRICT）</summary>
    public string DistrictName { get; set; } = string.Empty;

    /// <summary>所属单位名称（文书落款）</summary>
    public string OrgName { get; set; } = string.Empty;

    /// <summary>所属单位联系电话（文书落款）</summary>
    public string OrgPhone { get; set; } = string.Empty;

    /// <summary>页头范围徽标文案</summary>
    public string ScopeLabel => IsTownScope
        ? $"乡镇：{TownName}（全镇范围）"
        : $"村/社区：{VillageName}";
}

/// <summary>死亡情况说明列表行（申报台账 / 档案死亡登记双源合并）</summary>
public sealed class DeathSearchItem
{
    /// <summary>申报台账ID（来自档案登记的候选行为 null）</summary>
    public long? DeclarationId { get; set; }

    /// <summary>死亡登记ID（nc_biz_death_records.id）</summary>
    public long? DeathRecordId { get; set; }

    /// <summary>关联档案ID</summary>
    public long? ApplicationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;

    /// <summary>死亡日期（yyyy-MM-dd）</summary>
    public string DeathDateText { get; set; } = string.Empty;

    /// <summary>死亡时刻（HH:mm；未采集为空）</summary>
    public string DeathTimeText { get; set; } = string.Empty;

    public string DeclaredByName { get; set; } = string.Empty;
    public string DeclaredByType { get; set; } = string.Empty;
    public string DeclaredAtText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;

    /// <summary>数据来源（申报台账 / 档案死亡登记）</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>是否有有效申报</summary>
    public bool HasDeclaration { get; set; }

    /// <summary>列表副标题（申报人·申报时间 或 「未申报」）</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>所属村/社区（档案口径，展示核对用）</summary>
    public string VillageName { get; set; } = string.Empty;

    /// <summary>与户主关系（档案口径）</summary>
    public string Relation { get; set; } = string.Empty;

    public string DeathReason { get; set; } = string.Empty;
    public string DeathCertNo { get; set; } = string.Empty;

    /// <summary>行内角标（已申报/未申报/已作废）</summary>
    public string Badge { get; set; } = string.Empty;
}

/// <summary>死者在库身份（七源聚合，仅本村命中）</summary>
public sealed class DeathIdentityItem
{
    /// <summary>身份类别（低保对象/普惠高龄/残疾人…）</summary>
    public string TypeLabel { get; set; } = string.Empty;

    /// <summary>状态（在享/已停发/在读…）</summary>
    public string StatusLabel { get; set; } = string.Empty;

    /// <summary>享受起止（yyyy-MM 至今）</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>数据来源模块</summary>
    public string SourceLabel { get; set; } = string.Empty;

    /// <summary>备注（金额/等级/学校等）</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>源标识（Linkage 判定用：Archive/Elderly/Disability/College/TempRelief/NearRelative/Recovery）</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>是否本村命中（跨村记录不进表，只计数进提醒）</summary>
    public bool InVillage { get; set; } = true;

    public long? ApplicationId { get; set; }

    /// <summary>源记录ID（高龄=elderly_applications.id / 临救=temp_relief.id / 追缴=recovery.id / 大学生=college_students.id / 近亲属=links.id）</summary>
    public long? SourceRecordId { get; set; }
}

/// <summary>数据信息提醒（界面列表 + 文书附段共用同一份规则）</summary>
public sealed class DeathReminderItem
{
    /// <summary>级别：警告 / 提示</summary>
    public string Level { get; set; } = "提示";

    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>建议动作文案</summary>
    public string SuggestAction { get; set; } = string.Empty;

    /// <summary>导航键（空=不可跳转）</summary>
    public string NavKey { get; set; } = string.Empty;
}

/// <summary>跨流程联动卡片（只读视图 + 预填跳转，不跨域写）</summary>
public sealed class DeathLinkAction
{
    /// <summary>业务域（Elderly/ArchiveHead/ArchiveMember/College/TempRelief/NearRelative/Recovery）</summary>
    public string Domain { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>当前状态文案</summary>
    public string StateText { get; set; } = string.Empty;

    /// <summary>是否已办（目标域自身状态判定）</summary>
    public bool IsDone { get; set; }

    /// <summary>动作文案（去办理 / 已完成）</summary>
    public string ActionText { get; set; } = string.Empty;

    /// <summary>导航键（ElderlyStop / HouseholdDeath / MemberChange / College / Recovery / NearRelative / None）</summary>
    public string NavKey { get; set; } = "None";

    public long? ApplicationId { get; set; }
    public long? ElderlyApplicationId { get; set; }

    /// <summary>预填：死亡日期（yyyy-MM-dd）</summary>
    public string DeathDateText { get; set; } = string.Empty;

    /// <summary>预填：死亡时刻（HH:mm）</summary>
    public string DeathTimeText { get; set; } = string.Empty;

    public string DeathReason { get; set; } = string.Empty;
    public string DeathCertNo { get; set; } = string.Empty;
}

/// <summary>死亡情况说明上下文（文书字段装配 + 页面展示的唯一数据源）</summary>
public sealed class DeathStatementContext
{
    #region 死者

    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string DeathDateText { get; set; } = string.Empty;
    public string DeathTimeText { get; set; } = string.Empty;
    public string DeathDateTimeText { get; set; } = string.Empty;
    public string DeathReason { get; set; } = string.Empty;
    public string DeathCertNo { get; set; } = string.Empty;
    public string DeathPlace { get; set; } = string.Empty;

    /// <summary>与户主关系（档案口径）</summary>
    public string Relation { get; set; } = string.Empty;

    public string VillageName { get; set; } = string.Empty;
    public string TownName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;

    /// <summary>死者性别（档案 Gender；为空时由 VM 用身份证推导兜底）</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>死者民族（档案 Ethnicity 已转显示名；为空即档案无记录）</summary>
    public string Ethnicity { get; set; } = string.Empty;

    public long? ApplicationId { get; set; }
    public long? DeclarationId { get; set; }
    public long? DeathRecordId { get; set; }

    #endregion

    #region 申报

    public string DeclaredByName { get; set; } = string.Empty;
    public string DeclaredByIdCard { get; set; } = string.Empty;
    public string DeclaredByPhone { get; set; } = string.Empty;
    public string DeclaredByType { get; set; } = string.Empty;
    public string DeclaredRelation { get; set; } = string.Empty;
    public string DeclaredAtText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;

    // ── 核实说明要素（2026-10-26）──
    public string ReporterName { get; set; } = string.Empty;
    public string ReporterRelation { get; set; } = string.Empty;
    public string ReporterPhone { get; set; } = string.Empty;
    public string SituationDesc { get; set; } = string.Empty;
    public string RemainsArrangement { get; set; } = string.Empty;
    public string BusinessAgent { get; set; } = string.Empty;
    public string BusinessMatter { get; set; } = string.Empty;

    /// <summary>是否有有效申报</summary>
    public bool HasDeclaration { get; set; }

    #endregion

    #region 集合

    public List<DeathIdentityItem> Identities { get; set; } = new();
    public List<DeathReminderItem> Reminders { get; set; } = new();
    public List<DeathLinkAction> Links { get; set; } = new();

    /// <summary>跨村被剔除的身份记录数（只计数进提醒，不输出明细）</summary>
    public int CrossVillageCount { get; set; }

    public bool HasIdentities => Identities.Count > 0;
    public bool HasReminders => Reminders.Count > 0;
    public bool HasLinks => Links.Count > 0;

    /// <summary>未办联动数（首页横幅与页面徽标同口径）</summary>
    public int PendingLinkCount => Links.Count(l => !l.IsDone && l.NavKey != "None");

    #endregion
}

/// <summary>提交申报请求</summary>
public sealed class DeathDeclareRequest
{
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public DateTime DeathDate { get; set; } = DateTime.Today;

    /// <summary>死亡确切时刻（必填）</summary>
    public TimeSpan DeathTime { get; set; }

    public string DeathReason { get; set; } = string.Empty;
    public string DeathCertNo { get; set; } = string.Empty;
    public string DeathPlace { get; set; } = string.Empty;

    /// <summary>死者与申报人关系</summary>
    public string Relation { get; set; } = string.Empty;

    public string DeclaredByName { get; set; } = string.Empty;
    public string DeclaredByIdCard { get; set; } = string.Empty;
    public string DeclaredByPhone { get; set; } = string.Empty;
    public string DeclaredByType { get; set; } = string.Empty;

    // ── 核实说明要素（《死亡情况核实说明》，2026-10-26；报告人与申报人独立手填）──
    /// <summary>发现（报告）人姓名</summary>
    public string ReporterName { get; set; } = string.Empty;

    /// <summary>报告人与死者关系</summary>
    public string ReporterRelation { get; set; } = string.Empty;

    /// <summary>报告人联系电话</summary>
    public string ReporterPhone { get; set; } = string.Empty;

    /// <summary>现场所见情况描述（长文本）</summary>
    public string SituationDesc { get; set; } = string.Empty;

    /// <summary>遗体安置情况（长文本）</summary>
    public string RemainsArrangement { get; set; } = string.Empty;

    /// <summary>业务办理人</summary>
    public string BusinessAgent { get; set; } = string.Empty;

    /// <summary>相关业务事项（默认「申请居民死亡医学证明」）</summary>
    public string BusinessMatter { get; set; } = string.Empty;

    public long? ApplicationId { get; set; }

    /// <summary>所属村ID（村级申报有值；镇级降级为 NULL，归属改由 town_id 承担）</summary>
    public int? VillageId { get; set; }

    public string VillageName { get; set; } = string.Empty;
    public int? TownId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Channel { get; set; } = "PC";
    public string Remark { get; set; } = string.Empty;
}

/// <summary>申报提交结果</summary>
public sealed class DeathDeclareResult
{
    public long Id { get; set; }
    public DateTime DeclaredAt { get; set; }

    /// <summary>同身份证窗口期内既存有效申报数（&gt;0 时页面提示"本窗口内第 N 次申报"）</summary>
    public int RecentValidCount { get; set; }
}
