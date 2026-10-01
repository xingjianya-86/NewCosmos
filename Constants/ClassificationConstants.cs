namespace NewCosmos.Constants;

public static class ClassificationConstants
{
    /// <summary>低收入认定倍数：低收入标准 = 低保标准 × 该倍数（唯一事实来源，政策调整只改这里）</summary>
    public const decimal LowIncomeMultiplier = 1.5m;

    /// <summary>赡养能力年龄下限（未满该年龄视为无赡养能力）</summary>
    public const int SUPPORT_ABILITY_MIN_AGE = 18;

    /// <summary>赡养能力年龄上限（超过该年龄视为无赡养能力）</summary>
    public const int SUPPORT_ABILITY_MAX_AGE = 70;

    /// <summary>健康状况推导的高龄阈值（达到该年龄视为健康欠佳）</summary>
    public const int ELDERLY_AGE = 60;

    // 代码表
    public const string RuralSubsistence = "RuralSubsistence";
    public const string UrbanSubsistence = "UrbanSubsistence";
    public const string RuralLowIncome = "RuralLowIncome";
    public const string UrbanLowIncome = "UrbanLowIncome";
    public const string RuralLowIncomeSingle = "RuralLowIncomeSingle";
    public const string UrbanLowIncomeSingle = "UrbanLowIncomeSingle";
    public const string RuralDestituteScattered = "RuralDestituteScattered";
    public const string UrbanDestituteScattered = "UrbanDestituteScattered";
    public const string RuralDestituteCentralized = "RuralDestituteCentralized";
    public const string UrbanDestituteCentralized = "UrbanDestituteCentralized";
    public const string RuralRigidExpenditure = "RuralRigidExpenditure";
    public const string UrbanRigidExpenditure = "UrbanRigidExpenditure";
    public const string RuralIncomeExceeded = "RuralIncomeExceeded";
    public const string UrbanIncomeExceeded = "UrbanIncomeExceeded";
    public const string IneligibleWithLabor = "IneligibleWithLabor";
    public const string Ineligible = "Ineligible";
    public const string IneligibleOther = "IneligibleOther";
    public const string AssetVerification = "AssetVerification";

    /// <summary>档案制作覆盖的全部社会救助分类码（域级模板范围用；不含停发/不符合类）</summary>
    public static readonly string[] ArchiveCategoryCodes =
    [
        RuralSubsistence, UrbanSubsistence,
        RuralLowIncome, UrbanLowIncome,
        RuralLowIncomeSingle, UrbanLowIncomeSingle,
        RuralDestituteCentralized, UrbanDestituteCentralized,
        RuralDestituteScattered, UrbanDestituteScattered,
        RuralRigidExpenditure, UrbanRigidExpenditure
    ];

    // ── 简化代码 ──
    public static class SimplifiedCodes
    {
        public const string LOW_INCOME = "LowIncome";
        public const string DESTITUTE = "Destitute";
        public const string RIGID_EXPENDITURE = "RigidExpenditure";
        public const string INCOME_EXCEEDED = "IncomeExceeded";
    }

    // ── 代码到描述的映射（官方名称：依据《社会救助暂行办法》及《黑龙江省低收入人口认定办法（暂行）》黑民规〔2024〕7号） ──
    // 2021 年民发〔2021〕57号统一低保制度城乡概念，对外名称不再区分"农村/城市"；
    // 城乡差异由 hukou_type（Rural/Urban）区分，见 HukouType 与 NormalizeCodeByHukou。
    private static readonly Dictionary<string, string> CodeToDescription = new()
    {
        [RuralSubsistence] = "最低生活保障对象",
        [UrbanSubsistence] = "最低生活保障对象",
        [RuralLowIncome] = "最低生活保障边缘家庭",
        [UrbanLowIncome] = "最低生活保障边缘家庭",
        [RuralDestituteCentralized] = "特困人员（集中供养）",
        [UrbanDestituteCentralized] = "特困人员（集中供养）",
        [RuralDestituteScattered] = "特困人员（分散供养）",
        [UrbanDestituteScattered] = "特困人员（分散供养）",
        [RuralRigidExpenditure] = "刚性支出困难家庭",
        [UrbanRigidExpenditure] = "刚性支出困难家庭",
        [RuralIncomeExceeded] = "收入超标",
        [UrbanIncomeExceeded] = "收入超标",
        [IneligibleWithLabor] = "不符合认定条件（有劳动力）",
        [Ineligible] = "不符合认定条件",
        [RuralLowIncomeSingle] = "最低生活保障对象（单人）",
        [UrbanLowIncomeSingle] = "最低生活保障对象（单人）",
        [IneligibleOther] = "不符合认定条件（其他）"
    };

    // ── 分类分组（公有数组供报表/SQL 口径复用；Hash 集由数组构造，单一事实来源） ──

    /// <summary>低保族（含单人保）</summary>
    public static readonly string[] SubsistenceCategoryCodes =
    {
        RuralSubsistence, UrbanSubsistence,
        RuralLowIncomeSingle, UrbanLowIncomeSingle
    };

    /// <summary>最低生活保障边缘家庭族</summary>
    public static readonly string[] LowIncomeCategoryCodes =
    {
        RuralLowIncome, UrbanLowIncome
    };

    /// <summary>单人保族</summary>
    public static readonly string[] SingleRescueCategoryCodes =
    {
        RuralLowIncomeSingle, UrbanLowIncomeSingle
    };

    /// <summary>特困人员族</summary>
    public static readonly string[] DestituteCategoryCodes =
    {
        RuralDestituteCentralized, UrbanDestituteCentralized,
        RuralDestituteScattered, UrbanDestituteScattered
    };

    /// <summary>刚性支出困难家庭族</summary>
    public static readonly string[] RigidExpenditureCategoryCodes =
    {
        RuralRigidExpenditure, UrbanRigidExpenditure
    };

    /// <summary>停保/不符合族（收入超标、不符合认定条件）</summary>
    public static readonly string[] StopCategoryCodes =
    {
        RuralIncomeExceeded, UrbanIncomeExceeded,
        IneligibleWithLabor, Ineligible, IneligibleOther
    };

    private static readonly HashSet<string> SubsistenceCodes = new(SubsistenceCategoryCodes);

    private static readonly HashSet<string> LowIncomeCodes = new(LowIncomeCategoryCodes);

    private static readonly HashSet<string> SingleRescueCodes = new(SingleRescueCategoryCodes);

    private static readonly HashSet<string> DestituteCodes = new(DestituteCategoryCodes);

    private static readonly HashSet<string> RigidExpenditureCodes = new(RigidExpenditureCategoryCodes);

    private static readonly HashSet<string> StopCodes = new(StopCategoryCodes);

    private static readonly HashSet<string> RuralCodes = new()
    {
        RuralSubsistence, RuralLowIncome, RuralLowIncomeSingle,
        RuralDestituteScattered, RuralDestituteCentralized,
        RuralRigidExpenditure, RuralIncomeExceeded
    };

    private static readonly HashSet<string> UrbanCodes = new()
    {
        UrbanSubsistence, UrbanLowIncome, UrbanLowIncomeSingle,
        UrbanDestituteScattered, UrbanDestituteCentralized,
        UrbanRigidExpenditure, UrbanIncomeExceeded
    };

    // ── 判定方法 ──

    public static bool IsCodeSubsistence(string code) =>
        !string.IsNullOrEmpty(code) && SubsistenceCodes.Contains(code);

    public static bool IsCodeLowIncome(string code) =>
        !string.IsNullOrEmpty(code) && (LowIncomeCodes.Contains(code) || code == SimplifiedCodes.LOW_INCOME);

    public static bool IsCodeDestitute(string code) =>
        !string.IsNullOrEmpty(code) && (DestituteCodes.Contains(code) || code == SimplifiedCodes.DESTITUTE);

    public static bool IsCodeSingleRescue(string code) =>
        !string.IsNullOrEmpty(code) && SingleRescueCodes.Contains(code);

    public static bool IsCodeRigidExpenditure(string code) =>
        !string.IsNullOrEmpty(code) && (RigidExpenditureCodes.Contains(code) || code == SimplifiedCodes.RIGID_EXPENDITURE);

    public static bool IsCodeStop(string code) =>
        !string.IsNullOrEmpty(code) && (StopCodes.Contains(code) || code == SimplifiedCodes.INCOME_EXCEEDED);

    public static bool IsCodeRural(string code) =>
        !string.IsNullOrEmpty(code) && RuralCodes.Contains(code);

    public static bool IsCodeUrban(string code) =>
        !string.IsNullOrEmpty(code) && UrbanCodes.Contains(code);

    // ── 代码转换方法 ──

    /// <summary>
    /// 代码转描述
    /// </summary>
    public static string ConvertFromCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        return CodeToDescription.TryGetValue(code, out var desc) ? desc : code;
    }

    /// <summary>
    /// 代码转全称（与 ConvertFromCode 相同：对外名称不区分城乡）
    /// </summary>
    public static string ConvertToFullName(string code)
    {
        return ConvertFromCode(code);
    }

    /// <summary>
    /// 代码转简称（审核确认表等空间有限的场景）：单人保→"单人保"，其余走全称
    /// </summary>
    public static string ConvertToShortName(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        if (IsCodeSingleRescue(code)) return "单人保";
        return ConvertFromCode(code);
    }

    /// <summary>
    /// 代码转四大类显示名（月报人员列表口径）：低保 / 低保边缘 / 特困 / 刚性支出。
    /// 不显示农村/城市前缀及集中/分散、单人等细分；其余代码原样返回。
    /// </summary>
    public static string ConvertToMajorCategoryName(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        if (IsCodeSubsistence(code)) return "低保";
        if (IsCodeLowIncome(code)) return "低保边缘";
        if (IsCodeDestitute(code)) return "特困";
        if (IsCodeRigidExpenditure(code)) return "刚性支出";
        return ConvertFromCode(code);
    }

    /// <summary>
    /// 代码转认定家庭类型名称（如：最低生活保障家庭、最低生活保障边缘家庭）
    /// </summary>
    public static string ConvertToFamilyTypeName(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;

        return code switch
        {
            RuralSubsistence or UrbanSubsistence
                or RuralLowIncomeSingle or UrbanLowIncomeSingle => "最低生活保障家庭",
            RuralLowIncome or UrbanLowIncome => "最低生活保障边缘家庭",
            RuralDestituteCentralized or UrbanDestituteCentralized
                or RuralDestituteScattered or UrbanDestituteScattered => "特困供养家庭",
            RuralRigidExpenditure or UrbanRigidExpenditure => "刚性支出困难家庭",
            _ => ConvertFromCode(code)
        };
    }

    /// <summary>
    /// 描述转代码（按户籍消歧：同一名称对应 Rural/Urban 两个代码，须传 hukouType）
    /// 历史中文名（含"农村/城市"前缀的旧值）也可识别，用于存量数据兼容。
    /// </summary>
    public static string ConvertToCode(string description, string hukouType = null)
    {
        if (string.IsNullOrEmpty(description)) return description;

        // 历史存量旧名（含城乡前缀）→ 新名
        var normalized = description switch
        {
            "农村最低生活保障对象" or "城市最低生活保障对象" or "最低生活保障对象" => "最低生活保障对象",
            "农村最低生活保障对象（单人）" or "城市最低生活保障对象（单人）" or "农村低保（单人）" or "城市低保（单人）" or "最低生活保障对象（单人）" => "最低生活保障对象（单人）",
            "农村最低生活保障边缘家庭成员" or "城市最低生活保障边缘家庭成员" or "最低生活保障边缘家庭成员" or "农村最低生活保障边缘家庭" or "城市最低生活保障边缘家庭" or "最低生活保障边缘家庭" => "最低生活保障边缘家庭",
            "农村特困人员（集中供养）" or "城市特困人员（集中供养）" or "特困人员（集中供养）" => "特困人员（集中供养）",
            "农村特困人员（分散供养）" or "城市特困人员（分散供养）" or "特困人员（分散供养）" => "特困人员（分散供养）",
            "农村刚性支出困难家庭成员" or "城市刚性支出困难家庭成员" or "刚性支出困难家庭成员" or "农村刚性支出困难家庭" or "城市刚性支出困难家庭" or "刚性支出困难家庭" => "刚性支出困难家庭",
            "农村收入超标" or "城市收入超标" or "收入超标" => "收入超标",
            _ => description
        };

        bool isRural = HukouType.IsHukouRural(hukouType);
        return normalized switch
        {
            "最低生活保障对象" => isRural ? RuralSubsistence : UrbanSubsistence,
            "最低生活保障对象（单人）" => isRural ? RuralLowIncomeSingle : UrbanLowIncomeSingle,
            "最低生活保障边缘家庭成员" or "最低生活保障边缘家庭" => isRural ? RuralLowIncome : UrbanLowIncome,
            "特困人员（集中供养）" => isRural ? RuralDestituteCentralized : UrbanDestituteCentralized,
            "特困人员（分散供养）" => isRural ? RuralDestituteScattered : UrbanDestituteScattered,
            "刚性支出困难家庭成员" or "刚性支出困难家庭" => isRural ? RuralRigidExpenditure : UrbanRigidExpenditure,
            "收入超标" => isRural ? RuralIncomeExceeded : UrbanIncomeExceeded,
            "不符合认定条件（有劳动力）" => IneligibleWithLabor,
            "不符合认定条件（其他）" => IneligibleOther,
            "不符合认定条件" => Ineligible,
            _ => description
        };
    }

    /// <summary>
    /// 完整代码按户籍归一（如 RuralSubsistence + Urban 户籍 → UrbanSubsistence）
    /// 用于统一名称场景下按 hukou_type 消歧保存代码。
    /// </summary>
    public static string NormalizeCodeByHukou(string code, string hukouType)
    {
        if (string.IsNullOrEmpty(code)) return code;

        bool isRural = HukouType.IsHukouRural(hukouType);
        bool needSwitch = isRural ? IsCodeUrban(code) : IsCodeRural(code);
        if (!needSwitch) return code;

        return code switch
        {
            RuralSubsistence => UrbanSubsistence,
            UrbanSubsistence => RuralSubsistence,
            RuralLowIncome => UrbanLowIncome,
            UrbanLowIncome => RuralLowIncome,
            RuralLowIncomeSingle => UrbanLowIncomeSingle,
            UrbanLowIncomeSingle => RuralLowIncomeSingle,
            RuralDestituteCentralized => UrbanDestituteCentralized,
            UrbanDestituteCentralized => RuralDestituteCentralized,
            RuralDestituteScattered => UrbanDestituteScattered,
            UrbanDestituteScattered => RuralDestituteScattered,
            RuralRigidExpenditure => UrbanRigidExpenditure,
            UrbanRigidExpenditure => RuralRigidExpenditure,
            RuralIncomeExceeded => UrbanIncomeExceeded,
            UrbanIncomeExceeded => RuralIncomeExceeded,
            _ => code
        };
    }

    /// <summary>
    /// 是否为简化代码
    /// </summary>
    public static bool IsSimplifiedCode(string code)
    {
        return code is SimplifiedCodes.LOW_INCOME or SimplifiedCodes.DESTITUTE 
            or SimplifiedCodes.RIGID_EXPENDITURE or SimplifiedCodes.INCOME_EXCEEDED;
    }

    /// <summary>
    /// 简化代码转完整代码
    /// </summary>
    public static string NormalizeToFullCode(string code, string hukouType = null, string supportMode = null)
    {
        if (string.IsNullOrEmpty(code)) return code;
        if (!IsSimplifiedCode(code)) return code;

        bool isRural = string.IsNullOrEmpty(hukouType) || HukouType.IsHukouRural(hukouType);
        bool isCentralized = !string.IsNullOrEmpty(supportMode) && supportMode == SupportMode.CENTRALIZED;

        return code switch
        {
            SimplifiedCodes.LOW_INCOME => isRural ? RuralLowIncome : UrbanLowIncome,
            SimplifiedCodes.DESTITUTE when isRural && isCentralized => RuralDestituteCentralized,
            SimplifiedCodes.DESTITUTE when isRural => RuralDestituteScattered,
            SimplifiedCodes.DESTITUTE when !isRural && isCentralized => UrbanDestituteCentralized,
            SimplifiedCodes.DESTITUTE => UrbanDestituteScattered,
            SimplifiedCodes.RIGID_EXPENDITURE => isRural ? RuralRigidExpenditure : UrbanRigidExpenditure,
            SimplifiedCodes.INCOME_EXCEEDED => isRural ? RuralIncomeExceeded : UrbanIncomeExceeded,
            _ => code
        };
    }

    // ── 内部类 ──

    /// <summary>
    /// 户口类型
    /// </summary>
    public static class HukouType
    {
        public const string URBAN = "Urban";
        public const string RURAL = "Rural";

        private static readonly HashSet<string> UrbanTypes = new()
        {
            URBAN, "城镇", "非农", "非农业户口"
        };
        private static readonly HashSet<string> RuralTypes = new()
        {
            RURAL, "农村", "农业", "农业户口"
        };

        public static bool IsHukouUrban(string hukouType) =>
            !string.IsNullOrEmpty(hukouType) && UrbanTypes.Contains(hukouType);

        public static bool IsHukouRural(string hukouType) =>
            !string.IsNullOrEmpty(hukouType) && RuralTypes.Contains(hukouType);

        public static string Normalize(string hukouType)
        {
            if (string.IsNullOrWhiteSpace(hukouType)) return RURAL;
            return IsHukouUrban(hukouType) ? URBAN : RURAL;
        }
    }

    /// <summary>
    /// 供养方式
    /// </summary>
    public static class SupportMode
    {
        public const string CENTRALIZED = "Centralized";
        public const string SCATTERED = "Scattered";
        public const string HOME = "Home";

        public static string GetDescription(string code)
        {
            return code switch
            {
                CENTRALIZED => "集中供养",
                SCATTERED => "分散供养",
                HOME => "居家供养",
                _ => code
            };
        }
    }

    /// <summary>
    /// 健康状况
    /// </summary>
    public static class HealthStatus
    {
        public const string SEVERE_DISABILITY = "重残";
        public const string SEVERE_DISEASE = "重病";

        public static bool HasSevereDisability(string healthStatus) =>
            !string.IsNullOrEmpty(healthStatus) && healthStatus.Contains(SEVERE_DISABILITY);

        public static bool HasSevereDisease(string healthStatus) =>
            !string.IsNullOrEmpty(healthStatus) && healthStatus.Contains(SEVERE_DISEASE);
    }

    /// <summary>
    /// 残疾等级——key 与字典 DisabilityLevels 一致（Level1..Level4）
    /// </summary>
    public static class DisabilityLevel
    {
        private static readonly HashSet<string> SevereKeys = new()
        {
            DisabilityConstants.LEVEL1, DisabilityConstants.LEVEL2
        };
        private static readonly HashSet<string> NonSevereKeys = new()
        {
            DisabilityConstants.LEVEL3, DisabilityConstants.LEVEL4
        };

        /// <summary>
        /// 残疾等级 key 是否为重度（一级或二级）
        /// </summary>
        public static bool IsSevere(string levelKey) =>
            !string.IsNullOrEmpty(levelKey) && SevereKeys.Contains(levelKey);

        /// <summary>
        /// 残疾等级 key 是否为三级
        /// </summary>
        public static bool IsLevel3(string levelKey) =>
            levelKey == DisabilityConstants.LEVEL3;

        /// <summary>
        /// 残疾等级 key 是否为明确的非重度（三级、四级）。
        /// 单人保判定用：等级明确为三/四级时，即使健康状况被标为"重残"也不计重残；
        /// 等级未知（空值）时返回 false，维持按健康状况/标记判定的现状。
        /// </summary>
        public static bool IsNonSevere(string levelKey) =>
            !string.IsNullOrEmpty(levelKey) && NonSevereKeys.Contains(levelKey);

        /// <summary>
        /// 认定口径重度残疾：一、二级任意残疾类型；三级智力、三级精神
        /// （地方单人保/特困"无劳动能力"/分类施保重残津贴口径，单点实现）
        /// </summary>
        public static bool IsSevereForAssistance(string levelKey, string? disabilityTypeKey) =>
            IsSevere(levelKey)
            || (IsLevel3(levelKey) && DisabilityConstants.IsIntellectualOrMental(disabilityTypeKey));
    }

    /// <summary>
    /// 土地状况
    /// </summary>
    public static class LandStatus
    {
        public const string LIVING_ENTITLED = "存活享有";
        public const string DECEASED_INHERITANCE = "死亡继承";
        public const string NO_LAND_RIGHT = "无土地权";
        public const string EXTERNAL_LAND = "外地有地";

        public static readonly string[] All = { LIVING_ENTITLED, DECEASED_INHERITANCE, NO_LAND_RIGHT, EXTERNAL_LAND };
    }

    /// <summary>
    /// 健康状况扩展
    /// </summary>
    public static class HealthStatusExtended
    {
        public const string HEALTHY = "Healthy";
        public const string WEAK = "Weak";
        public const string SEVERE_DISEASE_AND_DISABILITY = "SevereIllnessAndDisability";

        /// <summary>
        /// 根据健康状况推断劳动能力
        /// </summary>
        public static string InferLaborAbility(string healthStatus)
        {
            return healthStatus switch
            {
                HEALTHY => DictionaryConstants.LaborAbility.HAS_ABILITY,
                WEAK => DictionaryConstants.LaborAbility.PARTIAL_ABILITY,
                HealthStatus.SEVERE_DISEASE => DictionaryConstants.LaborAbility.NO_ABILITY,
                HealthStatus.SEVERE_DISABILITY => DictionaryConstants.LaborAbility.NO_ABILITY,
                SEVERE_DISEASE_AND_DISABILITY => DictionaryConstants.LaborAbility.NO_ABILITY,
                _ => string.Empty
            };
        }
    }
}
