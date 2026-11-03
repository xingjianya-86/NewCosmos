namespace NewCosmos.Constants;

/// <summary>
/// 宽表（>=16 列）显式列清单——AGENTS 5-2「宽表禁用 SELECT *」的单点收口（批次6 治理）。
/// 取值 = 生产库 pg_attribute 全列快照（2026-11-01 按列序生成）；Npgsql 映射忽略实体未声明的多余列，
/// 表列全集比手抄交集更安全（不会因实体漏属性而查不到列）。表增列时同步更新。
/// 窄表（列数 &lt;=15）维持现状，已在 deploy\check_standards.ps1 的 NarrowTables 白名单豁免。
/// </summary>
public static class WideTableColumns
{
    /// <summary>nc_biz_business_incomes 全列（按列序）</summary>
    public const string BusinessIncomesColumns =
        "id, application_id, company_name, vendor_type, registered_capital, business_scope," +
        "operating_income, operating_cost, net_income, address, created_at, deleted_at," +
        "member_id, member_name, member_id_card, member_age, monthly_income";

    /// <summary>nc_biz_capability_assessments 全列（按列序）</summary>
    public const string CapabilityAssessmentsColumns =
        "id, application_id, assessment_date, assessor_name, eating, dressing," +
        "getting_in_out_bed, using_toilet, indoor_walking, bathing, completed_items," +
        "self_care_level, care_level, remark, created_at, updated_at, deleted_at";

    /// <summary>nc_biz_caregivers 全列（按列序）</summary>
    public const string CaregiversColumns =
        "id, application_id, cared_member_id, name, id_card, phone, relationship, gender, age," +
        "ethnicity, health_status, work_unit, position, address, created_at, deleted_at," +
        "marital_status, hukou_type, education_level, political_status, employment_status," +
        "main_income_source, bank_name, bank_account";

    /// <summary>nc_biz_elderly_applications 全列（按列序）</summary>
    public const string ElderlyApplicationsColumns =
        "id, application_no, name, id_card, gender, birth_date, phone, hukou_address," +
        "hukou_village, family_address, bank_name, bank_account, agent_name, agent_relation," +
        "category, identity_flag, identity_source, is_category_manual, payback_start_month," +
        "payback_end_month, auto_start_month, auto_end_month, payback_months, payback_amount," +
        "is_special_case, special_reason, status, apply_date, confirmed_at, confirmed_by," +
        "stopped_at, stopped_by, stop_reason, created_at, updated_at, created_by, updated_by," +
        "deleted_at, agent_receive_name, agent_receive_bank_name, agent_receive_bank_account," +
        "agent_receive_reason, issue_start_month, issue_amount, due_stop_date," +
        "actual_stop_date, is_recover, recover_start_month, recover_end_month, recover_amount," +
        "remark, hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id," +
        "hukou_detail_address, family_city_id, family_county_id, family_town_id," +
        "family_village_id, detail_address, payback_reason, source_type, source_id, death_date," +
        "agent_receive_relation";

    /// <summary>nc_biz_elderly_reviews 全列（按列序）</summary>
    public const string ElderlyReviewsColumns =
        "id, review_no, id_card, name, status, trigger_source, trigger_ref, trigger_reason," +
        "source_history_id, old_application_id, old_application_no, old_category," +
        "old_monthly_amount, new_application_id, new_application_no, new_category," +
        "new_monthly_amount, identity_flag, age_at_review, effective_month, issue_start_month," +
        "review_result, adjust_reason_code, review_opinion, reviewed_by, reviewed_at," +
        "created_at, updated_at, deleted_at";

    /// <summary>nc_biz_family_members 全列（按列序）</summary>
    public const string FamilyMembersColumns =
        "id, application_id, name, id_card, is_applicant, gender, birth_date, age, ethnicity," +
        "phone, hukou_type, hukou_address, marital_status, education_level, political_status," +
        "relationship_to_head, health_status, work_capacity, is_disabled, disability_type," +
        "disability_level, is_severe_disability, employment_status, annual_income, created_at," +
        "updated_at, deleted_at, member_category, disability_certificate_no, disease_category," +
        "disease_name, home_province, home_city, home_district, home_town, home_address," +
        "hukou_province, hukou_city, hukou_district, hukou_town, home_village," +
        "secondary_disease, person_type, annual_support_fee, is_support_ability," +
        "monthly_income_capacity, family_size, work_unit, monthly_support_fee, support_months," +
        "main_income_source, disease_code, is_severe_disease, is_labor_exempt";

    /// <summary>nc_biz_labor_incomes 全列（按列序）</summary>
    public const string LaborIncomesColumns =
        "id, application_id, member_id, income_sub_type, work_unit, work_type, work_place," +
        "monthly_income, months_worked, annual_income, remark, created_at, deleted_at," +
        "member_name, member_id_card, member_age";

    /// <summary>nc_biz_monthly_reports 全列（按列序）</summary>
    public const string MonthlyReportsColumns =
        "id, year, month, town, report_type, status, total_archives, new_archives," +
        "stopped_archives, total_amount, new_amount, rural_subsistence_count," +
        "urban_subsistence_count, rural_low_income_count, urban_low_income_count," +
        "rural_destitute_count, urban_destitute_count, rigid_expenditure_count, generated_by," +
        "generated_at, created_at";

    /// <summary>nc_biz_near_relative_links 全列（按列序）</summary>
    public const string NearRelativeLinksColumns =
        "id, staff_id, application_id, relation, name, id_card, gender, birth_date," +
        "family_address, hukou_address, residence_address, family_size, help_type," +
        "month_amount, report_amount, start_time, family_relation, apply_reason, apply_time," +
        "family_difficulty, economy_investigate, town_opinion, dynamic_record, sort_order," +
        "created_at, updated_at, deleted_at";

    /// <summary>nc_biz_special_approval_forms 全列（按列序）</summary>
    public const string SpecialApprovalFormsColumns =
        "id, application_id, form_no, basic_situation, special_matters, template_key," +
        "report_unit, handler_name, form_date, audit_result, audit_date, status, created_by," +
        "created_at, updated_at, deleted_at";

    /// <summary>nc_biz_special_approvals 全列（按列序）</summary>
    public const string SpecialApprovalsColumns =
        "id, application_id, meeting_date, meeting_location, meeting_topic, meeting_summary," +
        "meeting_members, special_reason, special_circumstances, override_classification," +
        "approver_name, approver_title, approver_organization, attachment_file, created_by," +
        "created_at, updated_at, deleted_at";

    /// <summary>nc_biz_temp_relief_applications 全列（按列序）</summary>
    public const string TempReliefApplicationsColumns =
        "id, application_no, relief_type, apply_year, source_type, source_table," +
        "source_family_id, applicant_name, applicant_id_card, gender, age, phone," +
        "family_address, hukou_address, town, village, family_size, family_category," +
        "policy_enjoyed, family_member_status, difficulty_reason, apply_date, report_unit," +
        "report_time, small_amount_level, confirm_amount, publicize_start_date," +
        "publicize_end_date, verify_result, acceptance_person, status, confirmed_at," +
        "confirmed_by, stopped_at, stopped_by, stop_reason, created_at, updated_at, created_by," +
        "updated_by, deleted_at, difficulty_type, family_province, family_city," +
        "family_district, family_town, family_detail, hukou_province, hukou_city," +
        "hukou_district, hukou_town, beneficiary_name, beneficiary_id_card, beneficiary_gender," +
        "beneficiary_age, beneficiary_relation, hukou_type, bank_name, bank_account";
}
