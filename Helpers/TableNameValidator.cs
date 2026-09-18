namespace NewCosmos.Helpers;

public static class TableNameValidator
{
    private static readonly HashSet<string> AllowedTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nc_biz_applications",
        "nc_biz_family_members",
        "nc_biz_low_income_families",
        "nc_biz_low_income_persons",
        "nc_biz_low_income_archives",
        "nc_biz_rural_subsistence_families",
        "nc_biz_rural_subsistence_persons",
        "nc_biz_urban_subsistence_families",
        "nc_biz_urban_subsistence_persons",
        "nc_biz_destitute_families",
        "nc_biz_destitute_persons",
        "nc_biz_rigid_expenditure_families",
        "nc_biz_rigid_expenditure_persons",
        "nc_biz_low_income_edge_families",
        "nc_biz_low_income_edge_persons",
        "nc_biz_labor_incomes",
        "nc_biz_business_incomes",
        "nc_biz_property_incomes",
        "nc_biz_transfer_incomes",
        "nc_biz_other_incomes",
        "nc_biz_alimony_incomes",
        "nc_biz_land_incomes",
        "nc_biz_subsidy_incomes",
        "nc_biz_rigid_expenditures",
        "nc_biz_properties",
        "nc_biz_vehicles",
        "nc_biz_machineries",
        "nc_biz_financial_assets",
        "nc_biz_land_confirmations",
        "nc_biz_land_confirmation_records",
        "nc_biz_land_confirmation_persons",
        "nc_biz_forest_lands",
        "nc_biz_breeding_incomes",
        "nc_biz_employment_costs",
        "nc_biz_caregivers",
        "nc_biz_guardians",
        "nc_biz_self_care_assessments",
        "nc_biz_kinship_filings",
        "nc_biz_change_records",
        "nc_biz_change_snapshots",
        "nc_biz_change_details",
        "nc_biz_household_surveys",
        "nc_biz_special_approvals",
        "nc_biz_archives",
        "nc_biz_archive_files",
        "nc_biz_print_records",
        "nc_biz_templates",
        "nc_biz_template_versions",
        "nc_biz_template_modules",
        "nc_biz_field_groups",
        "nc_biz_template_field_definitions",
        "nc_biz_template_module_codes",
        "nc_biz_land_contract_contractor",
        "nc_biz_land_contract_plot",
        "nc_biz_elderly_subsidy_history",
        "nc_biz_planting_subsidy",
        "nc_biz_rotation_subsidy",
        "nc_biz_soil_subsidy",
        "nc_biz_high_oil_soybean_person",
        "nc_biz_high_oil_soybean_detail",
        "nc_biz_high_protein_soybean_person",
        "nc_biz_high_protein_soybean_detail",
        "nc_biz_asset_checks",
        "nc_biz_asset_check_agents",
        "nc_biz_asset_check_operators",
        "nc_biz_asset_verifications",

        "nc_sys_users",
        "nc_sys_roles",
        "nc_sys_organizations",
        "nc_sys_permissions",
        "nc_sys_user_roles",
        "nc_perm_role_permissions",
        "nc_perm_user_permissions",
        "nc_perm_cache_version",

        "nc_regions_cities",
        "nc_regions_counties",
        "nc_regions_towns",
        "nc_regions_villages",

        "nc_config_standards",
        "nc_config_income_standards",
        "nc_config_subsidy_standards",
        "nc_config_destitute_standards",
        "nc_config_subsidy_prices",
        "nc_config_system_standards",

        "nc_dict_categories",
        "nc_dict_items",
        "nc_dict_categories_updates",
        "nc_dict_items_updates",
    };

    private static readonly string[] AllowedPrefixes =
    [
        "nc_biz_",
        "nc_sys_",
        "nc_perm_",
        "nc_regions_",
        "nc_config_",
        "nc_dict_",
    ];

    public static bool IsValid(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return false;

        if (AllowedTableNames.Contains(tableName))
            return true;

        foreach (var prefix in AllowedPrefixes)
        {
            if (tableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static void ValidateOrThrow(string tableName)
    {
        if (!IsValid(tableName))
            throw new InvalidOperationException($"无效的表名: {tableName}");
    }
}
