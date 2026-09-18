namespace NewCosmos.Constants;

public static class AssistanceTypeConstants
{
    public static class Codes
    {
        public const string DESTITUTE_SUPPORT = "DestituteSupport";
        public const string SUBSISTENCE = "Subsistence";
        public const string MEDICAL = "Medical";
        public const string EDUCATION = "Education";
        public const string HOUSING = "Housing";
        public const string EMPLOYMENT = "Employment";
        public const string DISASTER = "Disaster";
        public const string TEMPORARY = "Temporary";
        public const string EMERGENCY_MEDICAL = "EmergencyMedical";
        public const string HOMELESS = "Homeless";
    }

    public static readonly string[] BasicLivingTypes = { Codes.DESTITUTE_SUPPORT, Codes.SUBSISTENCE };
    public static readonly string[] SpecializedTypes = { Codes.MEDICAL, Codes.EDUCATION, Codes.HOUSING, Codes.EMPLOYMENT, Codes.DISASTER };
    public static readonly string[] EmergencyTypes = { Codes.TEMPORARY, Codes.EMERGENCY_MEDICAL, Codes.HOMELESS };

    public static string GetDescription(string code) =>
        Helpers.DictDisplayHelper.GetAssistanceTypeDisplay(code);

    public static string GetCategory(string code)
    {
        if (System.Array.IndexOf(BasicLivingTypes, code) >= 0) return AssistanceCategoryConstants.Codes.BASIC_LIVING;
        if (System.Array.IndexOf(SpecializedTypes, code) >= 0) return AssistanceCategoryConstants.Codes.SPECIALIZED;
        if (System.Array.IndexOf(EmergencyTypes, code) >= 0) return AssistanceCategoryConstants.Codes.EMERGENCY;
        return string.Empty;
    }
}
