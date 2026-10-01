namespace NewCosmos.Constants;

/// <summary>
/// 普惠高龄业务常量
/// </summary>
public static class ElderlyBenefitConstants
{
    /// <summary>享受类别：80-89周岁（含）低保对象、低保边缘家庭成员、特困人员</summary>
    public const string CatLowSubsidy = "CAT1";

    /// <summary>享受类别：80-89周岁（含）其他老年人</summary>
    public const string CatOtherElderly = "CAT2";

    /// <summary>享受类别：90-99周岁（含）老年人</summary>
    public const string Cat90To99 = "CAT3";

    /// <summary>享受类别：100周岁（含）以上老年人</summary>
    public const string Cat100Plus = "CAT4";

    /// <summary>状态：草稿</summary>
    public const string StatusDraft = "Draft";

    /// <summary>状态：已确认（在享）</summary>
    public const string StatusConfirmed = "Confirmed";

    /// <summary>状态：已停发</summary>
    public const string StatusStopped = "Stopped";

    /// <summary>状态：发放中（nc_biz_elderly_subsidy_history.status 在发记录）</summary>
    public const string StatusActive = "Active";

    /// <summary>下月待办类型：待新增</summary>
    public const string PendingTypeNew = "New";

    /// <summary>下月待办类型：需停旧增新</summary>
    public const string PendingTypeTransfer = "Transfer";

    /// <summary>高龄政策开始年月（此前的月份一概无补发金额）</summary>
    public static readonly DateTime PolicyStartDate = new(2024, 10, 1);

    /// <summary>享受门槛年龄</summary>
    public const int Threshold80 = 80;
    public const int Threshold90 = 90;
    public const int Threshold100 = 100;

    /// <summary>标准配置类型（nc_config_standards.standard_type）</summary>
    public const string StandardType = "ELDERLY_SUBSIDY";

    /// <summary>申请编号前缀</summary>
    public const string ApplicationNoPrefix = "GA";

    /// <summary>身份比对结果：最低生活保障对象</summary>
    public const string IdentityLowSubsidy = "最低生活保障对象";

    /// <summary>身份比对结果：最低生活保障边缘家庭</summary>
    public const string IdentityLowIncomeEdge = "最低生活保障边缘家庭";

    /// <summary>身份比对结果：特困人员</summary>
    public const string IdentityDestitute = "特困人员";

    /// <summary>身份比对结果：最低生活保障边缘家庭</summary>
    public const string IdentityLowIncome = "最低生活保障边缘家庭";

    /// <summary>身份比对结果：未命中</summary>
    public const string IdentityNone = "无";

    /// <summary>停发原因 key：去世（数据库存储 key，显示时映射中文）</summary>
    public const string StopReasonDeceased = "DECEASED";

    /// <summary>停发原因 key：户籍迁出本县</summary>
    public const string StopReasonMovedOut = "MOVED_OUT";

    /// <summary>停发原因 key：身份资格取消（不再属于低保/特困/低收入等）</summary>
    public const string StopReasonQualificationLost = "QUALIFICATION_LOST";

    /// <summary>停发原因 key：重复享受（多地重复发放）</summary>
    public const string StopReasonDuplicate = "DUPLICATE";

    /// <summary>停发原因 key：自愿放弃</summary>
    public const string StopReasonVoluntaryAbandon = "VOLUNTARY_ABANDON";

    /// <summary>停发原因 key：查找不到本人</summary>
    public const string StopReasonNotLocated = "NOT_LOCATED";

    /// <summary>停发原因 key：信息更正后不符合条件</summary>
    public const string StopReasonInfoCorrection = "INFO_CORRECTION";

    /// <summary>停发原因 key：政策调整</summary>
    public const string StopReasonPolicyChange = "POLICY_CHANGE";

    /// <summary>停发原因 key：其他（需说明）</summary>
    public const string StopReasonOther = "OTHER";

    /// <summary>停发原因 key：不符合每月认证要求</summary>
    public const string StopReasonCertificationFail = "CERTIFICATION_FAIL";

    /// <summary>
    /// 停发原因 key：类别复核调整（复核停旧增新系统写入，非人工停发）。
    /// 不加入 StopReasons/StopReasonPickerOptions，避免出现在停发下拉；
    /// 月报停止明细与停发统计显式排除本 key。
    /// </summary>
    public const string StopReasonReview = "REVIEW";

    /// <summary>停发原因选项（固定九项 key，顺序即界面展示顺序）</summary>
    public static readonly string[] StopReasons =
    {
        StopReasonDeceased,
        StopReasonMovedOut,
        StopReasonQualificationLost,
        StopReasonDuplicate,
        StopReasonVoluntaryAbandon,
        StopReasonNotLocated,
        StopReasonInfoCorrection,
        StopReasonPolicyChange,
        StopReasonOther
    };

    /// <summary>停发原因选项（取消备案表模板使用的五项 key）</summary>
    public static readonly string[] StopReasonPickerOptions =
    {
        StopReasonDeceased,           // 去世
        StopReasonMovedOut,           // 户籍迁出
        StopReasonQualificationLost,  // 低保、低保边缘、特困资格取消
        StopReasonCertificationFail,  // 不符合每月认证要求
        StopReasonOther               // 其他
    };

    /// <summary>停发原因 key 对应中文显示名（未知 key 原样返回，兼容历史中文存量）</summary>
    public static string GetStopReasonName(string? key) => key switch
    {
        StopReasonDeceased => "去世",
        StopReasonMovedOut => "户籍迁出本县",
        StopReasonQualificationLost => "身份资格取消",
        StopReasonDuplicate => "重复享受",
        StopReasonVoluntaryAbandon => "自愿放弃",
        StopReasonNotLocated => "查找不到本人",
        StopReasonInfoCorrection => "信息更正后不符合条件",
        StopReasonPolicyChange => "政策调整",
        StopReasonCertificationFail => "不符合每月认证要求",
        StopReasonReview => "类别复核调整",
        StopReasonOther => "其他",
        _ => key ?? string.Empty
    };

    /// <summary>停发原因取消备案表模板专用显示名（key → 模板勾选文本）</summary>
    private static readonly Dictionary<string, string> StopReasonTemplateNames = new()
    {
        [StopReasonDeceased] = "去世",
        [StopReasonMovedOut] = "户籍迁出",
        [StopReasonQualificationLost] = "低保、低保边缘、特困资格取消",
        [StopReasonCertificationFail] = "不符合每月认证要求",
        [StopReasonOther] = "其他________"
    };

    /// <summary>
    /// 生成取消备案表"停发原因"勾选文本：选中项前标 ☑、其余标 □，各选项一行。
    /// </summary>
    public static string BuildStopReasonCheckText(string? reason)
    {
        var options = StopReasonPickerOptions
            .Select(k => StopReasonTemplateNames.TryGetValue(k, out var name) ? name : GetStopReasonName(k))
            .ToArray();
        var checkedIndex = Array.IndexOf(StopReasonPickerOptions, reason);
        return BuildCheckText(options, checkedIndex, '\n');
    }

    /// <summary>补发原因 key：未及时登记（数据库存储 key，显示时映射中文）</summary>
    public const string PaybackReasonMissedRegistration = "MISSED_REGISTRATION";

    /// <summary>补发原因 key：查找不到本人</summary>
    public const string PaybackReasonNotLocated = "NOT_LOCATED";

    /// <summary>补发原因 key：短暂失联</summary>
    public const string PaybackReasonLostContact = "LOST_CONTACT";

    /// <summary>补发原因 key：其他</summary>
    public const string PaybackReasonOther = "OTHER";

    /// <summary>补发原因选项（固定四项 key，顺序即界面展示顺序）</summary>
    public static readonly string[] PaybackReasons =
    {
        PaybackReasonMissedRegistration,
        PaybackReasonNotLocated,
        PaybackReasonLostContact,
        PaybackReasonOther
    };

    /// <summary>补发原因 key 对应中文显示名（未知 key 原样返回，兼容历史中文存量）</summary>
    public static string GetPaybackReasonName(string? key) => key switch
    {
        PaybackReasonMissedRegistration => "未及时登记",
        PaybackReasonNotLocated => "查找不到本人",
        PaybackReasonLostContact => "短暂失联",
        PaybackReasonOther => "其他",
        _ => key ?? string.Empty
    };

    /// <summary>
    /// 获取享受类别显示名称
    /// </summary>
    public static string GetCategoryName(string? category) => category switch
    {
        CatLowSubsidy => "80-89周岁最低生活保障/边缘/特困人员",
        CatOtherElderly => "80-89周岁其他老年人",
        Cat90To99 => "90-99周岁老年人",
        Cat100Plus => "100周岁以上老年人",
        _ => category ?? string.Empty
    };

    /// <summary>享受类别代码（顺序与官方登记表一致：CAT1-CAT4）</summary>
    public static readonly string[] CategoryCodes =
    {
        CatLowSubsidy,
        CatOtherElderly,
        Cat90To99,
        Cat100Plus
    };

    /// <summary>月报明细表类型：新增</summary>
    public const string MonthlyFormNew = "新增";

    /// <summary>月报明细表类型：停止</summary>
    public const string MonthlyFormStop = "停止";

    /// <summary>月报表单项：满90周岁调整备案表（自动筛选本月满90周岁，逐人一页）</summary>
    public const string FormKeyAge90 = "AGE90_调整";

    /// <summary>
    /// 月报明细表勾选选项（key = "CATx_新增/停止"，label = "CATx新增/停止明细表"）
    /// 由分类代码 + 类型枚举组合生成，禁止在 ViewModel 内硬编码 8 项
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> GetMonthlyDetailFormOptions()
    {
        var result = new List<KeyValuePair<string, string>>(CategoryCodes.Length * 2);
        foreach (var categoryCode in CategoryCodes)
        {
            result.Add(new KeyValuePair<string, string>($"{categoryCode}_{MonthlyFormNew}", $"{categoryCode}新增明细表"));
            result.Add(new KeyValuePair<string, string>($"{categoryCode}_{MonthlyFormStop}", $"{categoryCode}停止明细表"));
        }
        // 满90周岁调整备案表：自动筛选本月满90周岁（在享+名册），逐人一页，标签不带计数
        result.Add(new KeyValuePair<string, string>(FormKeyAge90, "满90周岁调整备案表"));
        return result;
    }

    /// <summary>是否为「满90周岁调整备案表」表单项</summary>
    public static bool IsAge90Form(string? formKey) =>
        string.Equals(formKey, FormKeyAge90, StringComparison.Ordinal);

    /// <summary>
    /// 从明细表 formKey 解析分类代码（如 "CAT1_新增" → "CAT1"），非法返回空字符串
    /// </summary>
    public static string GetCategoryCodeFromFormKey(string? formKey)
    {
        if (string.IsNullOrWhiteSpace(formKey)) return string.Empty;
        var part = formKey.Split('_')[0].Trim();
        return CategoryCodes.Contains(part) ? part : string.Empty;
    }

    /// <summary>
    /// 从明细表 formKey 判断是否为停止表（"CAT1_停止" → true）
    /// </summary>
    public static bool IsStopForm(string? formKey) =>
        formKey?.Contains(MonthlyFormStop, StringComparison.Ordinal) == true;

    /// <summary>官方登记表享受类别四个勾选项（顺序与官方《林口县普惠高龄津贴登记表》一致）</summary>
    public static readonly string[] CategoryCheckOptions =
    {
        "80-89周岁（含）的最低生活保障对象、最低生活保障边缘家庭、特困人员",
        "80-89周岁（含）的其他老年人",
        "90周岁（含）以上老年人",
        "100周岁（含）以上老年人"
    };

    /// <summary>
    /// 生成登记表"享受类别"勾选文本：选中项前标 ☑、其余标 □，四个选项逐行显示（换行）。
    /// 明细表列请用 GetCategoryName（短名）。
    /// </summary>
    public static string BuildCategoryCheckText(string? category)
    {
        var index = category switch
        {
            CatLowSubsidy => 0,
            CatOtherElderly => 1,
            Cat90To99 => 2,
            Cat100Plus => 3,
            _ => -1
        };
        return BuildCheckText(CategoryCheckOptions, index, '\n');
    }

    /// <summary>
    /// 生成登记表"补发原因"勾选文本：选中项前标 ☑、其余标 □，各选项一行内用全角空格分隔（不换行）。
    /// 明细表列请用 GetPaybackReasonName（短名）。
    /// </summary>
    public static string BuildPaybackReasonCheckText(string? reason)
    {
        var options = new[]
        {
            "未及时登记",
            "查找不到本人",
            "短暂失联",
            "其他________"
        };
        var index = reason switch
        {
            PaybackReasonMissedRegistration => 0,
            PaybackReasonNotLocated => 1,
            PaybackReasonLostContact => 2,
            PaybackReasonOther => 3,
            _ => -1
        };
        return BuildCheckText(options, index, '\u3000');
    }

    /// <summary>按选中索引生成勾选文本：命中项 ☑、其余 □，选项间用指定分隔符连接</summary>
    private static string BuildCheckText(string[] options, int checkedIndex, char separator)
    {
        return string.Join(separator, options.Select((opt, i) => (i == checkedIndex ? "☑" : "□") + opt));
    }

    /// <summary>
    /// 获取状态中文名称
    /// </summary>
    public static string GetStatusName(string? status) => status switch
    {
        StatusDraft => "草稿",
        StatusConfirmed => "已确认",
        StatusStopped => "已停发",
        _ => status ?? string.Empty
    };

    /// <summary>
    /// 判断身份是否属于较高补贴档（最低生活保障/最低生活保障边缘/特困人员）
    /// </summary>
    public static bool IsHighSubsidyIdentity(string? identityFlag) =>
        identityFlag is IdentityLowSubsidy or IdentityLowIncomeEdge or IdentityDestitute or IdentityLowIncome;

    // ─────────────────────────────────────────────
    //  类别复核（身份 + 年龄重评，停旧增新）
    // ─────────────────────────────────────────────

    /// <summary>复核状态：待复核</summary>
    public const string ReviewStatusPending = "Pending";

    /// <summary>复核状态：已完成</summary>
    public const string ReviewStatusCompleted = "Completed";

    /// <summary>复核结果：已变更（类别不同，停旧增新）</summary>
    public const string ReviewResultChanged = "Changed";

    /// <summary>复核结果：无需调整（类别一致）</summary>
    public const string ReviewResultNoChange = "NoChange";

    /// <summary>复核触发来源：人工</summary>
    public const string ReviewTriggerManual = "Manual";

    /// <summary>复核触发来源：低收入归档联动</summary>
    public const string ReviewTriggerArchive = "ArchiveReview";

    /// <summary>复核触发来源：低收入信息变更/经济复核</summary>
    public const string ReviewTriggerLowIncomeChange = "LowIncomeChange";

    /// <summary>复核触发来源：导入名册</summary>
    public const string ReviewTriggerImport = "Import";

    /// <summary>复核触发来源：月报「满90周岁调整备案表」自动入队</summary>
    public const string ReviewTriggerAge90 = "Age90Monthly";

    /// <summary>调整原因：年龄档次</summary>
    public const string AdjustReasonAge = "AGE";

    /// <summary>调整原因：身份变动</summary>
    public const string AdjustReasonIdentity = "IDENTITY";

    /// <summary>调整原因：其他政策规定</summary>
    public const string AdjustReasonOther = "OTHER";

    /// <summary>复核来源类型（写入档案 source_type，用于报表排除，避免误计新增）</summary>
    public const string SourceTypeElderlyReview = "ElderlyReview";

    /// <summary>复核编号前缀</summary>
    public const string ReviewNoPrefix = "FC";

    /// <summary>复核状态中文名</summary>
    public static string GetReviewStatusName(string? status) => status switch
    {
        ReviewStatusPending => "待复核",
        ReviewStatusCompleted => "已完成",
        _ => status ?? string.Empty
    };

    /// <summary>复核结果中文名</summary>
    public static string GetReviewResultName(string? result) => result switch
    {
        ReviewResultChanged => "已变更",
        ReviewResultNoChange => "无需调整",
        _ => result ?? string.Empty
    };

    /// <summary>触发来源中文名</summary>
    public static string GetReviewTriggerName(string? trigger) => trigger switch
    {
        ReviewTriggerManual => "人工复核",
        ReviewTriggerArchive => "低收入归档联动",
        ReviewTriggerLowIncomeChange => "低收入信息变更",
        ReviewTriggerImport => "导入名册",
        ReviewTriggerAge90 => "满90周岁月报",
        _ => trigger ?? string.Empty
    };

    /// <summary>
    /// 名册人员类型（nc_biz_elderly_subsidy_history.person_type）→ 身份比对结果。
    /// 用于名册未建档人员补建旧档案时还原旧身份档（普惠型=普通老年人）。
    /// </summary>
    public static string GetIdentityFlagFromHistoryPersonType(string? personType)
    {
        if (string.IsNullOrWhiteSpace(personType)) return IdentityNone;
        if (personType.Contains("特困", StringComparison.Ordinal)) return IdentityDestitute;
        if (personType.Contains("最低生活保障对象", StringComparison.Ordinal)
            || (personType.Contains("低保", StringComparison.Ordinal) && !personType.Contains("边缘", StringComparison.Ordinal)))
            return IdentityLowSubsidy;
        if (personType.Contains("边缘", StringComparison.Ordinal)) return IdentityLowIncomeEdge;
        return IdentityNone;
    }

    /// <summary>调整备案表"调整原因"三选项（顺序与模板一致）</summary>
    private static readonly string[] AdjustReasonOptions =
    {
        "年龄由89周岁增长至90周岁",
        "低保对象、低保边缘家庭成员、特困人员变动（{0}取消/{1}新增）",
        "其他政策规定"
    };

    /// <summary>
    /// 生成调整备案表"调整原因"勾选文本：选中项 ☑、其余 □，三项逐行（换行）。
    /// reasonCode 见 AdjustReason*；identityAdd 表示身份变动方向（true=新增身份，false=取消身份）。
    /// </summary>
    public static string BuildAdjustReasonCheckText(string? reasonCode, bool identityAdd)
    {
        var index = reasonCode switch
        {
            AdjustReasonAge => 0,
            AdjustReasonIdentity => 1,
            _ => 2
        };
        var options = new[]
        {
            AdjustReasonOptions[0],
            string.Format(AdjustReasonOptions[1], identityAdd ? "□" : "☑", identityAdd ? "☑" : "□"),
            AdjustReasonOptions[2]
        };
        return BuildCheckText(options, index, '\n');
    }
}
