namespace NewCosmos.Constants;

public static class DisabilityConstants
{
    public const string DISABILITY_TYPES_CATEGORY = "DisabilityTypes";
    public const string DISABILITY_LEVELS_CATEGORY = "DisabilityLevels";

    // ── 残疾类型 key（与 nc_dict_items category=DisabilityTypes 的 item_key 一致） ──
    public const string TYPE_VISION = "Vision";
    public const string TYPE_HEARING = "Hearing";
    public const string TYPE_SPEECH = "Speech";
    public const string TYPE_PHYSICAL = "Physical";
    public const string TYPE_INTELLECTUAL = "Intellectual";
    public const string TYPE_MENTAL = "Mental";
    public const string TYPE_MULTIPLE = "Multiple";

    // ── 残疾等级 key（与 nc_dict_items category=DisabilityLevels 的 item_key 一致） ──
    public const string LEVEL1 = "Level1";
    public const string LEVEL2 = "Level2";
    public const string LEVEL3 = "Level3";
    public const string LEVEL4 = "Level4";

    /// <summary>三级智力、三级精神——认定与分类施保按重度残疾口径处理</summary>
    public static bool IsIntellectualOrMental(string? typeKey) =>
        typeKey is TYPE_INTELLECTUAL or TYPE_MENTAL;
}
