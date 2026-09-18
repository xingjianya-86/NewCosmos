namespace NewCosmos.Constants;

public static class AssistanceCategoryConstants
{
    public static class Codes
    {
        public const string BASIC_LIVING = "BasicLiving";
        public const string SPECIALIZED = "Specialized";
        public const string EMERGENCY = "Emergency";
    }

    public static string GetDescription(string code) =>
        Helpers.DictDisplayHelper.GetAssistanceCategoryDisplay(code);
}
