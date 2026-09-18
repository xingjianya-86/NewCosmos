namespace NewCosmos.Constants;

public static class ImportTypeCodes
{
    public const string RURAL_SUBSISTENCE = "RURAL_SUBSISTENCE";
    public const string URBAN_SUBSISTENCE = "URBAN_SUBSISTENCE";
    public const string LOW_INCOME_EDGE = "LOW_INCOME_EDGE";
    public const string RIGID_EXPENDITURE = "RIGID_EXPENDITURE";
    public const string DESTITUTE = "DESTITUTE";

    public const string ELDERLY_SUBSIDY = "ELDERLY_SUBSIDY";
    public const string AGRICULTURAL_SUBSIDY = "AGRICULTURAL_SUBSIDY";
    public const string PLANTING_SUBSIDY = "PLANTING_SUBSIDY";
    public const string LAND_CONTRACT = "LAND_CONTRACT";
    public const string SOYBEAN_SUBSIDY = "SOYBEAN_SUBSIDY";
    public const string ROTATION_SUBSIDY = "ROTATION_SUBSIDY";

    public static readonly string[] All =
    {
        RURAL_SUBSISTENCE,
        URBAN_SUBSISTENCE,
        LOW_INCOME_EDGE,
        RIGID_EXPENDITURE,
        DESTITUTE,
        ELDERLY_SUBSIDY,
        AGRICULTURAL_SUBSIDY,
        PLANTING_SUBSIDY,
        LAND_CONTRACT,
        SOYBEAN_SUBSIDY,
        ROTATION_SUBSIDY
    };

    public static readonly string[] Implemented =
    {
        RURAL_SUBSISTENCE,
        URBAN_SUBSISTENCE,
        LOW_INCOME_EDGE,
        RIGID_EXPENDITURE,
        DESTITUTE,
        ELDERLY_SUBSIDY,
        AGRICULTURAL_SUBSIDY,
        PLANTING_SUBSIDY,
        LAND_CONTRACT,
        SOYBEAN_SUBSIDY,
        ROTATION_SUBSIDY
    };

    public static string GetDisplayName(string code) => code switch
    {
        RURAL_SUBSISTENCE => "农村低保对象导入",
        URBAN_SUBSISTENCE => "城市低保对象导入",
        LOW_INCOME_EDGE => "最低生活保障边缘家庭导入",
        RIGID_EXPENDITURE => "刚性支出困难家庭导入",
        DESTITUTE => "特困人员导入",
        ELDERLY_SUBSIDY => "高龄补贴导入",
        AGRICULTURAL_SUBSIDY => "农业补贴导入",
        PLANTING_SUBSIDY => "种植补贴导入",
        LAND_CONTRACT => "土地承包导入",
        SOYBEAN_SUBSIDY => "大豆补贴导入",
        ROTATION_SUBSIDY => "轮作补贴导入",
        _ => code
    };

    public static bool IsCombinedType(string code) => code switch
    {
        RURAL_SUBSISTENCE => true,
        URBAN_SUBSISTENCE => true,
        LOW_INCOME_EDGE => true,
        RIGID_EXPENDITURE => true,
        DESTITUTE => true,
        _ => false
    };

    public static readonly string[] SingleFileTypes =
    {
        ELDERLY_SUBSIDY,
        AGRICULTURAL_SUBSIDY,
        PLANTING_SUBSIDY,
        LAND_CONTRACT,
        SOYBEAN_SUBSIDY,
        ROTATION_SUBSIDY
    };

    public static bool IsSingleFileType(string code) => code switch
    {
        ELDERLY_SUBSIDY => true,
        AGRICULTURAL_SUBSIDY => true,
        PLANTING_SUBSIDY => true,
        LAND_CONTRACT => true,
        SOYBEAN_SUBSIDY => true,
        ROTATION_SUBSIDY => true,
        _ => false
    };

    public static (string FamilyPattern, string PersonPattern) GetFilePatterns(string code) => code switch
    {
        RURAL_SUBSISTENCE => ("农村低保家庭查询_*.xlsx", "农村低保人员查询_*.xlsx"),
        URBAN_SUBSISTENCE => ("城市低保家庭查询_*.xlsx", "城市低保人员查询_*.xlsx"),
        LOW_INCOME_EDGE => ("低保边缘家庭查询_*.xlsx", "低保边缘人员查询_*.xlsx"),
        RIGID_EXPENDITURE => ("刚性支出家庭查询_*.xlsx", "刚性支出人员查询_*.xlsx"),
        DESTITUTE => ("特困人员家庭查询_*.xlsx", "特困人员人员查询_*.xlsx"),
        _ => (string.Empty, string.Empty)
    };

    public static (string FamilyTable, string PersonTable) GetTableNames(string code) => code switch
    {
        RURAL_SUBSISTENCE => ("nc_biz_rural_subsistence_families", "nc_biz_rural_subsistence_persons"),
        URBAN_SUBSISTENCE => ("nc_biz_urban_subsistence_families", "nc_biz_urban_subsistence_persons"),
        LOW_INCOME_EDGE => ("nc_biz_low_income_edge_families", "nc_biz_low_income_edge_persons"),
        RIGID_EXPENDITURE => ("nc_biz_rigid_expenditure_families", "nc_biz_rigid_expenditure_persons"),
        DESTITUTE => ("nc_biz_destitute_families", "nc_biz_destitute_persons"),
        _ => (string.Empty, string.Empty)
    };
}
