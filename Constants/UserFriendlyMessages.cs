namespace NewCosmos.Constants;

/// <summary>
/// 用户友好提示信息映射
/// </summary>
public static class UserFriendlyMessages
{
    private static readonly Dictionary<string, string> Messages = new()
    {
        #region 数据验证

        [ErrorCodes.VALIDATION_FAILED] = "数据验证失败，请检查输入内容",
        [ErrorCodes.REQUIRED_FIELD_MISSING] = "必填字段不能为空",
        [ErrorCodes.INVALID_ID_CARD] = "身份证号格式不正确",
        [ErrorCodes.INVALID_PHONE] = "手机号格式不正确",
        [ErrorCodes.DUPLICATE_ID_CARD] = "该身份证号已存在，请检查",
        [ErrorCodes.INVALID_NAME] = "姓名格式不正确",
        [ErrorCodes.INVALID_AMOUNT] = "金额格式不正确",

        #endregion

        #region 业务规则

        [ErrorCodes.INVALID_TRANSITION] = "当前状态无法执行此操作",
        [ErrorCodes.MEMBER_IS_HEAD] = "户主不能直接删除，请先变更户主",
        [ErrorCodes.LAST_MEMBER_CANNOT_DELETE] = "最后一个家庭成员不能删除",
        [ErrorCodes.INCOME_OUT_OF_RANGE] = "收入超出认定范围",
        [ErrorCodes.GRACE_PERIOD_INVALID_MONTHS] = "渐退期月数范围为1-12个月",
        [ErrorCodes.APPLICATION_ALREADY_EXISTS] = "申请记录已存在",
        [ErrorCodes.APPLICATION_ALREADY_SUBMITTED] = "申请已提交，无法修改",
        [ErrorCodes.ANNUAL_LIMIT_EXCEEDED] = "该申请人本年度已申请过，每年仅限一次",
        [ErrorCodes.NOT_FOUND] = "未找到相关记录",
        [ErrorCodes.DUPLICATE_USERNAME] = "用户名已存在，请更换",
        [ErrorCodes.DUPLICATE_PHONE] = "该手机号已被使用",

        #endregion

        #region 实体未找到
        [ErrorCodes.APPLICATION_NOT_FOUND] = "未找到申请记录",
        [ErrorCodes.FAMILY_MEMBER_NOT_FOUND] = "未找到家庭成员记录",
        [ErrorCodes.FAMILY_NOT_FOUND] = "未找到家庭记录",
        [ErrorCodes.USER_NOT_FOUND] = "未找到用户",
        [ErrorCodes.ROLE_NOT_FOUND] = "未找到角色",
        [ErrorCodes.PERMISSION_NOT_FOUND] = "未找到权限",
        [ErrorCodes.ARCHIVE_NOT_FOUND] = "未找到档案记录",
        [ErrorCodes.RECORD_NOT_FOUND] = "未找到记录",

        #endregion

        #region 数据库错误
        [ErrorCodes.DB_CONNECTION_FAILED] = "数据库连接失败，请检查网络后重试",
        [ErrorCodes.DB_TIMEOUT] = "操作超时，请稍后重试",
        [ErrorCodes.DB_UNIQUE_VIOLATION] = "数据冲突，该记录已存在",
        [ErrorCodes.DB_FOREIGN_KEY_VIOLATION] = "操作失败，请检查关联数据",
        [ErrorCodes.DB_NOT_NULL_VIOLATION] = "请填写所有必填项",
        [ErrorCodes.DB_QUERY_ERROR] = "查询失败，请稍后重试",
        [ErrorCodes.DB_TRANSACTION_ERROR] = "操作失败，请重试",

        #endregion

        #region 权限错误

        [ErrorCodes.PERMISSION_DENIED] = "权限不足，无法执行此操作",
        [ErrorCodes.AUTHENTICATION_FAILED] = "用户名或密码错误",
        [ErrorCodes.SESSION_EXPIRED] = "会话已过期，请重新登录",
        [ErrorCodes.ACCOUNT_LOCKED] = "账户已锁定，请联系管理员",
        [ErrorCodes.INVALID_PASSWORD] = "密码错误",

        #endregion

        #region 文件操作

        [ErrorCodes.FILE_NOT_FOUND] = "文件不存在",
        [ErrorCodes.FILE_FORMAT_ERROR] = "文件格式不正确",
        [ErrorCodes.FILE_EXPORT_FAILED] = "导出失败，请稍后重试",
        [ErrorCodes.FILE_IMPORT_FAILED] = "导入失败，请检查文件格式",
        [ErrorCodes.FILE_READ_ERROR] = "文件读取失败",
        [ErrorCodes.FILE_WRITE_ERROR] = "文件保存失败",

        #endregion

        #region 操作状态
        [ErrorCodes.OPERATION_IN_PROGRESS] = "操作进行中，请稍候",
        [ErrorCodes.CANCELLED] = "操作已取消",
        [ErrorCodes.UNKNOWN_ERROR] = "操作失败，请稍后重试",
        [ErrorCodes.OPERATION_FAILED] = "操作失败",

        #endregion

        #region 外部依赖

        [ErrorCodes.NETWORK_ERROR] = "网络连接失败，请检查网络后重试",
        [ErrorCodes.ZEROTIER_TOKEN_DENIED] = "ZeroTier 接入权限未授权（需要一次管理员授权，点击“接入网络”按提示继续）",

        #endregion

        #region 业务补充

        [ErrorCodes.CONCURRENCY_CONFLICT] = "数据已被其他人修改，请刷新后重试",
        [ErrorCodes.APPLICATION_ALREADY_ARCHIVED] = "该申请已归档，无法操作",
        [ErrorCodes.INVALID_CLASSIFICATION] = "分类判定结果无效",
        [ErrorCodes.SPECIAL_APPROVAL_ALREADY_EXISTS] = "一事一议申请已存在",
        [ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND] = "未找到一事一议申报表",
        [ErrorCodes.SPECIAL_APPROVAL_ALREADY_SUBMITTED] = "申报表已提交，无法修改",
        [ErrorCodes.SPECIAL_APPROVAL_REVIEW_DONE] = "该申报表已完成会议审议",
        [ErrorCodes.SPECIAL_APPROVAL_TEMPLATE_NOT_FOUND] = "未找到一事一议申报表模板，请先在模板管理中导入",
        [ErrorCodes.TIMELINE_CALCULATION_FAILED] = "时间线计算失败",
        [ErrorCodes.CLASSIFICATION_FAILED] = "分类判定失败，请检查家庭经济与成员信息",
        [ErrorCodes.CONFIG_NOT_FOUND] = "未找到相关标准配置，请先在标准配置管理中维护",

        #endregion

        #region 模板相关

        [ErrorCodes.TEMPLATE_NOT_FOUND] = "未找到模板",
        [ErrorCodes.TEMPLATE_SYNTACTICALLY_INVALID] = "模板语法无效",
        [ErrorCodes.TEMPLATE_UNSUPPORTED_TYPE] = "不支持的模板类型",
        [ErrorCodes.TEMPLATE_ALREADY_EXISTS] = "模板已存在",
        [ErrorCodes.TEMPLATE_UPLOAD_FAILED] = "模板上传失败",

        #endregion

        #region 文档处理

        [ErrorCodes.DOCUMENT_GENERATION_FAILED] = "文档生成失败",
        [ErrorCodes.DOCUMENT_PLACEHOLDER_UNRESOLVED] = "文档中存在未解析的占位符",
        [ErrorCodes.DOCUMENT_PDF_CONVERSION_FAILED] = "PDF转换失败",

        #endregion

        #region 打印相关

        [ErrorCodes.PRINT_FAILED] = "打印失败",
        [ErrorCodes.PRINT_JOB_TIMEOUT] = "打印任务超时",
        [ErrorCodes.PRINT_TASK_RETRY_EXHAUSTED] = "打印重试次数已用尽",
        [ErrorCodes.COM_INSTANCE_FAILED] = "COM组件实例创建失败",
        [ErrorCodes.COM_SERVER_CRASHED] = "COM服务已崩溃",
        [ErrorCodes.COM_TIMEOUT] = "COM操作超时",
        [ErrorCodes.OFFICE_NOT_INSTALLED] = "未检测到Office软件",

        #endregion

        #region 地区管理

        [ErrorCodes.REGION_COUNTY_NOT_FOUND] = "未找到县区信息",
        [ErrorCodes.REGION_TOWN_NOT_FOUND] = "未找到乡镇信息",
        [ErrorCodes.REGION_VILLAGE_NOT_FOUND] = "未找到村/社区信息",
        [ErrorCodes.REGION_CITY_NOT_FOUND] = "未找到城市信息",
        [ErrorCodes.REGION_NAME_DUPLICATE] = "地区名称已存在",
        [ErrorCodes.REGION_HAS_CHILDREN] = "该地区存在下级数据，无法删除",

        #endregion

        #region 值班管理

        [ErrorCodes.DUTY_NO_MEMBERS] = "值班成员名单为空，请先在成员管理中维护各组成员",
        [ErrorCodes.DUTY_SCHEDULE_EXISTS] = "该月值班表已生成，如需重新生成请先删除该月值班表",
        [ErrorCodes.DUTY_SCHEDULE_NOT_FOUND] = "未找到值班班次记录",
        [ErrorCodes.DUTY_MEMBER_NOT_FOUND] = "未找到值班成员",
        [ErrorCodes.DUTY_MEMBER_CONFLICT] = "该成员当日已有值班安排，不能重复安排",
        [ErrorCodes.DUTY_INVALID_DATE] = "值班日期无效",
        [ErrorCodes.DUTY_LEAVE_INVALID_RANGE] = "请假区间无效，结束日期不能早于开始日期",
        [ErrorCodes.DUTY_LEAVE_BACKFILL_REASON_REQUIRED] = "补登记过去日期的请假必须填写事由",
        [ErrorCodes.DUTY_SHIFT_INVALID_DATE] = "仅支持对今天及以后的值班进行调整，过去班次不可操作",
        [ErrorCodes.DUTY_SHIFT_NOT_FOUND] = "未找到该日期的班次，或该班次不属于所选人员",
        [ErrorCodes.DUTY_SHIFT_MEMBER_INVALID] = "顶班人员无效：须为同组启用成员且当日可参与",
        [ErrorCodes.DUTY_CHANGE_NOT_FOUND] = "未找到班务调整记录，或记录已撤销",
        [ErrorCodes.DUTY_SHIFT_ALREADY_ADJUSTED] = "该日期的班次已有一条生效的调整记录，请先撤销或改期",

        #endregion

        #region 节假日

        [ErrorCodes.HOLIDAY_API_UNREACHABLE] = "节假日数据接口不可达，请检查网络或改用手工维护",
        [ErrorCodes.HOLIDAY_DATA_INVALID] = "节假日接口返回数据无效，请稍后重试或手工维护",
        [ErrorCodes.HOLIDAY_DUPLICATE_DATE] = "该日期的节假日记录已存在",

        #endregion
    };

    /// <summary>
    /// 获取用户友好提示信息
    /// </summary>
    /// <param name="errorCode">错误码</param>
    /// <param name="detail">详细信息（可选）</param>
    /// <returns>用户友好提示信息</returns>
    public static string Get(string errorCode, string detail = null)
    {
        var baseMessage = Messages.TryGetValue(errorCode, out var msg)
            ? msg
            : "操作失败";

        return string.IsNullOrEmpty(detail)
            ? baseMessage
            : $"{baseMessage}：{detail}";
    }

    /// <summary>
    /// 获取用户友好提示信息（带格式化参数）
    /// </summary>
    public static string GetFormat(string errorCode, params object[] args)
    {
        var baseMessage = Messages.TryGetValue(errorCode, out var msg)
            ? msg
            : "操作失败";

        if (args.Length == 0) return baseMessage;

        try
        {
            return string.Format(baseMessage, args);
        }
        catch (FormatException)
        {
            return $"{baseMessage}：{string.Join(", ", args)}";
        }
    }

    /// <summary>
    /// 判断是否包含指定错误码的提示信息
    /// </summary>
    public static bool Contains(string errorCode) => Messages.ContainsKey(errorCode);

    /// <summary>
    /// 获取所有已定义的错误码
    /// </summary>
    public static IReadOnlyList<string> GetAllErrorCodes() => Messages.Keys.ToList();
}
