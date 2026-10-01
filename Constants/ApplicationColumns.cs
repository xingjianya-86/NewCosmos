namespace NewCosmos.Constants;

/// <summary>
/// nc_biz_applications 显式列清单（宽表禁用 SELECT *，见 AGENTS.md §14.2）。
/// 取值 = 实体映射属性与实际表列的交集（2026-10-01 对 pg_dump 权威 schema 校验，剔除12 个无表列的幻影属性）。
/// 与 Models.Entities.Application 映射属性增删时同步维护。
/// </summary>
public static class ApplicationColumns
{
    public const string Full =
        "id, application_no, applicant_name, applicant_id_card, applicant_phone, hukou_type, gender, ethnicity, " +
        "marital_status, education_level, political_status, hukou_address, hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, " +
        "disability_card_no, province, city_id, county_id, town_id, village_id, city, district, " +
        "town, community, address, bank_name, bank_account, physical_condition, disease_name, secondary_disease_name, " +
        "disease_code, is_severe_disease, disability_type, disability_level, health_status, employment_status, work_unit, income_source, " +
        "is_single_rescue, support_mode, application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id, family_size, " +
        "confirmed_family_size, work_income_total, business_income_total, property_income_total, transfer_income_total, other_income_total, total_family_income, per_capita_income, " +
        "rigid_expenditure, alimony_income, total_annual_income, per_capita_annual_income, is_eligible, classification_result, classified_subsidy_type, classified_subsidy_amount, " +
        "household_monthly_guarantee_amount, person_category_protection_total_amount, caregiver_subsidy_amount, total_guarantee_amount, stop_reason, stop_date, stop_remark, is_special_approval, " +
        "special_approval_id, status, current_step, submit_at, submit_by, created_at, updated_at, created_by, " +
        "updated_by, deleted_at, original_application_id, source_type, chain_type, source_table, data_completed_at, first_approved_at, " +
        "family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area, land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count";
}
