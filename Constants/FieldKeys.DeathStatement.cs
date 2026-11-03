namespace NewCosmos.Constants;

/// <summary>
/// 《死亡情况说明》文书字段键（证明文件分类模板专用）。
///
/// 【三同步纪律】（见 FieldKeys.cs 头注释，缺一即产生孤儿键）：
///   ① 本文件声明常量（值=键名，全 ASCII，过 Scripts\check_template_fields.ps1 -FailOn NonAscii）；
///   ② DeathStatementViewModel.BuildStatementFields() 同步供给（与既有同源键并存时同值双写）；
///   ③ 上传模板 config_json 的 Fields 需声明同名键（索引槽必须声明，否则 tableRows 展平被忽略）。
///
/// 模板名常量见 DocumentTemplateNames.DeathStatement；占位符规范见
/// docs/20261026_死亡情况说明与申报台账规范.md。
/// </summary>
public static partial class FieldKeys
{
    #region 死者基本信息

    /// <summary>死者姓名</summary>
    public const string DEATH_NAME = "DEATH_NAME";

    /// <summary>死者身份证号</summary>
    public const string DEATH_ID_CARD = "DEATH_ID_CARD";

    /// <summary>死亡日期（yyyy-MM-dd）</summary>
    public const string DEATH_DATE = "DEATH_DATE";

    /// <summary>死亡时刻（HH:mm；历史数据未采集时为空）</summary>
    public const string DEATH_TIME = "DEATH_TIME";

    /// <summary>死亡确切时间整段（yyyy年M月d日 HH:mm；无时刻时只到日期）</summary>
    public const string DEATH_DATETIME_TEXT = "DEATH_DATETIME_TEXT";

    /// <summary>死亡原因</summary>
    public const string DEATH_REASON = "DEATH_REASON";

    /// <summary>死亡证明编号</summary>
    public const string DEATH_CERT_NO = "DEATH_CERT_NO";

    /// <summary>死亡地点</summary>
    public const string DEATH_PLACE = "DEATH_PLACE";

    /// <summary>死者与户主关系（档案内；手工录入无档案时为空）</summary>
    public const string DEATH_RELATION = "DEATH_RELATION";

    /// <summary>死者与申报人关系</summary>
    public const string DECLARED_RELATION = "DECLARED_RELATION";

    /// <summary>所属村/社区</summary>
    public const string DEATH_VILLAGE = "DEATH_VILLAGE";

    /// <summary>所属乡镇/街道</summary>
    public const string DEATH_TOWN = "DEATH_TOWN";

    /// <summary>家庭住址</summary>
    public const string DEATH_ADDRESS = "DEATH_ADDRESS";

    /// <summary>所属档案分类（低保/特困/边缘…；无档案为空）</summary>
    public const string DEATH_ARCHIVE_CLASSIFICATION = "DEATH_ARCHIVE_CLASSIFICATION";

    /// <summary>关联档案ID（无档案为空）</summary>
    public const string DEATH_ARCHIVE_ID = "DEATH_ARCHIVE_ID";

    #endregion

    #region 申报信息

    /// <summary>申报人姓名</summary>
    public const string DECLARED_BY_NAME = "DECLARED_BY_NAME";

    /// <summary>申报人身份证号</summary>
    public const string DECLARED_BY_ID_CARD = "DECLARED_BY_ID_CARD";

    /// <summary>申报人联系电话</summary>
    public const string DECLARED_BY_PHONE = "DECLARED_BY_PHONE";

    /// <summary>申报人类型（家属/村民委员会/医疗机构/公安机关/其他）</summary>
    public const string DECLARED_BY_TYPE = "DECLARED_BY_TYPE";

    /// <summary>申报（受理）时间（yyyy-MM-dd HH:mm）</summary>
    public const string DECLARED_AT = "DECLARED_AT";

    #endregion

    #region 核实说明要素（《死亡情况核实说明》模板，2026-10-26）

    /// <summary>推送单位（三联各异：村留存 / 镇政府 / 镇中心卫生院）</summary>
    public const string PUSH_UNIT = "PUSH_UNIT";

    /// <summary>死者性别（身份证推导 `IdCardValidator.ExtractGender`，档案 Gender 兜底）</summary>
    public const string DEATH_GENDER = "DEATH_GENDER";

    /// <summary>死者民族（档案 Ethnicity 字典 key → IDictCacheService.GetValue(Ethnicities,…) 转显示名）</summary>
    public const string DEATH_ETHNICITY = "DEATH_ETHNICITY";

    /// <summary>发现（报告）人姓名——与申报人独立，表单手填</summary>
    public const string DEATH_REPORTER_NAME = "DEATH_REPORTER_NAME";

    /// <summary>报告人与死者关系</summary>
    public const string DEATH_REPORTER_RELATION = "DEATH_REPORTER_RELATION";

    /// <summary>报告人联系电话</summary>
    public const string DEATH_REPORTER_PHONE = "DEATH_REPORTER_PHONE";

    /// <summary>现场所见情况描述（长文本）</summary>
    public const string DEATH_SITUATION_DESC = "DEATH_SITUATION_DESC";

    /// <summary>遗体安置情况（长文本）</summary>
    public const string DEATH_REMAINS_ARRANGEMENT = "DEATH_REMAINS_ARRANGEMENT";

    /// <summary>业务办理人</summary>
    public const string BUSINESS_AGENT = "BUSINESS_AGENT";

    /// <summary>相关业务事项（多选 CheckBox 勾选，分隔符「、」拼接；选项=字典 BusinessMatters，首项默认勾选）</summary>
    public const string BUSINESS_MATTER = "BUSINESS_MATTER";

    /// <summary>所在省（组织机构省级级联 province_name，2026-10-26 加）</summary>
    public const string PROVINCE = "PROVINCE";

    /// <summary>所在市（组织 CityName；DISTRICT 键已存在故县区复用不另建）</summary>
    public const string CITY = "CITY";

    #endregion

    #region 出具信息

    /// <summary>出具单位（当前登录用户所属单位/村委会，落款用）</summary>
    public const string DEATH_ISSUE_UNIT = "DEATH_ISSUE_UNIT";

    /// <summary>出具日期（yyyy年M月d日）</summary>
    public const string DEATH_ISSUE_DATE = "DEATH_ISSUE_DATE";

    #endregion

    #region 汇总

    /// <summary>身份条数</summary>
    public const string DEATH_IDENTITY_COUNT = "DEATH_IDENTITY_COUNT";

    /// <summary>数据提醒条数</summary>
    public const string DEATH_REMIND_COUNT = "DEATH_REMIND_COUNT";

    /// <summary>身份清单表索引槽容量（模板需预置 _1.._12 占位符，超出部分由 DEATH_IDENTITY_COUNT 提示）</summary>
    public const int DEATH_IDENTITY_SLOTS = 12;

    /// <summary>数据提醒索引槽容量（模板需预置 _1.._8 占位符）</summary>
    public const int DEATH_REMIND_SLOTS = 8;

    #endregion

    #region 身份清单表（索引槽 _1.._12，tableRows 展平）

    /// <summary>身份类别（低保对象/普惠高龄/残疾人…）</summary>
    public const string DEATH_IDENTITY_TYPE_BASE = "DEATH_IDENTITY_TYPE";

    /// <summary>身份状态（在享/已停发/在读…）</summary>
    public const string DEATH_IDENTITY_STATUS_BASE = "DEATH_IDENTITY_STATUS";

    /// <summary>享受起止（yyyy-MM 至今 / yyyy-MM ~ yyyy-MM）</summary>
    public const string DEATH_IDENTITY_PERIOD_BASE = "DEATH_IDENTITY_PERIOD";

    /// <summary>数据来源（表/模块名）</summary>
    public const string DEATH_IDENTITY_SOURCE_BASE = "DEATH_IDENTITY_SOURCE";

    /// <summary>备注（金额/等级等补充）</summary>
    public const string DEATH_IDENTITY_NOTE_BASE = "DEATH_IDENTITY_NOTE";

    #endregion

    #region 数据提醒（索引槽 _1.._8，tableRows 展平）

    /// <summary>提醒级别（警告/提示）</summary>
    public const string DEATH_REMIND_LEVEL_BASE = "DEATH_REMIND_LEVEL";

    /// <summary>提醒内容</summary>
    public const string DEATH_REMIND_TEXT_BASE = "DEATH_REMIND_TEXT";

    #endregion
}
