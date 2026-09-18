using NewCosmos.Constants;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Import.RuralSubsistence;

public class RuralSubsistenceImportService : BaseCombinedImportService
{
    public override string ImportTypeName => ImportTypeCodes.RURAL_SUBSISTENCE;
    public override string FamilyFilePattern => "农村低保家庭查询_*.xlsx";
    public override string PersonFilePattern => "农村低保人员查询_*.xlsx";
    protected override string FamilyTableName => "nc_biz_rural_subsistence_families";
    protected override string PersonTableName => "nc_biz_rural_subsistence_persons";

    protected override Dictionary<string, string[]> FamilyColumnAliases => new()
    {
        ["applicant_name"] = new[] { "户主姓名" },
        ["applicant_id_card"] = new[] { "户主身份证号" },
        ["family_size"] = new[] { "家庭人口", "家庭人口数" },
        ["guarantee_size"] = new[] { "保障人口", "保障人口数" },
        ["annual_income"] = new[] { "家庭年总收入（元）" },
        ["per_capita_income"] = new[] { "家庭年均收入（元）" },
        ["monthly_guarantee_amount"] = new[] { "户月保障金额（元）" },
        ["family_classified_amount"] = new[] { "家庭分类施保总额（元）" },
        ["person_classified_amount"] = new[] { "人员分类施保总额（元）" },
        ["total_amount"] = new[] { "合计（元）" },
        ["phone"] = new[] { "联系电话" },
        ["address"] = new[] { "家庭住址" },
        ["province"] = new[] { "所属省", "所属省级" },
        ["city"] = new[] { "所属市", "所属市级" },
        ["district"] = new[] { "所属区", "所属区县" },
        ["street"] = new[] { "所属街", "所属街道" },
        ["community"] = new[] { "所属社", "所属社区" },
        ["certificate_number"] = new[] { "低保（特困）证号" },
        ["first_receive_month"] = new[] { "最初享受月", "最初享受月份" },
        ["apply_reason"] = new[] { "申请原因" },
        ["calculation_method"] = new[] { "救助金计算方式" },
        ["grade_type"] = new[] { "分档类型" },
        ["bank_name"] = new[] { "开户银行" },
        ["bank_account_name"] = new[] { "银行账户名称" },
        ["bank_account"] = new[] { "银行帐号" },
        ["one_card_account"] = new[] { "一卡通账号" },
        ["housing_count"] = new[] { "拥有产权住房套数" },
        ["housing_area"] = new[] { "住房总面积（平方米）" },
        ["financial_assets"] = new[] { "金融资产总额（元）" },
        ["other_property"] = new[] { "其他家庭财产情况" },
        ["member_names"] = new[] { "家庭成员姓名" },
        ["member_id_cards"] = new[] { "家庭成员身份证号" }
    };

    protected override Dictionary<string, string[]> PersonColumnAliases => new()
    {
        ["name"] = new[] { "姓名" },
        ["id_card"] = new[] { "身份证号" },
        ["head_id_card"] = new[] { "户主身份证号" },
        ["relationship"] = new[] { "与户主关系" },
        ["gender"] = new[] { "性别" },
        ["birth_date"] = new[] { "出生日期" },
        ["age"] = new[] { "年龄" },
        ["ethnicity"] = new[] { "民族" },
        ["marital_status"] = new[] { "婚姻状况" },
        ["education_level"] = new[] { "文化程度" },
        ["political_status"] = new[] { "政治面貌" },
        ["employment_status"] = new[] { "就业状况" },
        ["annual_income"] = new[] { "年收入（元）" },
        ["is_disabled"] = new[] { "是否残疾", "是否残疾人" },
        ["disability_certificate"] = new[] { "残疾证号" },
        ["disability_type"] = new[] { "残疾类别" },
        ["disability_level"] = new[] { "残疾等级" },
        ["disease_type"] = new[] { "患病病种" },
        ["work_capacity"] = new[] { "劳动能力" },
        ["health_status"] = new[] { "健康状况" },
        ["insurance_type"] = new[] { "参保类型" },
        ["first_receive_month"] = new[] { "最初享受月", "最初享受月份" },
        ["is_poverty_household"] = new[] { "是否建档立卡扶贫对象" },
        ["bank_name"] = new[] { "开户银行" },
        ["bank_account_name"] = new[] { "银行账户名称" },
        ["bank_account"] = new[] { "银行帐号" },
        ["one_card_account"] = new[] { "一卡通账号", "一卡通账户" },
        ["assistance_type"] = new[] { "救助类型" },
        ["monthly_guarantee_amount"] = new[] { "户月保障金额（元）" },
        ["family_classified_amount"] = new[] { "家庭分类施保金额（元）" },
        ["total_classified_amount"] = new[] { "合计分类施保金额（元）" },
        ["calculation_method"] = new[] { "救助金计算方式" },
        ["grade_type"] = new[] { "分档类型" },
        ["total_amount"] = new[] { "合计（元）" },
        ["school_name"] = new[] { "在读学校名称" },
        ["school_nature"] = new[] { "在读学校性质" },
        ["enrollment_date"] = new[] { "在读学校入学时间" },
        ["employer_name"] = new[] { "在职单位名称" },
        ["employer_nature"] = new[] { "在职单位性质" },
        ["employer_location"] = new[] { "在职单位地点" },
        ["remark"] = new[] { "备注" },
        ["province"] = new[] { "所属省", "所属省级" },
        ["city"] = new[] { "所属市", "所属市级" },
        ["district"] = new[] { "所属区", "所属区县" },
        ["street"] = new[] { "所属街", "所属街道" },
        ["community"] = new[] { "所属社", "所属社区" },
        ["family_address"] = new[] { "家庭地址" },
        ["certificate_number"] = new[] { "低保（特困）证号" }
    };

    private static readonly ImportColumnSpec[] FamilyColumnSpecs =
    {
        new("applicant_name", ImportValueKind.String),
        new("applicant_id_card", ImportValueKind.String),
        new("family_size", ImportValueKind.Int, DefaultInt: 1),
        new("guarantee_size", ImportValueKind.Int),
        new("annual_income", ImportValueKind.Decimal),
        new("per_capita_income", ImportValueKind.Decimal),
        new("monthly_guarantee_amount", ImportValueKind.Decimal),
        new("family_classified_amount", ImportValueKind.Decimal),
        new("person_classified_amount", ImportValueKind.Decimal),
        new("total_amount", ImportValueKind.Decimal),
        new("phone", ImportValueKind.String),
        new("address", ImportValueKind.String),
        new("province", ImportValueKind.String),
        new("city", ImportValueKind.String),
        new("district", ImportValueKind.String),
        new("street", ImportValueKind.String),
        new("community", ImportValueKind.String),
        new("certificate_number", ImportValueKind.String),
        new("first_receive_month", ImportValueKind.String),
        new("apply_reason", ImportValueKind.String),
        new("calculation_method", ImportValueKind.String),
        new("grade_type", ImportValueKind.String),
        new("bank_name", ImportValueKind.String),
        new("bank_account_name", ImportValueKind.String),
        new("bank_account", ImportValueKind.String),
        new("one_card_account", ImportValueKind.String),
        new("housing_count", ImportValueKind.Int),
        new("housing_area", ImportValueKind.Decimal),
        new("financial_assets", ImportValueKind.Decimal),
        new("other_property", ImportValueKind.String),
        new("member_names", ImportValueKind.String),
        new("member_id_cards", ImportValueKind.String),
    };

    private static readonly ImportColumnSpec[] PersonColumnSpecs =
    {
        new("head_id_card", ImportValueKind.HeadIdCard),
        new("name", ImportValueKind.String),
        new("id_card", ImportValueKind.String),
        new("relationship", ImportValueKind.String, DictCategory: "FamilyRelationships"),
        new("gender", ImportValueKind.GenderFromIdCard),
        new("birth_date", ImportValueKind.BirthDateFromIdCard),
        new("age", ImportValueKind.Int),
        new("ethnicity", ImportValueKind.String, DictCategory: "Ethnicities"),
        new("marital_status", ImportValueKind.String, DictCategory: "MaritalStatuses"),
        new("education_level", ImportValueKind.String, DictCategory: "EducationLevels"),
        new("political_status", ImportValueKind.String, DictCategory: "PoliticalStatuses"),
        new("employment_status", ImportValueKind.String, DictCategory: "EmploymentStatuses"),
        new("annual_income", ImportValueKind.Decimal),
        new("is_disabled", ImportValueKind.Bool),
        new("disability_certificate", ImportValueKind.String),
        new("disability_type", ImportValueKind.String, DictCategory: "DisabilityTypes"),
        new("disability_level", ImportValueKind.String, DictCategory: "DisabilityLevels"),
        new("disease_type", ImportValueKind.String),
        new("work_capacity", ImportValueKind.String, DictCategory: "LaborAbilities"),
        new("health_status", ImportValueKind.String, DictCategory: "HealthStatuses"),
        new("insurance_type", ImportValueKind.String),
        new("first_receive_month", ImportValueKind.String),
        new("is_poverty_household", ImportValueKind.Bool),
        new("bank_name", ImportValueKind.String),
        new("bank_account_name", ImportValueKind.String),
        new("bank_account", ImportValueKind.String),
        new("one_card_account", ImportValueKind.String),
        new("assistance_type", ImportValueKind.String, DictCategory: "AssistanceTypes"),
        new("monthly_guarantee_amount", ImportValueKind.Decimal),
        new("family_classified_amount", ImportValueKind.Decimal),
        new("total_classified_amount", ImportValueKind.Decimal),
        new("calculation_method", ImportValueKind.String),
        new("grade_type", ImportValueKind.String),
        new("total_amount", ImportValueKind.Decimal),
        new("school_name", ImportValueKind.String),
        new("school_nature", ImportValueKind.String),
        new("enrollment_date", ImportValueKind.String),
        new("employer_name", ImportValueKind.String),
        new("employer_nature", ImportValueKind.String),
        new("employer_location", ImportValueKind.String),
        new("remark", ImportValueKind.String),
        new("province", ImportValueKind.String),
        new("city", ImportValueKind.String),
        new("district", ImportValueKind.String),
        new("street", ImportValueKind.String),
        new("community", ImportValueKind.String),
        new("family_address", ImportValueKind.String),
        new("certificate_number", ImportValueKind.String),
    };

    protected override IReadOnlyList<ImportColumnSpec> FamilyColumns => FamilyColumnSpecs;
    protected override IReadOnlyList<ImportColumnSpec> PersonColumns => PersonColumnSpecs;

    public RuralSubsistenceImportService(IDatabaseService databaseService, IDictCacheService dictCache, ILoggerService logger)
        : base(databaseService, dictCache, logger)
    {
    }
}
