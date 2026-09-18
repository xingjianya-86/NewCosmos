namespace NewCosmos.Helpers;

public static class PageDefaultValues
{
    public static readonly string[] GenderOptions = { "男", "女" };
    public static readonly string[] GenderCodes = { "Male", "Female" };

    public static readonly string[] EducationOptions = { "文盲/半文盲", "小学", "初中", "高中/中专", "大专", "本科", "研究生及以上" };
    public static readonly string[] EducationCodes = { "Illiterate", "Primary", "JuniorHigh", "SeniorHigh", "College", "Bachelor", "Postgraduate" };

    public static readonly string[] HealthOptions = { "健康或良好", "一般或较弱", "重病", "重残", "重病且重残", "其他" };
    public static readonly string[] HealthCodes = { "Healthy", "Weak", "SevereIllness", "SevereDisability", "SevereIllnessAndDisability", "Other" };

    public static readonly string[] WorkCapacityOptions = { "有劳动能力", "部分丧失劳动能力", "完全丧失劳动能力" };
    public static readonly string[] WorkCapacityCodes = { "Full", "partial", "None" };

    public static readonly string[] HukouTypeOptions = { "农村户口", "城市户口" };
    public static readonly string[] HukouTypeCodes = { "Rural", "Urban" };

    public static readonly string[] CaregiverTypeOptions = { "无", "亲属", "机构" };
    public static readonly string[] CaregiverTypeCodes = { "None", "Family", "Institution" };

    public static string GetCode(string[] options, string[] codes, string displayValue)
    {
        var index = System.Array.IndexOf(options, displayValue);
        return index >= 0 && index < codes.Length ? codes[index] : displayValue;
    }

    public static string GetDisplay(string[] options, string[] codes, string code)
    {
        var index = System.Array.IndexOf(codes, code);
        return index >= 0 && index < options.Length ? options[index] : code;
    }
}
