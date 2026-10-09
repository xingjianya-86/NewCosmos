namespace NewCosmos.Constants;

/// <summary>
/// 字典常量
/// </summary>
public static class DictionaryConstants
{
    /// <summary>
    /// 家庭关系
    /// </summary>
    public static class FamilyRelationship
    {
        public const string HEAD = "Head";
        public const string SPOUSE = "Spouse";
        public const string SON = "Son";
        public const string DAUGHTER = "Daughter";
        public const string GRANDCHILD = "Grandchild";
        public const string PARENT = "Parent";
        public const string GRANDPARENT = "Grandparent";
        public const string SIBLING = "Sibling";
        public const string OTHER = "Other";

        public static readonly string[] All =
        {
            HEAD, SPOUSE, SON, DAUGHTER, GRANDCHILD,
            PARENT, GRANDPARENT, SIBLING, OTHER
        };

        /// <summary>
        /// 判断关系是否为配偶关系
        /// </summary>
        public static bool IsSpouse(string relation) => relation == SPOUSE;
    }

    /// <summary>
    /// 自理能力
    /// </summary>
    public static class SelfCareAbility
    {
        public const string SELF_CARE = "能自理";
        public const string PARTIAL_CARE = "部分自理";
        public const string NO_CARE = "不能自理";

        public static readonly string[] All = { SELF_CARE, PARTIAL_CARE, NO_CARE };
    }

    /// <summary>
    /// 能力鉴定等级（六项考核指标）
    /// </summary>
    public static class CapabilityLevel
    {
        public const string FULL_SELF_CARE = "全自理";
        public const string MILD_DISABILITY = "轻度失能";
        public const string MODERATE_DISABILITY = "中度失能";
        public const string SEVERE_DISABILITY = "重度失能";
        public const string COMPLETE_DISABILITY = "完全失能";

        public static readonly string[] All = { FULL_SELF_CARE, MILD_DISABILITY, MODERATE_DISABILITY, SEVERE_DISABILITY, COMPLETE_DISABILITY };

        /// <summary>
        /// 根据能完成项目数判定等级
        /// </summary>
        public static string DetermineLevel(int completedItems)
        {
            return completedItems switch
            {
                6 => FULL_SELF_CARE,
                5 => MILD_DISABILITY,
                3 or 4 => MODERATE_DISABILITY,
                1 or 2 => SEVERE_DISABILITY,
                _ => COMPLETE_DISABILITY
            };
        }

        /// <summary>
        /// 能力鉴定等级 → 三档自理能力（档案输出：自理/半自理/不能自理）
        /// </summary>
        public static string ToSelfCareDisplay(string capabilityLevel) => capabilityLevel switch
        {
            DictionaryConstants.CapabilityLevel.FULL_SELF_CARE => "自理",
            DictionaryConstants.CapabilityLevel.MILD_DISABILITY => "半自理",
            DictionaryConstants.CapabilityLevel.MODERATE_DISABILITY => "半自理",
            DictionaryConstants.CapabilityLevel.SEVERE_DISABILITY => "不能自理",
            DictionaryConstants.CapabilityLevel.COMPLETE_DISABILITY => "不能自理",
            _ => "未评估"
        };
    }

    /// <summary>
    /// 照料等级
    /// </summary>
    public static class CareLevel
    {
        public const string SELF_CARE = "自理";
        public const string LEVEL_2 = "二级照料";
        public const string LEVEL_1 = "一级照料";
        public const string SPECIAL = "特级照料";

        public static readonly string[] All = { SELF_CARE, LEVEL_2, LEVEL_1, SPECIAL };

        /// <summary>
        /// 根据自理能力等级判定照料等级
        /// </summary>
        public static string DetermineByCapability(string capabilityLevel)
        {
            return capabilityLevel switch
            {
                DictionaryConstants.CapabilityLevel.FULL_SELF_CARE => SELF_CARE,
                DictionaryConstants.CapabilityLevel.MILD_DISABILITY => LEVEL_2,
                DictionaryConstants.CapabilityLevel.MODERATE_DISABILITY => LEVEL_1,
                DictionaryConstants.CapabilityLevel.SEVERE_DISABILITY => SPECIAL,
                DictionaryConstants.CapabilityLevel.COMPLETE_DISABILITY => SPECIAL,
                _ => SELF_CARE
            };
        }
    }

    /// <summary>
    /// 刚性支出类型
    /// </summary>
    public static class RigidExpenditureType
    {
        public const string MEDICAL = "Medical";
        public const string EDUCATION = "Education";
        public const string DISABILITY_REHAB = "DisabilityRehab";
        public const string LIVING = "Living";

        public static readonly string[] All = { MEDICAL, EDUCATION, DISABILITY_REHAB, LIVING };
    }

    /// <summary>
    /// 劳动能力
    /// </summary>
    public static class LaborAbility
    {
        public const string HAS_ABILITY = "有劳动能力";
        public const string PARTIAL_ABILITY = "部分劳动能力";
        public const string NO_ABILITY = "丧失劳动能力";
        public const string COMPLETE_LOSS = "完全丧失劳动能力";
        public const string PARTIAL_LOSS = "部分丧失劳动能力";

        public static readonly string[] All = { HAS_ABILITY, PARTIAL_ABILITY, NO_ABILITY, COMPLETE_LOSS, PARTIAL_LOSS };

        /// <summary>
        /// 从健康状况推断劳动能力
        /// </summary>
        public static string InferFromHealthStatus(string healthStatus)
        {
            return healthStatus switch
            {
                HealthStatus.HEALTHY => HAS_ABILITY,
                HealthStatus.WEAK => PARTIAL_ABILITY,
                HealthStatus.SEVERE_DISEASE => NO_ABILITY,
                HealthStatus.SEVERE_DISABILITY => NO_ABILITY,
                HealthStatus.SEVERE_DISEASE_AND_DISABILITY => NO_ABILITY,
                _ => string.Empty
            };
        }
    }

    /// <summary>
    /// 健康状况
    /// </summary>
    public static class HealthStatus
    {
        public const string HEALTHY = "Healthy";
        public const string WEAK = "Weak";
        public const string SEVERE_DISEASE = "SevereIllness";
        public const string SEVERE_DISABILITY = "SevereDisability";
        public const string SEVERE_DISEASE_AND_DISABILITY = "SevereIllnessAndDisability";

        public static readonly string[] All = { HEALTHY, WEAK, SEVERE_DISEASE, SEVERE_DISABILITY, SEVERE_DISEASE_AND_DISABILITY };

        public static bool HasSevereDisease(string healthStatus)
        {
            return !string.IsNullOrEmpty(healthStatus) &&
                   (healthStatus == SEVERE_DISEASE || healthStatus == SEVERE_DISEASE_AND_DISABILITY);
        }

        public static bool HasSevereDisability(string healthStatus)
        {
            return !string.IsNullOrEmpty(healthStatus) &&
                   (healthStatus == SEVERE_DISABILITY || healthStatus == SEVERE_DISEASE_AND_DISABILITY);
        }
    }

    /// <summary>
    /// 变更类型
    /// </summary>
    public static class ChangeType
    {
        public const string FUND_CHANGE = "FundChange";
        public const string MEMBER_ATTRIBUTE = "MemberAttribute";
        public const string MEMBER_ADD = "MemberAdd";
        public const string MEMBER_REMOVE = "MemberRemove";
        public const string MEMBER_DEATH = "MemberDeath";
        public const string HOUSEHOLD_DEATH = "HouseholdDeath";
        public const string HOUSEHOLD_HEAD_CHANGE = "HouseholdHeadChange";
        public const string DRAFT_SAVE = "DraftSave";
        public const string MEMBER_MODIFY = "MemberModify";

        /// <summary>跨大类变更：原类别停止（旧分类停止享受，供月报"停保汇总表"体现）</summary>
        public const string CATEGORY_STOP = "CategoryStop";

        /// <summary>跨大类变更：新类别新增（新分类开始享受，供月报"新增救助明细"体现）</summary>
        public const string CATEGORY_ADD = "CategoryAdd";

        /// <summary>户主死亡进入渐退期时的分类施保减法记录（原分类施保不再享受，挂旧档）</summary>
        public const string CLASSIFIED_SUBSIDY_REDUCE = "ClassifiedSubsidyReduce";
    }

    /// <summary>
    /// 变更类别（nc_biz_change_records.change_category）
    /// </summary>
    public static class ChangeCategory
    {
        public const string FUND_CHANGE = "FundChange";
        public const string MEMBER_ATTRIBUTE = "MemberAttribute";

        /// <summary>成员增减变更（档案输出"增减员调整表"按此类别取值）</summary>
        public const string MEMBER_CHANGE = "MemberChange";
    }

    /// <summary>
    /// 操作模式
    /// </summary>
    public static class OperationMode
    {
        public const string NEW_APPLICATION = "NewApplication";
        public const string MODIFICATION = "Modification";
        public const string STANDARD_REVIEW = "StandardReview";
        public const string MEMBER_CHANGE_REVIEW = "MemberChangeReview";
        public const string HOUSEHOLD_DEATH = "HouseholdDeath";
        public const string STOP_ARCHIVE = "StopArchive";
    }

    /// <summary>
    /// 土地用途
    /// </summary>
    public static class LandUsage
    {
        public const string SELF_FARM = "自行种植";
        public const string SUBLEASE = "转包他人";
        public const string CONTRACT = "承包土地";

        public static readonly string[] All = { SELF_FARM, SUBLEASE, CONTRACT };
    }

    /// <summary>
    /// 补贴类型
    /// </summary>
    public static class SubsidyType
    {
        public const string LAND_FERTILITY = "地力补贴";
        public const string SOYBEAN = "大豆补贴";
        public const string CORN = "玉米补贴";
        public const string SURFACE_WATER_RICE = "地表水水稻";
        public const string GROUND_WATER_RICE = "地下水水稻";
        public const string ROTATION = "轮作补贴";

        public static readonly string[] All = { LAND_FERTILITY, SOYBEAN, CORN, SURFACE_WATER_RICE, GROUND_WATER_RICE, ROTATION };
    }

    /// <summary>
    /// 土地状况
    /// </summary>
    public static class LandStatus
    {
        public const string LIVING_ENTITLED = "LivingEntitled";
        public const string DECEASED_INHERITANCE = "DeceasedInheritance";
        public const string NO_LAND_RIGHT = "NoLandRight";
        public const string EXTERNAL_LAND = "ExternalLand";

        public static readonly string[] All = { LIVING_ENTITLED, DECEASED_INHERITANCE, NO_LAND_RIGHT, EXTERNAL_LAND };
    }

    /// <summary>
    /// 性别
    /// </summary>
    public static class Gender
    {
        public const string MALE = "男";
        public const string FEMALE = "女";

        public static readonly string[] All = { MALE, FEMALE };
    }
}
