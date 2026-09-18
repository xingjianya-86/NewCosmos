namespace NewCosmos.Constants;

/// <summary>
/// 值班管理常量 - 组编码、日期类型、设置键、生成规则
/// </summary>
public static class DutyConstants
{
    /// <summary>
    /// 值班组编码
    /// </summary>
    public static class Groups
    {
        public const string LEADER = "LEADER";
        public const string MIDDLE = "MIDDLE";
        public const string MALE = "MALE";
        public const string FEMALE = "FEMALE";

        /// <summary>值班表展示顺序（列顺序）</summary>
        public static readonly string[] DisplayOrder = { LEADER, MIDDLE, MALE, FEMALE };

        /// <summary>工作日/休息日需要出班的组（无中层）</summary>
        public static readonly string[] RegularRequired = { LEADER, MALE, FEMALE };

        /// <summary>法定节假日需要出班的组（含中层）</summary>
        public static readonly string[] HolidayRequired = { LEADER, MIDDLE, MALE, FEMALE };

        /// <summary>生成时选人优先级（同人一天一班规则下的冲突消解顺序）</summary>
        public static readonly string[] SelectionOrder = { LEADER, MIDDLE, MALE, FEMALE };

        /// <summary>是否为合法组编码</summary>
        public static bool IsValid(string code) =>
            code == LEADER || code == MIDDLE || code == MALE || code == FEMALE;

        public static string DisplayName(string code) => code switch
        {
            LEADER => "领导组",
            MIDDLE => "中层管理组",
            MALE => "男生组",
            FEMALE => "女生组",
            _ => code
        };
    }

    /// <summary>
    /// 值班日期类型（nc_duty_schedules.date_type / 轮转线维度）
    /// </summary>
    public static class DateTypes
    {
        public const string WORKDAY = "WORKDAY";
        public const string RESTDAY = "RESTDAY";
        public const string HOLIDAY = "HOLIDAY";

        public static bool IsValid(string code) =>
            code == WORKDAY || code == RESTDAY || code == HOLIDAY;

        public static string DisplayName(string code) => code switch
        {
            WORKDAY => "工作日",
            RESTDAY => "休息日",
            HOLIDAY => "法定节假日",
            _ => code
        };
    }

    /// <summary>
    /// 班务调整类型（nc_duty_shift_changes.change_type）
    /// </summary>
    public static class ChangeTypes
    {
        /// <summary>串班（单向转让：转出者让出某日班次，受让人顶上）</summary>
        public const string SWAP = "SWAP";
        /// <summary>代班（某人代替另一人上班一次）</summary>
        public const string SUBSTITUTE = "SUBSTITUTE";

        public static bool IsValid(string code) => code == SWAP || code == SUBSTITUTE;

        public static string DisplayName(string code) => code switch
        {
            SWAP => "串班",
            SUBSTITUTE => "代班",
            _ => code
        };
    }

    /// <summary>
    /// 班务调整重放状态（列表展示；不落库，由 GetShiftChangesAsync 实时计算）
    /// </summary>
    public static class ChangeReplayStatuses
    {
        /// <summary>已生效（两班均已按记录替换）</summary>
        public const string Applied = "Applied";
        /// <summary>待生效（对方月份未生成，补齐生成后自动生效）</summary>
        public const string PendingUnbuilt = "PendingUnbuilt";
        /// <summary>待生效（轮转匹配，重新生成该月即生效）</summary>
        public const string PendingRebuild = "PendingRebuild";
        /// <summary>已失效（轮转已变化，需改期或撤销）</summary>
        public const string Invalid = "Invalid";
        /// <summary>已撤销</summary>
        public const string Cancelled = "Cancelled";

        public static string ColorHex(string status) => status switch
        {
            Applied => "#2E7D32",
            PendingUnbuilt or PendingRebuild => "#B45309",
            Invalid => "#C62828",
            _ => "#78909C"
        };
    }

    /// <summary>
    /// 节假日行类型（nc_sys_holidays.date_type）：放假日 / 调休补班日
    /// </summary>
    public static class HolidayRowTypes
    {
        public const string HOLIDAY = "HOLIDAY";
        public const string MAKEUP = "MAKEUP";

        public static string DisplayName(string code) => code switch
        {
            HOLIDAY => "法定节假日",
            MAKEUP => "调休补班",
            _ => code
        };
    }

    /// <summary>
    /// 节假日数据来源（nc_sys_holidays.source）
    /// </summary>
    public static class Sources
    {
        public const string API = "API";
        public const string MANUAL = "MANUAL";
        public const string SEED = "SEED";

        public static string DisplayName(string code) => code switch
        {
            API => "接口下载",
            MANUAL => "手工维护",
            SEED => "内置种子",
            _ => code
        };
    }

    /// <summary>
    /// 值班设置键（nc_duty_settings.setting_key）
    /// </summary>
    public static class SettingKeys
    {
        /// <summary>休息日（周六/周日）带班领导两天同人：1=开启（周日复用周六所选领导），0/未设置=每天独立轮转</summary>
        public const string RESTDAY_LEADER_SAME_PAIR = "restday_leader_same_pair";

        /// <summary>法定节假日数据下载接口地址，{year} 为年份占位符</summary>
        public const string HOLIDAY_API_URL = "holiday_api_url";

        /// <summary>批量导入的轮转锚点起始日期（yyyy-MM-dd）：生成该月值班表时自动从此日起生成</summary>
        public const string IMPORT_ANCHOR_DATE = "duty_import_anchor_date";

        /// <summary>锚点月各轮转线的校准起点 key 前缀（完整 key = 前缀 + 组编码 + "_" + 日期类型，值为起点成员 ID）。
        /// 锚点月生成起点恒定取此值（月内冻结游标），保证重生成结果确定；批量导入/手工校准时刷新。</summary>
        public const string ANCHOR_START_PREFIX = "duty_anchor_start_";

        /// <summary>构造锚点校准起点 setting_key</summary>
        public static string AnchorStartKey(string groupCode, string dateType) =>
            $"{ANCHOR_START_PREFIX}{groupCode}_{dateType}";
    }

    /// <summary>节假日数据下载接口默认地址（timor.tech 免费节假日 API，{year} 占位替换）</summary>
    public const string DefaultHolidayApiUrl = "https://timor.tech/api/holiday/year/{year}";

    /// <summary>节假日接口请求超时（秒）</summary>
    public const int HolidayApiTimeoutSeconds = 30;

    /// <summary>设置值：开</summary>
    public const string SettingOn = "1";

    /// <summary>设置值：关</summary>
    public const string SettingOff = "0";

    /// <summary>
    /// 政务值班表模板（存储于模板库 nc_biz_templates，与其他模块模板一致；
    /// 由用户在文书模板管理页上传，名称须为 GovDutyTemplateName）
    /// </summary>
    public static class TemplateSettings
    {
        /// <summary>模板库内的模板名称（ITemplateService.GetByNameAsync 精确匹配键）</summary>
        public const string GovDutyTemplateName = "模板_政务值班表";

        /// <summary>导出文件名格式：{0}=年 {1}=月</summary>
        public const string ExportFileNameFormat = "{0}年{1}月政务值班表.xlsx";

        /// <summary>值班成员批量导入模板文件名（ExportMemberImportTemplateAsync 生成）</summary>
        public const string MemberImportTemplateFileName = "值班成员导入模板.xlsx";
    }

    /// <summary>
    /// 政务值班表输出口径
    /// </summary>
    public static class OutputRules
    {
        /// <summary>{日期N} 单元格格式（如"9月1日"）</summary>
        public const string DateCellFormat = "M月d日";

        /// <summary>非法定节假日时中层列、以及班次缺位时姓名列的占位符</summary>
        public const string EmptyCellPlaceholder = "—";

        /// <summary>小月多余日期行（N>当月天数）的填充值（空串，保留模板行结构）</summary>
        public const string OverflowCellPlaceholder = "";
    }
}
