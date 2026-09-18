namespace NewCosmos.Helpers;

/// <summary>
/// 导入数据映射工具类
/// 将历史导入数据的非标准值映射为字典标准值
/// 确保 Picker 选项能正确匹配
/// </summary>
public static class ImportedDataMapper
{
    /// <summary>
    /// 文化程度映射
    /// </summary>
    private static readonly Dictionary<string, string> EducationLevelMap = new()
    {
        ["小学教育"] = "小学",
        ["初级中学教育"] = "初中",
        ["普通高级中学教育"] = "高中/中专",
        ["大学本科教育"] = "本科",
        ["专科教育"] = "大专",
        ["文盲或半文盲"] = "文盲/半文盲",
        ["文盲"] = "文盲/半文盲",
        ["半文盲"] = "文盲/半文盲",
        ["高中"] = "高中/中专",
        ["中专"] = "高中/中专",
    };

    /// <summary>
    /// 婚姻状况映射
    /// </summary>
    private static readonly Dictionary<string, string> MaritalStatusMap = new()
    {
        ["未说明的婚姻状况"] = "其他",
        ["有配偶"] = "已婚",
    };

    /// <summary>
    /// 劳动能力映射
    /// </summary>
    private static readonly Dictionary<string, string> WorkCapacityMap = new()
    {
        ["无劳动能力"] = "完全丧失劳动能力",
        ["丧失劳动能力"] = "完全丧失劳动能力",
    };

    /// <summary>
    /// 就业状况映射
    /// </summary>
    private static readonly Dictionary<string, string> EmploymentStatusMap = new()
    {
        ["无工作"] = "失业",
        ["务农人员"] = "务农",
        ["在校学生"] = "学生",
        ["灵活就业人员"] = "其他",
        ["未登记失业人员"] = "失业",
        ["登记失业人员"] = "失业",
        ["无业"] = "失业",
    };

    /// <summary>
    /// 残疾类别映射（补"残疾"后缀）
    /// </summary>
    private static readonly Dictionary<string, string> DisabilityTypeMap = new()
    {
        ["肢体"] = "肢体残疾",
        ["智力"] = "智力残疾",
        ["精神"] = "精神残疾",
        ["视力"] = "视力残疾",
        ["听力"] = "听力残疾",
        ["言语"] = "言语残疾",
        ["多重"] = "多重残疾",
    };

    /// <summary>
    /// 残疾等级映射（补描述）
    /// </summary>
    private static readonly Dictionary<string, string> DisabilityLevelMap = new()
    {
        ["一级"] = "一级（极重度）",
        ["二级"] = "二级（重度）",
        ["三级"] = "三级（中度）",
        ["四级"] = "四级（轻度）",
    };

    /// <summary>
    /// 健康状况映射（中文值 → 字典显示值）
    /// </summary>
    private static readonly Dictionary<string, string> HealthStatusMap = new()
    {
        ["健康"] = "健康或良好",
        ["良好"] = "健康或良好",
        ["一般"] = "一般或较弱",
        ["较弱"] = "一般或较弱",
    };

    /// <summary>
    /// 救助类型映射（中文值 → 字典 code）
    /// </summary>
    private static readonly Dictionary<string, string> AssistanceTypeMap = new()
    {
        ["农村低保"] = "Subsistence",
        ["城市低保"] = "Subsistence",
        ["最低生活保障"] = "Subsistence",
        ["特困供养"] = "DestituteSupport",
        ["特困人员供养"] = "DestituteSupport",
        ["农村特困"] = "DestituteSupport",
        ["城市特困"] = "DestituteSupport",
    };

    /// <summary>
    /// 家庭关系映射（中文值 → 字典显示值）
    /// </summary>
    private static readonly Dictionary<string, string> FamilyRelationshipMap = new()
    {
        ["户主"] = "本人/户主",
        ["本人"] = "本人/户主",
        ["本人/户主"] = "本人/户主",
        ["子"] = "子/婿",
        ["婿"] = "子/婿",
        ["儿子"] = "子/婿",
        ["女"] = "女/媳",
        ["媳"] = "女/媳",
        ["女儿"] = "女/媳",
        ["孙子女"] = "孙子女/外孙子女",
        ["（外）孙子女"] = "孙子女/外孙子女",
        ["孙子"] = "孙子女/外孙子女",
        ["孙女"] = "孙子女/外孙子女",
        ["外孙"] = "孙子女/外孙子女",
        ["外孙女"] = "孙子女/外孙子女",
        ["父母"] = "父母/岳父母/公婆",
        ["岳父母"] = "父母/岳父母/公婆",
        ["公婆"] = "父母/岳父母/公婆",
        ["父亲"] = "父母/岳父母/公婆",
        ["母亲"] = "父母/岳父母/公婆",
        ["祖父母"] = "祖父母/外祖父母",
        ["外祖父母"] = "祖父母/外祖父母",
        ["爷爷"] = "祖父母/外祖父母",
        ["奶奶"] = "祖父母/外祖父母",
        ["外公"] = "祖父母/外祖父母",
        ["外婆"] = "祖父母/外祖父母",
        ["兄弟姐妹"] = "兄弟姐妹",
        ["哥哥"] = "兄弟姐妹",
        ["弟弟"] = "兄弟姐妹",
        ["姐姐"] = "兄弟姐妹",
        ["妹妹"] = "兄弟姐妹",
        ["其他"] = "其他",
    };

    /// <summary>
    /// 映射文化程度
    /// </summary>
    public static string MapEducationLevel(string? value)
    {
        return MapValue(EducationLevelMap, value);
    }

    /// <summary>
    /// 映射婚姻状况
    /// </summary>
    public static string MapMaritalStatus(string? value)
    {
        return MapValue(MaritalStatusMap, value);
    }

    /// <summary>
    /// 映射劳动能力
    /// </summary>
    public static string MapWorkCapacity(string? value)
    {
        return MapValue(WorkCapacityMap, value);
    }

    /// <summary>
    /// 映射就业状况
    /// </summary>
    public static string MapEmploymentStatus(string? value)
    {
        return MapValue(EmploymentStatusMap, value);
    }

    /// <summary>
    /// 映射残疾类别
    /// </summary>
    public static string MapDisabilityType(string? value)
    {
        return MapValue(DisabilityTypeMap, value);
    }

    /// <summary>
    /// 映射残疾等级
    /// </summary>
    public static string MapDisabilityLevel(string? value)
    {
        return MapValue(DisabilityLevelMap, value);
    }

    /// <summary>
    /// 映射健康状况（中文值 → 字典显示值）
    /// </summary>
    public static string MapHealthStatus(string? value)
    {
        return MapValue(HealthStatusMap, value);
    }

    /// <summary>
    /// 映射家庭关系（中文值 → 字典显示值）
    /// </summary>
    public static string MapFamilyRelationship(string? value)
    {
        return MapValue(FamilyRelationshipMap, value);
    }

    /// <summary>
    /// 映射救助类型（中文值 → 字典 code）
    /// </summary>
    public static string MapAssistanceType(string? value)
    {
        return MapValue(AssistanceTypeMap, value);
    }

    /// <summary>
    /// 映射所有可能不匹配的字段
    /// </summary>
    public static void MapAllFields(
        string? educationLevel,
        string? maritalStatus,
        string? workCapacity,
        string? employmentStatus,
        string? disabilityType,
        string? disabilityLevel,
        out string mappedEducationLevel,
        out string mappedMaritalStatus,
        out string mappedWorkCapacity,
        out string mappedEmploymentStatus,
        out string mappedDisabilityType,
        out string mappedDisabilityLevel)
    {
        mappedEducationLevel = MapEducationLevel(educationLevel);
        mappedMaritalStatus = MapMaritalStatus(maritalStatus);
        mappedWorkCapacity = MapWorkCapacity(workCapacity);
        mappedEmploymentStatus = MapEmploymentStatus(employmentStatus);
        mappedDisabilityType = MapDisabilityType(disabilityType);
        mappedDisabilityLevel = MapDisabilityLevel(disabilityLevel);
    }

    /// <summary>
    /// 户口类型映射（原始值 → 标准显示值）
    /// </summary>
    private static readonly Dictionary<string, string> HukouTypeMap = new()
    {
        ["农村户口"] = "农村户口",
        ["农业户口"] = "农村户口",
        ["农村"] = "农村户口",
        ["农业"] = "农村户口",
        ["城市户口"] = "城市户口",
        ["非农业户口"] = "城市户口",
        ["城镇"] = "城市户口",
        ["非农"] = "城市户口",
    };

    /// <summary>
    /// 原始值 → item_key 统一入口
    /// 第一步：原始值 → 标准显示值（通过 Map 方法）
    /// 第二步：标准显示值 → item_key（通过 DictCacheService）
    /// </summary>
    public static string MapToKey(string? rawValue, string category, Services.Core.IDictCacheService dictCache)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;

        var standardValue = MapByCategory(rawValue.Trim(), category);
        var key = dictCache.GetKeyByValue(category, standardValue);
        return key ?? string.Empty;
    }

    /// <summary>
    /// 根据 category 分发到对应的 Map 方法
    /// </summary>
    public static string MapByCategory(string value, string category)
    {
        return category switch
        {
            "EducationLevels" => MapEducationLevel(value),
            "MaritalStatuses" => MapMaritalStatus(value),
            "HukouTypes" => MapHukouType(value),
            "HealthStatuses" => MapHealthStatus(value),
            "DisabilityTypes" => MapDisabilityType(value),
            "DisabilityLevels" => MapDisabilityLevel(value),
            "EmploymentStatuses" => MapEmploymentStatus(value),
            "LaborAbilities" => MapWorkCapacity(value),
            "AssistanceTypes" => MapAssistanceType(value),
            "FamilyRelationships" => MapFamilyRelationship(value),
            _ => value
        };
    }

    /// <summary>
    /// 映射户口类型
    /// </summary>
    public static string MapHukouType(string? value)
    {
        return MapValue(HukouTypeMap, value);
    }

    /// <summary>
    /// 通用映射方法
    /// </summary>
    private static string MapValue(Dictionary<string, string> map, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;

        var trimmed = value.Trim();
        return map.TryGetValue(trimmed, out var mapped) ? mapped : trimmed;
    }
}
