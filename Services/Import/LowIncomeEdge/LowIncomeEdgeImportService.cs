using NewCosmos.Constants;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Import.LowIncomeEdge;

public class LowIncomeEdgeImportService : BaseCombinedImportService
{
    public override string ImportTypeName => ImportTypeCodes.LOW_INCOME_EDGE;
    public override string FamilyFilePattern => "低保边缘家庭查询_*.xlsx";
    public override string PersonFilePattern => "低保边缘人员查询_*.xlsx";
    protected override string FamilyTableName => "nc_biz_low_income_edge_families";
    protected override string PersonTableName => "nc_biz_low_income_edge_persons";

    protected override Dictionary<string, string[]> FamilyColumnAliases => new()
    {
        ["applicant_name"] = new[] { "户主姓名" },
        ["applicant_id_card"] = new[] { "户主身份证号" },
        ["guarantee_size"] = new[] { "保障人口", "保障人口数" },
        ["apply_reason"] = new[] { "申请原因" },
        ["hukou_type"] = new[] { "户籍类型" },
        ["phone"] = new[] { "联系电话" },
        ["address"] = new[] { "家庭住址" },
        ["province"] = new[] { "所属省", "所属省级" },
        ["city"] = new[] { "所属市", "所属市级" },
        ["district"] = new[] { "所属区", "所属区县" },
        ["street"] = new[] { "所属街", "所属街道" },
        ["community"] = new[] { "所属社", "所属社区" },
        ["include_month"] = new[] { "纳入月份" },
        ["member_names"] = new[] { "家庭成员姓名" },
        ["member_id_cards"] = new[] { "家庭成员身份证号" }
    };

    protected override Dictionary<string, string[]> PersonColumnAliases => new()
    {
        ["region"] = new[] { "所属区", "所属区划" },
        ["address"] = new[] { "家庭地址" },
        ["name"] = new[] { "姓名" },
        ["id_card"] = new[] { "身份证号" },
        ["relationship"] = new[] { "与户主关系" },
        ["head_id_card"] = new[] { "户主身份证号" },
        ["gender"] = new[] { "性别" },
        ["ethnicity"] = new[] { "民族" },
        ["marital_status"] = new[] { "婚姻状况" },
        ["education_level"] = new[] { "文化程度" },
        ["political_status"] = new[] { "政治面貌" },
        ["employment_status"] = new[] { "就业状况" },
        ["monthly_income"] = new[] { "月收入" },
        ["is_disabled"] = new[] { "是否残疾", "是否残疾人" },
        ["disability_certificate"] = new[] { "残疾证号" },
        ["disability_type"] = new[] { "残疾类别" },
        ["disability_level"] = new[] { "残疾等级" },
        ["work_capacity"] = new[] { "劳动能力" },
        ["health_status"] = new[] { "健康状况" },
        ["hukou_type"] = new[] { "户籍类型" },
        ["is_poverty_policy_population"] = new[] { "是否属脱贫享受政策人口" },
        ["include_month"] = new[] { "纳入月份" }
    };

    private static readonly ImportColumnSpec[] FamilyColumnSpecs =
    {
        new("applicant_name", ImportValueKind.String),
        new("applicant_id_card", ImportValueKind.String),
        new("guarantee_size", ImportValueKind.Int),
        new("apply_reason", ImportValueKind.String),
        new("hukou_type", ImportValueKind.String),
        new("phone", ImportValueKind.String),
        new("address", ImportValueKind.String),
        new("province", ImportValueKind.String),
        new("city", ImportValueKind.String),
        new("district", ImportValueKind.String),
        new("street", ImportValueKind.String),
        new("community", ImportValueKind.String),
        new("include_month", ImportValueKind.String),
        new("member_names", ImportValueKind.String),
        new("member_id_cards", ImportValueKind.String),
    };

    // 低保边缘人员表无 birth_date/age 列（与低保/特困不同），性别仍按身份证推导
    private static readonly ImportColumnSpec[] PersonColumnSpecs =
    {
        new("head_id_card", ImportValueKind.HeadIdCard),
        new("name", ImportValueKind.String),
        new("id_card", ImportValueKind.String),
        new("relationship", ImportValueKind.String, DictCategory: "FamilyRelationships"),
        new("gender", ImportValueKind.GenderFromIdCard),
        new("ethnicity", ImportValueKind.String, DictCategory: "Ethnicities"),
        new("marital_status", ImportValueKind.String, DictCategory: "MaritalStatuses"),
        new("education_level", ImportValueKind.String, DictCategory: "EducationLevels"),
        new("political_status", ImportValueKind.String, DictCategory: "PoliticalStatuses"),
        new("employment_status", ImportValueKind.String, DictCategory: "EmploymentStatuses"),
        new("monthly_income", ImportValueKind.Decimal),
        new("is_disabled", ImportValueKind.Bool),
        new("disability_certificate", ImportValueKind.String),
        new("disability_type", ImportValueKind.String, DictCategory: "DisabilityTypes"),
        new("disability_level", ImportValueKind.String, DictCategory: "DisabilityLevels"),
        new("work_capacity", ImportValueKind.String, DictCategory: "LaborAbilities"),
        new("health_status", ImportValueKind.String, DictCategory: "HealthStatuses"),
        new("hukou_type", ImportValueKind.String, DictCategory: "HukouTypes"),
        new("is_poverty_policy_population", ImportValueKind.Bool),
        new("region", ImportValueKind.String),
        new("address", ImportValueKind.String),
        new("include_month", ImportValueKind.String),
    };

    protected override IReadOnlyList<ImportColumnSpec> FamilyColumns => FamilyColumnSpecs;
    protected override IReadOnlyList<ImportColumnSpec> PersonColumns => PersonColumnSpecs;

    public LowIncomeEdgeImportService(IDatabaseService databaseService, IDictCacheService dictCache, ILoggerService logger)
        : base(databaseService, dictCache, logger)
    {
    }
}
