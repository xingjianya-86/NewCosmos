namespace NewCosmos.Constants;

public static class AssetTypeConstants
{
    public static class Codes
    {
        public const string REAL_ESTATE = "RealEstate";
        public const string VEHICLE = "Vehicle";
        public const string DEPOSIT = "Deposit";
        public const string SECURITIES = "Securities";
        public const string INSURANCE = "Insurance";
        public const string OTHER = "Other";
    }

    public static string GetDescription(string code) =>
        Helpers.DictDisplayHelper.GetAssetTypeDisplay(code);
}
