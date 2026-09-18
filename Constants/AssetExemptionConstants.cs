namespace NewCosmos.Constants;

public static class AssetExemptionConstants
{
    public static class Codes
    {
        public const string NONE = "None";
        public const string MEDICAL_VEHICLE = "MedicalVehicle";
        public const string REHAB_VEHICLE = "RehabVehicle";
        public const string LIVELIHOOD_VEHICLE = "LivelihoodVehicle";
        public const string ASSISTANCE_DEPOSIT = "AssistanceDeposit";
        public const string HOUSING_SALE_DEPOSIT = "HousingSaleDeposit";
    }

    public static bool IsExempted(string code) => code != Codes.NONE;

    public static string GetDescription(string code) =>
        Helpers.DictDisplayHelper.GetAssetExemptionDisplay(code);
}
