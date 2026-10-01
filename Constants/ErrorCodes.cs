namespace NewCosmos.Constants;

/// <summary>
/// 错误码常量类
/// </summary>
public static class ErrorCodes
{
    #region 数据验证 (VALIDATION_xxx)

    /// <summary>数据验证失败</summary>
    public const string VALIDATION_FAILED = "VALIDATION_FAILED";

    /// <summary>必填字段缺失</summary>
    public const string REQUIRED_FIELD_MISSING = "REQUIRED_FIELD_MISSING";

    /// <summary>身份证号格式不正确</summary>
    public const string INVALID_ID_CARD = "INVALID_ID_CARD";

    /// <summary>手机号格式不正确</summary>
    public const string INVALID_PHONE = "INVALID_PHONE";

    /// <summary>身份证号重复</summary>
    public const string DUPLICATE_ID_CARD = "DUPLICATE_ID_CARD";

    /// <summary>用户名重复</summary>
    public const string DUPLICATE_USERNAME = "DUPLICATE_USERNAME";
    public const string DUPLICATE_PHONE = "DUPLICATE_PHONE";
    public const string INVALID_NAME = "INVALID_NAME";

    /// <summary>金额无效</summary>
    public const string INVALID_AMOUNT = "INVALID_AMOUNT";

    #endregion

    #region 业务规则 (BIZ_xxx)

    /// <summary>状态转换无效</summary>
    public const string INVALID_TRANSITION = "INVALID_TRANSITION";

    /// <summary>成员是户主</summary>
    public const string MEMBER_IS_HEAD = "MEMBER_IS_HEAD";

    /// <summary>最后一个成员不能删除</summary>
    public const string LAST_MEMBER_CANNOT_DELETE = "LAST_MEMBER_CANNOT_DELETE";

    /// <summary>收入超出范围</summary>
    public const string INCOME_OUT_OF_RANGE = "INCOME_OUT_OF_RANGE";

    /// <summary>渐退期月数无效</summary>
    public const string GRACE_PERIOD_INVALID_MONTHS = "GRACE_PERIOD_INVALID_MONTHS";

    /// <summary>申请已存在</summary>
    public const string APPLICATION_ALREADY_EXISTS = "APPLICATION_ALREADY_EXISTS";

    /// <summary>申请已提交</summary>
    public const string APPLICATION_ALREADY_SUBMITTED = "APPLICATION_ALREADY_SUBMITTED";

    /// <summary>年度唯一性冲突（如临时救助每年仅限一次）</summary>
    public const string ANNUAL_LIMIT_EXCEEDED = "ANNUAL_LIMIT_EXCEEDED";

    #endregion

    #region 实体未找到 (NOT_FOUND_xxx)

    /// <summary>实体未找到</summary>
    public const string NOT_FOUND = "NOT_FOUND";

    /// <summary>申请未找到</summary>
    public const string APPLICATION_NOT_FOUND = "APPLICATION_NOT_FOUND";

    /// <summary>家庭成员未找到</summary>
    public const string FAMILY_MEMBER_NOT_FOUND = "FAMILY_MEMBER_NOT_FOUND";

    /// <summary>家庭未找到</summary>
    public const string FAMILY_NOT_FOUND = "FAMILY_NOT_FOUND";

    /// <summary>用户未找到</summary>
    public const string USER_NOT_FOUND = "USER_NOT_FOUND";

    /// <summary>角色未找到</summary>
    public const string ROLE_NOT_FOUND = "ROLE_NOT_FOUND";

    /// <summary>权限未找到</summary>
    public const string PERMISSION_NOT_FOUND = "PERMISSION_NOT_FOUND";

    /// <summary>档案未找到</summary>
    public const string ARCHIVE_NOT_FOUND = "ARCHIVE_NOT_FOUND";

    /// <summary>记录未找到</summary>
    public const string RECORD_NOT_FOUND = "RECORD_NOT_FOUND";

    #endregion

    #region 数据库错误 (DB_xxx)

    /// <summary>数据库连接失败</summary>
    public const string DB_CONNECTION_FAILED = "DB_CONNECTION_FAILED";

    /// <summary>数据库超时</summary>
    public const string DB_TIMEOUT = "DB_TIMEOUT";

    /// <summary>唯一性约束违反</summary>
    public const string DB_UNIQUE_VIOLATION = "DB_UNIQUE_VIOLATION";

    /// <summary>外键约束违反</summary>
    public const string DB_FOREIGN_KEY_VIOLATION = "DB_FOREIGN_KEY_VIOLATION";

    /// <summary>非空约束违反</summary>
    public const string DB_NOT_NULL_VIOLATION = "DB_NOT_NULL_VIOLATION";

    /// <summary>数据库查询错误</summary>
    public const string DB_QUERY_ERROR = "DB_QUERY_ERROR";

    /// <summary>数据库事务错误</summary>
    public const string DB_TRANSACTION_ERROR = "DB_TRANSACTION_ERROR";

    #endregion

    #region 权限错误 (AUTH_xxx)

    /// <summary>权限不足</summary>
    public const string PERMISSION_DENIED = "PERMISSION_DENIED";

    /// <summary>认证失败</summary>
    public const string AUTHENTICATION_FAILED = "AUTHENTICATION_FAILED";

    /// <summary>会话已过期</summary>
    public const string SESSION_EXPIRED = "SESSION_EXPIRED";

    /// <summary>账户已锁定</summary>
    public const string ACCOUNT_LOCKED = "ACCOUNT_LOCKED";

    /// <summary>密码错误</summary>
    public const string INVALID_PASSWORD = "INVALID_PASSWORD";

    #endregion

    #region 模板相关 (TEMPLATE_xxx)

    public const string TEMPLATE_NOT_FOUND = "TEMPLATE_NOT_FOUND";
    public const string TEMPLATE_SYNTACTICALLY_INVALID = "TEMPLATE_SYNTACTICALLY_INVALID";
    public const string TEMPLATE_UNSUPPORTED_TYPE = "TEMPLATE_UNSUPPORTED_TYPE";
    public const string TEMPLATE_ALREADY_EXISTS = "TEMPLATE_ALREADY_EXISTS";
    public const string TEMPLATE_UPLOAD_FAILED = "TEMPLATE_UPLOAD_FAILED";

    #endregion

    #region 值班管理 (DUTY_xxx)

    /// <summary>无可用值班成员（任一必需组队列或全体成员为空）</summary>
    public const string DUTY_NO_MEMBERS = "DUTY_NO_MEMBERS";

    /// <summary>该月值班表已生成（需强制重生成时二次确认）</summary>
    public const string DUTY_SCHEDULE_EXISTS = "DUTY_SCHEDULE_EXISTS";

    /// <summary>值班班次不存在</summary>
    public const string DUTY_SCHEDULE_NOT_FOUND = "DUTY_SCHEDULE_NOT_FOUND";

    /// <summary>值班成员不存在</summary>
    public const string DUTY_MEMBER_NOT_FOUND = "DUTY_MEMBER_NOT_FOUND";

    /// <summary>成员当日已有班次，不能重复安排</summary>
    public const string DUTY_MEMBER_CONFLICT = "DUTY_MEMBER_CONFLICT";

    /// <summary>值班日期无效</summary>
    public const string DUTY_INVALID_DATE = "DUTY_INVALID_DATE";

    /// <summary>请假区间无效（结束早于开始或区间为空）</summary>
    public const string DUTY_LEAVE_INVALID_RANGE = "DUTY_LEAVE_INVALID_RANGE";

    /// <summary>补登记过去日期的请假必须填写事由</summary>
    public const string DUTY_LEAVE_BACKFILL_REASON_REQUIRED = "DUTY_LEAVE_BACKFILL_REASON_REQUIRED";

    /// <summary>班务调整日期无效（串班/代班仅限今天及以后，过去班次不可调整）</summary>
    public const string DUTY_SHIFT_INVALID_DATE = "DUTY_SHIFT_INVALID_DATE";

    /// <summary>班务调整目标班次不存在或不属于该人员</summary>
    public const string DUTY_SHIFT_NOT_FOUND = "DUTY_SHIFT_NOT_FOUND";

    /// <summary>顶班人员无效（不同组/已停用/当日不可用）</summary>
    public const string DUTY_SHIFT_MEMBER_INVALID = "DUTY_SHIFT_MEMBER_INVALID";

    /// <summary>班务调整记录不存在</summary>
    public const string DUTY_CHANGE_NOT_FOUND = "DUTY_CHANGE_NOT_FOUND";

    /// <summary>该班次已有一条生效的班务调整记录（一个班次同一时间只允许一条生效调整）</summary>
    public const string DUTY_SHIFT_ALREADY_ADJUSTED = "DUTY_SHIFT_ALREADY_ADJUSTED";

    #endregion

    #region 节假日 (HOLIDAY_xxx)

    /// <summary>节假日接口不可达（网络受限或服务异常）</summary>
    public const string HOLIDAY_API_UNREACHABLE = "HOLIDAY_API_UNREACHABLE";

    /// <summary>节假日接口返回数据无效</summary>
    public const string HOLIDAY_DATA_INVALID = "HOLIDAY_DATA_INVALID";

    /// <summary>节假日日期已存在</summary>
    public const string HOLIDAY_DUPLICATE_DATE = "HOLIDAY_DUPLICATE_DATE";

    #endregion

    #region 文档处理 (DOC_xxx)

    public const string DOCUMENT_GENERATION_FAILED = "DOCUMENT_GENERATION_FAILED";
    public const string DOCUMENT_PLACEHOLDER_UNRESOLVED = "DOCUMENT_PLACEHOLDER_UNRESOLVED";
    public const string DOCUMENT_PDF_CONVERSION_FAILED = "DOCUMENT_PDF_CONVERSION_FAILED";

    #endregion

    #region 打印相关 (PRINT_xxx)

    public const string PRINT_FAILED = "PRINT_FAILED";
    public const string PRINT_JOB_TIMEOUT = "PRINT_JOB_TIMEOUT";
    public const string PRINT_TASK_RETRY_EXHAUSTED = "PRINT_TASK_RETRY_EXHAUSTED";
    public const string COM_INSTANCE_FAILED = "COM_INSTANCE_FAILED";
    public const string COM_SERVER_CRASHED = "COM_SERVER_CRASHED";
    public const string COM_TIMEOUT = "COM_TIMEOUT";
    public const string OFFICE_NOT_INSTALLED = "OFFICE_NOT_INSTALLED";

    #endregion

    #region 业务 (BIZ_xxx 补充)

    public const string CONCURRENCY_CONFLICT = "CONCURRENCY_CONFLICT";
    public const string APPLICATION_ALREADY_ARCHIVED = "APPLICATION_ALREADY_ARCHIVED";
    public const string INVALID_CLASSIFICATION = "INVALID_CLASSIFICATION";
    public const string SPECIAL_APPROVAL_ALREADY_EXISTS = "SPECIAL_APPROVAL_ALREADY_EXISTS";
    public const string SPECIAL_APPROVAL_FORM_NOT_FOUND = "SPECIAL_APPROVAL_FORM_NOT_FOUND";
    public const string SPECIAL_APPROVAL_ALREADY_SUBMITTED = "SPECIAL_APPROVAL_ALREADY_SUBMITTED";
    public const string SPECIAL_APPROVAL_REVIEW_DONE = "SPECIAL_APPROVAL_REVIEW_DONE";
    public const string SPECIAL_APPROVAL_TEMPLATE_NOT_FOUND = "SPECIAL_APPROVAL_TEMPLATE_NOT_FOUND";
    public const string TIMELINE_CALCULATION_FAILED = "TIMELINE_CALCULATION_FAILED";

    /// <summary>分类判定失败</summary>
    public const string CLASSIFICATION_FAILED = "CLASSIFICATION_FAILED";

    /// <summary>档案不在当前复核周期内</summary>
    public const string ECONOMIC_REVIEW_OUT_OF_PERIOD = "ECONOMIC_REVIEW_OUT_OF_PERIOD";

    /// <summary>档案状态无效（只能修正已批准状态的档案）</summary>
    public const string ECONOMIC_REVIEW_INVALID_STATUS = "ECONOMIC_REVIEW_INVALID_STATUS";

    /// <summary>标准配置未找到</summary>
    public const string CONFIG_NOT_FOUND = "CONFIG_NOT_FOUND";

    /// <summary>高龄类别复核评估失败</summary>
    public const string REVIEW_EVALUATE_FAILED = "REVIEW_EVALUATE_FAILED";

    /// <summary>高龄档案状态无效（仅支持在享已确认档案复核）</summary>
    public const string ELDERLY_REVIEW_INVALID_STATUS = "ELDERLY_REVIEW_INVALID_STATUS";

    #endregion

    #region 文件操作 (FILE_xxx)

    /// <summary>文件未找到</summary>
    public const string FILE_NOT_FOUND = "FILE_NOT_FOUND";

    /// <summary>文件格式错误</summary>
    public const string FILE_FORMAT_ERROR = "FILE_FORMAT_ERROR";

    /// <summary>文件导出失败</summary>
    public const string FILE_EXPORT_FAILED = "FILE_EXPORT_FAILED";

    /// <summary>文件导入失败</summary>
    public const string FILE_IMPORT_FAILED = "FILE_IMPORT_FAILED";

    /// <summary>文件读取失败</summary>
    public const string FILE_READ_ERROR = "FILE_READ_ERROR";

    /// <summary>文件写入失败</summary>
    public const string FILE_WRITE_ERROR = "FILE_WRITE_ERROR";

    #endregion

    #region 外部依赖 (NETWORK_xxx)

    /// <summary>网络错误</summary>
    public const string NETWORK_ERROR = "NETWORK_ERROR";

    /// <summary>ZeroTier 管理令牌无读权限（需一次性管理员授权）</summary>
    public const string ZEROTIER_TOKEN_DENIED = "ZEROTIER_TOKEN_DENIED";

    #endregion

    #region 操作状态 (OP_xxx)

    /// <summary>操作进行中</summary>
    public const string OPERATION_IN_PROGRESS = "OPERATION_IN_PROGRESS";

    /// <summary>操作已取消</summary>
    public const string CANCELLED = "CANCELLED";

    /// <summary>未知错误</summary>
    public const string UNKNOWN_ERROR = "UNKNOWN_ERROR";

    /// <summary>操作失败</summary>
    public const string OPERATION_FAILED = "OPERATION_FAILED";

    #endregion

    #region 地区管理 (REGION_xxx)

    /// <summary>地级市未找到</summary>
    public const string REGION_CITY_NOT_FOUND = "REGION_CITY_NOT_FOUND";

    /// <summary>县区未找到</summary>
    public const string REGION_COUNTY_NOT_FOUND = "REGION_COUNTY_NOT_FOUND";

    /// <summary>乡镇未找到</summary>
    public const string REGION_TOWN_NOT_FOUND = "REGION_TOWN_NOT_FOUND";

    /// <summary>村/社区未找到</summary>
    public const string REGION_VILLAGE_NOT_FOUND = "REGION_VILLAGE_NOT_FOUND";

    /// <summary>地区名称重复</summary>
    public const string REGION_NAME_DUPLICATE = "REGION_NAME_DUPLICATE";

    /// <summary>地区存在下级数据</summary>
    public const string REGION_HAS_CHILDREN = "REGION_HAS_CHILDREN";

    #endregion
}
