namespace NewCosmos.Constants;

public static class FieldKeys
{
    // 申请人
    public const string APPLICANT_NAME = "APPLICANT_NAME";
    public const string APPLICANT_ID_CARD = "APPLICANT_ID_CARD";
    public const string APPLICANT_ID_TYPE = "APPLICANT_ID_TYPE";
    public const string APPLICATION_REASON = "APPLICATION_REASON";
    public const string APPLICATION_DATE = "APPLICATION_DATE";
    public const string CONTACT_PHONE = "CONTACT_PHONE";

    // 家庭
    public const string FAMILY_ADDRESS = "FAMILY_ADDRESS";
    public const string FAMILY_VILLAGE = "FAMILY_VILLAGE";
    public const string COMMUNITY = "COMMUNITY";
    public const string FAMILY_DETAIL_ADDRESS = "FAMILY_DETAIL_ADDRESS";
    public const string HEAD_FAMILY_SIZE = "HEAD_FAMILY_SIZE";
    public const string FAMILY_SIZE = "FAMILY_SIZE";
    public const string APPLICANT_COUNT = "APPLICANT_COUNT";
    public const string STATUS = "STATUS";

    // 户主
    public const string HEAD_ID_CARD = "HEAD_ID_CARD";
    public const string HEAD_CITY = "HEAD_CITY";                              // 户主所在市

    // 配偶（遗失声明/无房产声明等模板专用）
    public const string SPOUSE_NAME = "SPOUSE_NAME";                          // 配偶姓名
    public const string SPOUSE_GENDER = "SPOUSE_GENDER";                      // 配偶性别
    public const string SPOUSE_ID_CARD = "SPOUSE_ID_CARD";                    // 配偶身份证号

    // 当前日期（声明类模板落款日期）
    public const string CURRENT_DATE = "CURRENT_DATE";                        // 当前日期

    // 经办人
    public const string OPERATOR_NAME = "OPERATOR_NAME";
    public const string OPERATOR_UNIT = "OPERATOR_UNIT";
    public const string REPORT_DATE = "REPORT_DATE";
    public const string CIVIL_ASSISTANT_NAME = "CIVIL_ASSISTANT_NAME";

    // 代理人
    public const string AGENT_NAME = "AGENT_NAME";
    public const string AGENT_ID_CARD = "AGENT_ID_CARD";
    public const string AGENT_ID_TYPE = "AGENT_ID_TYPE";
    public const string AGENT_CERT_TYPE = "AGENT_CERT_TYPE";
    public const string AGENT_RELATION = "AGENT_RELATION";
    public const string IS_AGENT = "IS_AGENT";

    // 家庭成员（TableData替换）
    public const string FAMILY_MEMBER_NAME = "FAMILY_MEMBER_NAME";
    public const string FAMILY_MEMBER_ID_CARD = "FAMILY_MEMBER_ID_CARD";
    public const string FAMILY_MEMBER_CERT_TYPE = "FAMILY_MEMBER_CERT_TYPE";
    public const string FAMILY_MEMBER_RELATION = "FAMILY_MEMBER_RELATION";
    public const string FAMILY_MEMBER_ADDRESS = "FAMILY_MEMBER_ADDRESS";
    public const string FAMILY_MEMBER_AGE = "FAMILY_MEMBER_AGE";                       // 成员年龄（模板74低收入审核确认表）
    public const string FAMILY_MEMBER_HUKOU = "FAMILY_MEMBER_HUKOU";                   // 成员户口性质（模板74低收入审核确认表）
    public const string FAMILY_MEMBER_EMPLOYMENT = "FAMILY_MEMBER_EMPLOYMENT";         // 成员就业状况（模板74低收入审核确认表）

    // 组织架构
    public const string DISTRICT = "DISTRICT";
    public const string TOWN = "TOWN";
    public const string DISTRICT_CIVIL_BUREAU = "DISTRICT_CIVIL_BUREAU";
    public const string DISTRICT_CIVIL_BUREAU_PHONE = "DISTRICT_CIVIL_BUREAU_PHONE";
    public const string OPERATOR_UNIT_PHONE = "OPERATOR_UNIT_PHONE";
    public const string NOTICE_NUMBER = "NOTICE_NUMBER";

    // 入户调查
    public const string SURVEY_DATE = "SURVEY_DATE";
    public const string HUKOU_ADDRESS = "HUKOU_ADDRESS";
    public const string CLASSIFICATION_RESULT = "CLASSIFICATION_RESULT";

    // 入户调查表新增字段
    public const string LAND_INCOME_SITUATION = "LAND_INCOME_SITUATION";          // 土地收入情况（复合字段）
    public const string LAND_INCOME_SELF_FARM = "LAND_INCOME_SELF_FARM";          // 自种收入
    public const string LAND_INCOME_SUBLEASE = "LAND_INCOME_SUBLEASE";            // 转包收入
    public const string LAND_INCOME_CONTRACT = "LAND_INCOME_CONTRACT";            // 承包收入
    public const string TOTAL_FAMILY_INCOME_ANNUAL = "TOTAL_FAMILY_INCOME_ANNUAL"; // 家庭总收入（年）
    public const string PER_CAPITA_INCOME_ANNUAL = "PER_CAPITA_INCOME_ANNUAL";    // 家庭人均收入（年）
    public const string APPLICATION_REASON_DETAIL = "APPLICATION_REASON_DETAIL";    // 申请理由
    public const string SURVEY_VISIT_SITUATION = "SURVEY_VISIT_SITUATION";        // 调查走访情况
    public const string SINGLE_RESCUE_SITUATION = "SINGLE_RESCUE_SITUATION";      // 单人保情况
    public const string RIGID_EXPENDITURE_DETAIL = "RIGID_EXPENDITURE_DETAIL";    // 刚性支出
    public const string PROPERTY_EXEMPTION_SITUATION = "PROPERTY_EXEMPTION_SITUATION"; // 家庭财产豁免情况
    public const string KINSHIP_FILING_SITUATION = "KINSHIP_FILING_SITUATION";     // 近亲属备案
    public const string SURVEY_TIME = "SURVEY_TIME";                              // 入户调查时间

    // 共同生活成员（索引字段基础键）
    public const string SHARED_MEMBER_NAME = "SHARED_MEMBER_NAME";
    public const string SHARED_MEMBER_GENDER = "SHARED_MEMBER_GENDER";
    public const string SHARED_MEMBER_AGE = "SHARED_MEMBER_AGE";
    public const string SHARED_MEMBER_RELATION = "SHARED_MEMBER_RELATION";
    public const string SHARED_MEMBER_EMPLOYMENT = "SHARED_MEMBER_EMPLOYMENT";

    // 赡养人（索引字段基础键）
    public const string SUPPORTER_NAME = "SUPPORTER_NAME";
    public const string SUPPORTER_GENDER = "SUPPORTER_GENDER";
    public const string SUPPORTER_AGE = "SUPPORTER_AGE";
    public const string SUPPORTER_RELATION = "SUPPORTER_RELATION";
    public const string SUPPORTER_AMOUNT = "SUPPORTER_AMOUNT";
    public const string SUPPORTER_ABILITY = "SUPPORTER_ABILITY";
    public const string SUPPORTER_ID_CARD = "SUPPORTER_ID_CARD";
    public const string SUPPORTER_PHONE = "SUPPORTER_PHONE";
    public const string SUPPORTER_WORK = "SUPPORTER_WORK";                       // 工作单位（模板9子女收入调查表）
    public const string SUPPORTER_INCOME_SOURCE = "SUPPORTER_INCOME_SOURCE";     // 主要收入来源（模板9子女收入调查表）
    public const string SUPPORTER_INCOME = "SUPPORTER_INCOME";                   // 收入情况（模板9子女收入调查表）
    public const string SUPPORTER_OCCUPATION = "SUPPORTER_OCCUPATION";           // 职业/就业状况（模板9/74赡养义务人）
    public const string SUPPORTER_FAMILY_SIZE = "SUPPORTER_FAMILY_SIZE";         // 赡养义务人家庭人口（模板74）
    public const string SUPPORTER_MONTHLY_INCOME = "SUPPORTER_MONTHLY_INCOME";   // 赡养义务人月收入（模板74）
    // 模板74（最低生活保障边缘家庭审核确认表）赡养义务人合计（专用键，避免与模板15家庭人口 FAMILY_SIZE 冲突）
    public const string SUPPORTER_TOTAL_FAMILY_SIZE = "SUPPORTER_TOTAL_FAMILY_SIZE";       // 赡养义务人家庭总人口合计
    public const string SUPPORTER_TOTAL_INCOME_MONTHLY = "SUPPORTER_TOTAL_INCOME_MONTHLY"; // 赡养义务人月收入合计
    public const string SUPPORTER_PER_CAPITA_INCOME_MONTHLY = "SUPPORTER_PER_CAPITA_INCOME_MONTHLY"; // 赡养义务人人均月收入

    // 家庭收入（月口径）
    public const string TOTAL_FAMILY_INCOME_MONTHLY = "TOTAL_FAMILY_INCOME_MONTHLY"; // 家庭月总收入
    public const string PER_CAPITA_INCOME_MONTHLY = "PER_CAPITA_INCOME_MONTHLY";    // 家庭月人均收入

    // 公示文件
    public const string GUARANTEE_AMOUNT = "GUARANTEE_AMOUNT";
    // 模板14（低保审核确认表）专用：确定金额 = 户月保障金额（不含分类施保加发）
    public const string CONFIRM_AMOUNT = "CONFIRM_AMOUNT";
    public const string GUARANTEE_TYPE_DISPLAY = "GUARANTEE_TYPE_DISPLAY";
    public const string PUBLICITY_START_DATE = "PUBLICITY_START_DATE";
    public const string PUBLICITY_END_DATE = "PUBLICITY_END_DATE";

    // 每月公示名单（模板：公共_每月公示名单）表头占位符
    public const string PUBLICITY_COUNTY = "{所在县}";                    // 所在县
    public const string PUBLICITY_TOWN = "{所在镇}";                      // 所在镇
    public const string PUBLICITY_PERIOD = "{当前月公示周期}";            // 公示周期（整月）
    public const string PUBLICITY_TOWN_PHONE = "{所在镇电话}";            // 所在镇投诉电话
    // 每月公示名单数据行占位符模板（N=1..22）
    public const string PUBLICITY_ROW_NAME = "{救助姓名";                 // {救助姓名N}
    public const string PUBLICITY_ROW_VILLAGE = "{救助人所在村屯";        // {救助人所在村屯N}
    public const string PUBLICITY_ROW_TYPE = "{救助类型";                 // {救助类型N}
    public const string PUBLICITY_ROW_SIZE = "{救助人数";                 // {救助人数N}
    public const string PUBLICITY_ROW_RELATIVE = "{是否近亲属备案";       // {是否近亲属备案N}

    // 财产状况
    public const string PROPERTY_RENTAL = "PROPERTY_RENTAL";
    public const string PROPERTY_BUILDING_AREA = "PROPERTY_BUILDING_AREA";
    public const string PROPERTY_PRIVATE = "PROPERTY_PRIVATE";
    public const string PROPERTY_STRUCTURE = "PROPERTY_STRUCTURE";
    public const string PROPERTY_VEHICLE = "PROPERTY_VEHICLE";
    public const string PROPERTY_DEPOSIT = "PROPERTY_DEPOSIT";
    public const string PROPERTY_SECURITY = "PROPERTY_SECURITY";
    public const string PROPERTY_FUND = "PROPERTY_FUND";
    public const string PROPERTY_INSURANCE = "PROPERTY_INSURANCE";
    public const string PROPERTY_BUSINESS_REG = "PROPERTY_BUSINESS_REG";
    public const string PROPERTY_BOND = "PROPERTY_BOND";
    public const string PROPERTY_OTHER = "PROPERTY_OTHER";

    // 收入状况
    public const string INCOME_BREEDING = "INCOME_BREEDING";
    public const string INCOME_LABOR = "INCOME_LABOR";
    public const string INCOME_BUSINESS = "INCOME_BUSINESS";
    public const string INCOME_PROPERTY = "INCOME_PROPERTY";
    public const string INCOME_TRANSFER = "INCOME_TRANSFER";
    public const string INCOME_ALIMONY = "INCOME_ALIMONY";
    public const string INCOME_OTHER = "INCOME_OTHER";
    public const string INCOME_TOTAL = "INCOME_TOTAL";
    public const string INCOME_RIGID_EXPENDITURE = "INCOME_RIGID_EXPENDITURE";
    public const string LABOR_LOCATION = "LABOR_LOCATION";
    public const string LABOR_TYPE = "LABOR_TYPE";
    public const string BUSINESS_LICENSE = "BUSINESS_LICENSE";
    public const string BUSINESS_SCALE = "BUSINESS_SCALE";
    public const string BUSINESS_STALL = "BUSINESS_STALL";
    public const string BUSINESS_STALL_SCALE = "BUSINESS_STALL_SCALE";

    // 务工详情（索引字段）
    public const string LABOR_LOCATION_1 = "LABOR_LOCATION_1";
    public const string LABOR_LOCATION_2 = "LABOR_LOCATION_2";
    public const string LABOR_TYPE_1 = "LABOR_TYPE_1";
    public const string LABOR_TYPE_2 = "LABOR_TYPE_2";

    // 养殖业收入
    public const string INCOME_BREEDING_AMOUNT = "INCOME_BREEDING_AMOUNT";

    // 农业补贴
    public const string INCOME_SUBSIDY = "INCOME_SUBSIDY";

    // 土地收入
    public const string INCOME_LAND = "INCOME_LAND";

    // 经营业收入
    public const string INCOME_BUSINESS_AMOUNT = "INCOME_BUSINESS_AMOUNT";

    // 土地相关
    public const string FAMILY_LAND_AREA = "FAMILY_LAND_AREA";
    public const string SELF_FARMED_LAND_AREA = "SELF_FARMED_LAND_AREA";
    public const string SUBLEASED_LAND_AREA = "SUBLEASED_LAND_AREA";
    public const string CONTRACTED_LAND_AREA = "CONTRACTED_LAND_AREA";
    public const string LAND_INCOME_TOTAL = "LAND_INCOME_TOTAL";
    public const string TOTAL_CONFIRMED_LAND_AREA = "TOTAL_CONFIRMED_LAND_AREA";

    // 低保审核确认表（模板14）
    public const string APPLICANT_GENDER = "APPLICANT_GENDER";
    public const string EMPLOYMENT_STATUS = "EMPLOYMENT_STATUS";
    public const string HOUSING_CATEGORY = "HOUSING_CATEGORY";
    public const string HEALTH_STATUS = "HEALTH_STATUS";
    public const string MARITAL_STATUS = "MARITAL_STATUS";
    public const string URBAN_MONTHLY_INCOME = "URBAN_MONTHLY_INCOME";
    public const string RURAL_ANNUAL_INCOME = "RURAL_ANNUAL_INCOME";
    public const string ENJOY_CLASSIFIED_SUBSIDY = "ENJOY_CLASSIFIED_SUBSIDY";
    public const string CLASSIFIED_SUBSIDY_AMOUNT = "CLASSIFIED_SUBSIDY_AMOUNT";
    public const string ENJOY_DISABILITY_ALLOWANCE = "ENJOY_DISABILITY_ALLOWANCE";
    public const string DISABILITY_ALLOWANCE_AMOUNT = "DISABILITY_ALLOWANCE_AMOUNT";
    public const string ENJOY_ELDERLY_ALLOWANCE = "ENJOY_ELDERLY_ALLOWANCE";
    public const string ELDERLY_ALLOWANCE_AMOUNT = "ELDERLY_ALLOWANCE_AMOUNT";
    public const string SINGLE_PERSON_GUARANTEE = "SINGLE_PERSON_GUARANTEE";
    public const string GUARANTEE_FAMILY_SIZE = "GUARANTEE_FAMILY_SIZE";
    public const string RIGID_EXPENDITURE = "RIGID_EXPENDITURE";
    public const string KINSHIP_FILING = "KINSHIP_FILING";
    public const string PROPERTY_EXEMPTION = "PROPERTY_EXEMPTION";
    public const string PUBLICITY_STATUS = "PUBLICITY_STATUS";
    public const string GOVERNMENT_OPINION = "GOVERNMENT_OPINION";
    public const string AUDIT_DATE = "AUDIT_DATE";
    public const string VERIFIED_BENEFIT = "VERIFIED_BENEFIT";
    public const string ARCHIVE_EFFECTIVE_DATE = "ARCHIVE_EFFECTIVE_DATE";

    // 分类施保调整表（模板15）
    public const string APPLICANT_BIRTH_DATE = "APPLICANT_BIRTH_DATE";
    public const string NATIONALITY = "NATIONALITY";
    public const string CLASSIFIED_SUBSIDY_TYPE = "CLASSIFIED_SUBSIDY_TYPE";
    public const string CLASSIFIED_TOTAL_COUNT = "CLASSIFIED_TOTAL_COUNT";
    public const string CLASSIFIED_TOTAL_AMOUNT = "CLASSIFIED_TOTAL_AMOUNT";
    public const string AUDIT_OPINION = "AUDIT_OPINION";

    // 经济财产声明书（模板16）
    public const string DECLARATION_CLASSIFICATION = "DECLARATION_CLASSIFICATION"; // 救助类别勾选框（模板16专用，避免与CLASSIFICATION_RESULT冲突）
    public const string LAND_STATUS = "LAND_STATUS";
    public const string LAND_AREA = "LAND_AREA";
    public const string HAS_PENSION = "HAS_PENSION";
    public const string PENSION_AMOUNT = "PENSION_AMOUNT";
    public const string HAS_UNEMPLOYMENT = "HAS_UNEMPLOYMENT";
    public const string UNEMPLOYMENT_AMOUNT = "UNEMPLOYMENT_AMOUNT";
    public const string HAS_ODD_JOB = "HAS_ODD_JOB";
    public const string ODD_JOB_AMOUNT = "ODD_JOB_AMOUNT";
    public const string HAS_BUSINESS = "HAS_BUSINESS";
    public const string BUSINESS_AMOUNT = "BUSINESS_AMOUNT";
    public const string OTHER_INCOME = "OTHER_INCOME";
    public const string HAS_CASH = "HAS_CASH";
    public const string CASH_AMOUNT = "CASH_AMOUNT";
    public const string HAS_BANK_DEPOSIT = "HAS_BANK_DEPOSIT";
    public const string BANK_DEPOSIT_AMOUNT = "BANK_DEPOSIT_AMOUNT";
    public const string BANK_DEPOSIT_NAME = "BANK_DEPOSIT_NAME";
    public const string HAS_STOCK = "HAS_STOCK";
    public const string STOCK_AMOUNT = "STOCK_AMOUNT";
    public const string STOCK_BANK_NAME = "STOCK_BANK_NAME";
    public const string HAS_HOUSE = "HAS_HOUSE";
    public const string LIVING_AREA = "LIVING_AREA";
    public const string HAS_VEHICLE = "HAS_VEHICLE";
    public const string LICENSE_PLATE = "LICENSE_PLATE";
    public const string HAS_INSURANCE = "HAS_INSURANCE";
    public const string INSURANCE_AMOUNT = "INSURANCE_AMOUNT";

    // 土地证明（模板17/18）
    public const string SUBSIDY_TYPE_CONTENT = "SUBSIDY_TYPE_CONTENT";
    public const string LAND_DETAIL_INFO = "LAND_DETAIL_INFO";

    // 赡养费承诺书（模板19）
    public const string SUPPORTER_INFO = "SUPPORTER_INFO";
    public const string HEAD_INFO = "HEAD_INFO";
    public const string SPOUSE_INFO = "SPOUSE_INFO";
    public const string SUPPORT_FEE = "SUPPORT_FEE";
    public const string SUPPORT_FEE_CHINESE = "SUPPORT_FEE_CHINESE";

    // 特困入户调查表（模板23）
    public const string DESTITUTE_SUPPORT_TYPE = "DESTITUTE_SUPPORT_TYPE";           // 供养方式（集中/分散）
    public const string DESTITUTE_CAREGIVER_SITUATION = "DESTITUTE_CAREGIVER_SITUATION"; // 照料人情况
    public const string DESTITUTE_HEALTH_ASSESSMENT = "DESTITUTE_HEALTH_ASSESSMENT"; // 健康评估
    public const string DESTITUTE_LIVING_CONDITION = "DESTITUTE_LIVING_CONDITION";   // 居住条件
    public const string DESTITUTE_DAILY_CARE = "DESTITUTE_DAILY_CARE";              // 日常照料
    public const string DESTITUTE_MEDICAL_SITUATION = "DESTITUTE_MEDICAL_SITUATION"; // 医疗情况
    public const string DESTITUTE_FAMILY_STATUS = "DESTITUTE_FAMILY_STATUS";         // 家庭状况
    public const string DESTITUTE_PROPERTY_SITUATION = "DESTITUTE_PROPERTY_SITUATION"; // 财产状况
    public const string DESTITUTE_SURVEY_CONCLUSION = "DESTITUTE_SURVEY_CONCLUSION"; // 调查结论
    public const string DESTITUTE_INSTITUTION_INFO = "DESTITUTE_INSTITUTION_INFO";   // 供养机构信息
    public const string DESTITUTE_SELF_CARE_ABILITY = "DESTITUTE_SELF_CARE_ABILITY"; // 自理能力
    public const string DESTITUTE_CARE_LEVEL = "DESTITUTE_CARE_LEVEL";              // 照料等级

    // 特困审核确认表（模板75）专用
    public const string DESTITUTE_AUDIT_CATEGORY = "DESTITUTE_AUDIT_CATEGORY";       // 人员类别（特困全称）
    public const string DESTITUTE_AUDIT_CLASSIFICATION = "DESTITUTE_AUDIT_CLASSIFICATION"; // 享受待遇类型（特困全称）
    public const string DESTITUTE_DISABILITY = "DESTITUTE_DISABILITY";              // 残疾类别（类型+等级）

    // 能力鉴定（六项考核指标）
    public const string CAPABILITY_ASSESSMENT_DATE = "CAPABILITY_ASSESSMENT_DATE";  // 评估日期
    public const string CAPABILITY_EATING = "CAPABILITY_EATING";                    // 进食
    public const string CAPABILITY_DRESSING = "CAPABILITY_DRESSING";                // 穿衣
    public const string CAPABILITY_GETTING_IN_OUT_BED = "CAPABILITY_GETTING_IN_OUT_BED"; // 上下床
    public const string CAPABILITY_USING_TOILET = "CAPABILITY_USING_TOILET";        // 如厕
    public const string CAPABILITY_INDOOR_WALKING = "CAPABILITY_INDOOR_WALKING";    // 室内行走
    public const string CAPABILITY_BATHING = "CAPABILITY_BATHING";                  // 洗澡
    public const string CAPABILITY_COMPLETED_ITEMS = "CAPABILITY_COMPLETED_ITEMS";   // 能完成项目数
    public const string CAPABILITY_SELF_CARE_LEVEL = "CAPABILITY_SELF_CARE_LEVEL";  // 自理能力等级
    public const string CAPABILITY_ASSESSOR = "CAPABILITY_ASSESSOR";               // 评估人
    public const string CAPABILITY_REMARK = "CAPABILITY_REMARK";                   // 备注

    // 档案目录（模板22）
    public const string APPLICATION_NO = "APPLICATION_NO";                         // 申请编号
    public const string APPLICATION_YEAR = "APPLICATION_YEAR";                     // 申请年份
    public const string BUSINESS_END_DATE = "BUSINESS_END_DATE";                   // 业务终结时间
    public const string ARCHIVE_TITLE = "ARCHIVE_TITLE";                           // 案卷题名
    public const string RETENTION_PERIOD = "RETENTION_PERIOD";                     // 保管期限
    public const string ARCHIVE_VOLUME_NO = "ARCHIVE_VOLUME_NO";                   // 档案卷号
    public const string SOCIAL_ASSISTANCE_TYPE = "SOCIAL_ASSISTANCE_TYPE";         // 社会救助类型（目录）
    public const string ARCHIVE_VOLUME = "ARCHIVE_VOLUME";                         // 档案卷号（模板配置兼容键）
    public const string DIRECTORY_PERSON = "DIRECTORY_PERSON";                     // 责任人（基础键，索引 _1.._27）
    public const string DIRECTORY_TITLE = "DIRECTORY_TITLE";                       // 档案题名（基础键）
    public const string DIRECTORY_DATE = "DIRECTORY_DATE";                         // 档案日期（基础键）
    public const string DIRECTORY_PAGE = "DIRECTORY_PAGE";                         // 档案页号（基础键）
    public const string COPYRIGHT_INFO = "COPYRIGHT_INFO";                         // 版权信息（底部版权行，由户主出生月份映射电信号生成）

    // 档案封面
    public const string COVER_CLASSIFICATION = "COVER_CLASSIFICATION";             // 享受类别（封面专用，避免模板16覆盖）
    public const string ARCHIVE_NUMBER = "ARCHIVE_NUMBER";                         // 档案编号（封面）

    // 档案认定结果告知书
    public const string NOTICE_CLASSIFICATION_RESULT = "NOTICE_CLASSIFICATION_RESULT"; // 享受分类（告知书专用，避免模板16覆盖）

    // ── 林口县社会救助告知书（收入超标停保/不予认定） ──
    public const string NOTICE_BUSINESS_CATEGORY = "NOTICE_BUSINESS_CATEGORY";     // 业务分类："停止" / "不予认定"
    public const string NOTICE_TOWN = "NOTICE_TOWN";                               // 所在镇
    public const string NOTICE_VILLAGE = "NOTICE_VILLAGE";                         // 所在村
    public const string NOTICE_FAMILY_SIZE_TEXT = "NOTICE_FAMILY_SIZE_TEXT";       // 申请家庭人口（文字："X人"）
    public const string NOTICE_HANDLING_CATEGORY = "NOTICE_HANDLING_CATEGORY";     // 承办分类（全称）
    public const string NOTICE_BENEFIT_TYPE = "NOTICE_BENEFIT_TYPE";              // 享受类型："享受" / "停止享受"
    public const string NOTICE_BENEFIT_RESULT = "NOTICE_BENEFIT_RESULT";          // 享受结果："社会救助保障待遇"
    public const string NOTICE_DETAILED_REASON = "NOTICE_DETAILED_REASON";        // 具体原因（自动生成的经济理由）
    public const string NOTICE_CUSTOM_REASON = "NOTICE_CUSTOM_REASON";            // 其他自定义原因（可选补充，新模板已无此占位符）
    public const string NOTICE_DELIVERER = "NOTICE_DELIVERER";                    // 送达告知人（当前登录用户姓名）
    public const string NOTICE_ORG_UNIT = "NOTICE_ORG_UNIT";                      // 当前工作所在单位（当前登录用户所在机构名）
    public const string NOTICE_DATE = "NOTICE_DATE";                              // 当前日期（告知书落款日期）

    // 模板13 公示文件专用（避免与模板16的CLASSIFICATION_RESULT冲突）
    public const string PUBLICITY_CLASSIFICATION = "PUBLICITY_CLASSIFICATION";       // 公示类型（全称）

    // 模板14 低保审核确认表专用（避免与模板16的CLASSIFICATION_RESULT冲突）
    public const string AUDIT_CLASSIFICATION = "AUDIT_CLASSIFICATION";              // 享受类别（简称）

    // 模板8 低保入户调查表专用（避免与模板16的CLASSIFICATION_RESULT冲突）
    public const string SURVEY_CLASSIFICATION = "SURVEY_CLASSIFICATION";             // 享受待遇类别（简称）

    // 模板10 最低生活保障申请书专用（避免与模板16的CLASSIFICATION_RESULT冲突）
    public const string APPLY_CLASSIFICATION = "APPLY_CLASSIFICATION";              // 保障类型（简称）

    // 一事一议申报表专用（档案_乡镇社会救助一事一议申报表）
    public const string SPECIAL_APPROVAL_BASIC_SITUATION = "SPECIAL_APPROVAL_BASIC_SITUATION"; // 申请救助家庭基本情况
    public const string SPECIAL_APPROVAL_MATTERS = "SPECIAL_APPROVAL_MATTERS";       // 申请救助家庭需要一事一议说明的情况
    public const string SPECIAL_APPROVAL_AUDIT_RESULT = "SPECIAL_APPROVAL_AUDIT_RESULT"; // 乡镇民政办审核结果
    public const string SPECIAL_APPROVAL_AUDIT_DATE = "SPECIAL_APPROVAL_AUDIT_DATE"; // 审核日期（申报表专用）

    // ── 普惠高龄补贴（登记表/享受须知/新增明细/停止明细/取消备案表） ──
    public const string ELDERLY_NAME = "ELDERLY_NAME";                               // 申请人姓名
    public const string ELDERLY_AGE = "ELDERLY_AGE";                                 // 申请人年龄
    public const string ELDERLY_PHONE = "ELDERLY_PHONE";                             // 联系电话
    public const string ELDERLY_ID_CARD = "ELDERLY_ID_CARD";                         // 身份证号
    public const string ELDERLY_HUKOU_ADDRESS = "ELDERLY_HUKOU_ADDRESS";             // 户籍地址
    public const string ELDERLY_FAMILY_ADDRESS = "ELDERLY_FAMILY_ADDRESS";           // 家庭住址
    public const string ELDERLY_BANK_NAME = "ELDERLY_BANK_NAME";                     // 社保卡开户行
    public const string ELDERLY_BANK_ACCOUNT = "ELDERLY_BANK_ACCOUNT";               // 社保卡账号
    public const string ELDERLY_AGENT_NAME = "ELDERLY_AGENT_NAME";                   // 代办人姓名
    public const string ELDERLY_AGENT_RELATION = "ELDERLY_AGENT_RELATION";           // 代办人关系
    public const string ELDERLY_AGENT_RECEIVE_NAME = "ELDERLY_AGENT_RECEIVE_NAME";   // 代领人姓名
    public const string ELDERLY_AGENT_RECEIVE_RELATION = "ELDERLY_AGENT_RECEIVE_RELATION"; // 代领人与申请人关系
    public const string ELDERLY_AGENT_RECEIVE_BANK_NAME = "ELDERLY_AGENT_RECEIVE_BANK_NAME"; // 代领人社保卡开户行
    public const string ELDERLY_AGENT_RECEIVE_BANK_ACCOUNT = "ELDERLY_AGENT_RECEIVE_BANK_ACCOUNT"; // 代领人社保卡账号
    public const string ELDERLY_AGENT_RECEIVE_REASON = "ELDERLY_AGENT_RECEIVE_REASON"; // 代领原因
    public const string ELDERLY_CATEGORY = "ELDERLY_CATEGORY";                       // 享受类别（中文名）
    public const string ELDERLY_APPLY_DATE = "ELDERLY_APPLY_DATE";                   // 申请日期
    public const string ELDERLY_ACCEPT_DATE = "ELDERLY_ACCEPT_DATE";                 // 受理日期
    public const string ELDERLY_ISSUE_START_MONTH = "ELDERLY_ISSUE_START_MONTH";     // 计发年月
    public const string ELDERLY_ISSUE_AMOUNT = "ELDERLY_ISSUE_AMOUNT";               // 计发金额
    public const string ELDERLY_PAYBACK_RANGE = "ELDERLY_PAYBACK_RANGE";             // 补发起止与补发月数（如 2024-10至2025-06，共9个月）
    public const string ELDERLY_PAYBACK_AMOUNT = "ELDERLY_PAYBACK_AMOUNT";           // 补发金额
    public const string ELDERLY_PAYBACK_REASON = "ELDERLY_PAYBACK_REASON";           // 补发原因
    public const string ELDERLY_STOP_REASON = "ELDERLY_STOP_REASON";                 // 停发原因
    public const string ELDERLY_DUE_STOP_DATE = "ELDERLY_DUE_STOP_DATE";             // 应停发时间
    public const string ELDERLY_ACTUAL_STOP_DATE = "ELDERLY_ACTUAL_STOP_DATE";       // 实停发时间
    public const string ELDERLY_IS_RECOVER = "ELDERLY_IS_RECOVER";                   // 是否追缴（是/否）
    public const string ELDERLY_RECOVER_RANGE = "ELDERLY_RECOVER_RANGE";             // 追缴起止时间与追缴月数
    public const string ELDERLY_RECOVER_AMOUNT = "ELDERLY_RECOVER_AMOUNT";           // 追缴金额
    public const string ELDERLY_REMARK = "ELDERLY_REMARK";                           // 备注
    public const string ELDERLY_STANDARD = "ELDERLY_STANDARD";                       // 补贴标准（元/月）
    public const string ELDERLY_ACTUAL_AMOUNT = "ELDERLY_ACTUAL_AMOUNT";             // 实际发放金额
    public const string ELDERLY_CONFIRM_DATE = "ELDERLY_CONFIRM_DATE";               // 审批时间
    public const string ELDERLY_CONTACT_NAME = "ELDERLY_CONTACT_NAME";               // 联系人姓名
    public const string ELDERLY_CONTACT_RELATION = "ELDERLY_CONTACT_RELATION";       // 联系关系
    public const string ELDERLY_GENDER = "ELDERLY_GENDER";                           // 性别
    public const string ELDERLY_ADDRESS = "ELDERLY_ADDRESS";                         // 住址（明细表）
    public const string ELDERLY_PAYBACK_TIME = "ELDERLY_PAYBACK_TIME";               // 补发时间（明细表）
    public const string ELDERLY_PAYBACK_MONTHS = "ELDERLY_PAYBACK_MONTHS";           // 补发月份（明细表）
    public const string ELDERLY_INDEX = "ELDERLY_INDEX";                             // 序号（明细表）
    public const string ELDERLY_STOP_TIME = "ELDERLY_STOP_TIME";                     // 停止时间（明细表）

    // ── 普惠高龄津贴调整备案表（类别复核变更专用） ──
    public const string ELDERLY_ADJUST_NAME = "ELDERLY_ADJUST_NAME";                             // 调整姓名
    public const string ELDERLY_ADJUST_ID_CARD = "ELDERLY_ADJUST_ID_CARD";                       // 调整身份证号
    public const string ELDERLY_ADJUST_HUKOU_ADDRESS = "ELDERLY_ADJUST_HUKOU_ADDRESS";           // 调整户籍地址
    public const string ELDERLY_ADJUST_FAMILY_ADDRESS = "ELDERLY_ADJUST_FAMILY_ADDRESS";         // 调整家庭地址
    public const string ELDERLY_ADJUST_REASON = "ELDERLY_ADJUST_REASON";                         // 调整原因（勾选文本）
    public const string ELDERLY_ADJUST_TIME = "ELDERLY_ADJUST_TIME";                             // 调整时间（年月）
    public const string ELDERLY_ADJUST_AMOUNT = "ELDERLY_ADJUST_AMOUNT";                         // 调整金额（由X元/月调整至Y元/月）
    public const string ELDERLY_ADJUST_PAYBACK_RANGE = "ELDERLY_ADJUST_PAYBACK_RANGE";           // 补发起止时间与补发月数
    public const string ELDERLY_ADJUST_PAYBACK_AMOUNT = "ELDERLY_ADJUST_PAYBACK_AMOUNT";         // 补发金额
    public const string ELDERLY_ADJUST_ACCEPT_DATE = "ELDERLY_ADJUST_ACCEPT_DATE";               // 受理日期

    // ── 临时救助（入户调查表/大额审批表/小额审批表/信息公示/验收报告） ──
    // 主表与通用字段
    public const string TEMP_REPORT_UNIT = "TEMP_REPORT_UNIT";                       // 填报单位
    public const string TEMP_REPORT_TIME = "TEMP_REPORT_TIME";                       // 填报时间
    public const string TEMP_APPLICANT_NAME = "TEMP_APPLICANT_NAME";                 // 申请人姓名
    public const string TEMP_APPLICANT_GENDER = "TEMP_APPLICANT_GENDER";             // 申请人性别
    public const string TEMP_APPLICANT_AGE = "TEMP_APPLICANT_AGE";                   // 申请人年龄
    public const string TEMP_APPLICANT_ID_CARD = "TEMP_APPLICANT_ID_CARD";           // 申请人身份证号
    public const string TEMP_FAMILY_ADDRESS = "TEMP_FAMILY_ADDRESS";                 // 家庭住址
    public const string TEMP_POLICY_ENJOYED = "TEMP_POLICY_ENJOYED";                 // 享受的政策救助情况
    public const string TEMP_FAMILY_MEMBER_STATUS = "TEMP_FAMILY_MEMBER_STATUS";     // 家庭成员状态
    public const string TEMP_DIFFICULTY_REASON = "TEMP_DIFFICULTY_REASON";           // 申请救助原因/困难情况
    public const string TEMP_DIFFICULTY_TYPE = "TEMP_DIFFICULTY_TYPE";               // 困难类型（疾病/意外灾害/教育支出/其他困难）
    public const string TEMP_DIFFICULTY_DETAILS = "TEMP_DIFFICULTY_DETAILS";         // 困难情况明细（按类型合并文本，多条序号展示）
    public const string TEMP_DISEASE_DIAGNOSIS = "TEMP_DISEASE_DIAGNOSIS";           // 疾病诊断结果（多条以分号分隔合并）
    public const string TEMP_DISEASE_CODE = "TEMP_DISEASE_CODE";                     // 疾病编码（ICD-10，多条以分号分隔合并）
    public const string TEMP_DISEASE_SELF_PAID = "TEMP_DISEASE_SELF_PAID";           // 自费费用（元，疾病明细共享值）
    public const string TEMP_DISEASE_HOSPITAL = "TEMP_DISEASE_HOSPITAL";             // 所在医院（疾病明细共享值）
    public const string TEMP_ACCIDENT_DETAILS = "TEMP_ACCIDENT_DETAILS";             // 意外灾害明细（多条序号展示）
    public const string TEMP_EDUCATION_DETAILS = "TEMP_EDUCATION_DETAILS";           // 教育支出明细（多条序号展示）
    public const string TEMP_APPLY_DATE = "TEMP_APPLY_DATE";                         // 申请日期
    public const string TEMP_FAMILY_SIZE = "TEMP_FAMILY_SIZE";                       // 家庭人口
    public const string TEMP_FAMILY_CATEGORY = "TEMP_FAMILY_CATEGORY";               // 户主家庭类别
    public const string TEMP_VERIFY_RESULT = "TEMP_VERIFY_RESULT";                   // 入户核实情况
    public const string TEMP_CONFIRM_AMOUNT = "TEMP_CONFIRM_AMOUNT";                 // 确定金额（小额审批表）
    public const string TEMP_PUBLICIZE_RANGE = "TEMP_PUBLICIZE_RANGE";               // 公示日期区间
    public const string TEMP_HUKOU_ADDRESS = "TEMP_HUKOU_ADDRESS";                   // 户籍所在地
    public const string TEMP_VILLAGE = "TEMP_VILLAGE";                               // 所在村/社区
    public const string TEMP_TOWN = "TEMP_TOWN";                                     // 所在镇/街道
    public const string TEMP_ACCEPTANCE_PERSON = "TEMP_ACCEPTANCE_PERSON";           // 乡镇验收人（民政助理）
    public const string TEMP_CONTACT_PHONE = "TEMP_CONTACT_PHONE";                   // 工作单位联系方式（公示异议反馈电话）
    public const string TEMP_RELIEF_TYPE_NAME = "TEMP_RELIEF_TYPE_NAME";             // 救助类型名称（大额/小额）
    public const string TEMP_APPLICATION_NO = "TEMP_APPLICATION_NO";                 // 申请编号
    public const string TEMP_APPLICANT_PHONE = "TEMP_APPLICANT_PHONE";               // 申请人联系电话（审批表）
    public const string TEMP_PUBLICIZE_START = "TEMP_PUBLICIZE_START";               // 公示开始日期（验收报告日期起）
    public const string TEMP_PUBLICIZE_END = "TEMP_PUBLICIZE_END";                   // 公示结束日期（验收报告日期止）
    public const string TEMP_APPLY_STATEMENT = "TEMP_APPLY_STATEMENT";               // 申请救助情况说明（纯叙述，不含成员状态追加段；入户调查表专用）
    public const string TEMP_POLICY_CHECKLIST = "TEMP_POLICY_CHECKLIST";               // 享受政策清单（□/√ 勾选格式；入户调查表专用）
    public const string TEMP_ACCEPTANCE_REASON = "TEMP_ACCEPTANCE_REASON";             // 临时救助原因（叙述+费用明细；验收报告专用）
    public const string TEMP_ACCEPTANCE_CONCLUSION = "TEMP_ACCEPTANCE_CONCLUSION";     // 验收结论整句（因病因学…，于…公示，…验收通过）
    public const string TEMP_INVESTIGATION_DATE = "TEMP_INVESTIGATION_DATE";           // 入户调查时间（动态：min(今日最近工作日, 公示开始前一工作日)；入户调查表/审核审批表用）
    public const string TEMP_TOWN_OPINION_DATE = "TEMP_TOWN_OPINION_DATE";             // 街道（乡镇）意见日期（=公示结束日；审核审批表用）
    public const string TEMP_DIFFICULTY_SUMMARY = "TEMP_DIFFICULTY_SUMMARY";           // 困难情况摘要（概括句，不含疾病名称/编码/费用；点名患病人；入户调查表+信息公示用）
    public const string TEMP_AUDIT_REASON = "TEMP_AUDIT_REASON";                       // 申请救助原因（精简句；审核审批表专用）
    public const string TEMP_BENEFICIARY_NAME = "TEMP_BENEFICIARY_NAME";               // 救助对象姓名（家庭成员中选定；空时兜底回退申请人）
    public const string TEMP_BENEFICIARY_ID_CARD = "TEMP_BENEFICIARY_ID_CARD";         // 救助对象身份证号
    public const string TEMP_BENEFICIARY_GENDER = "TEMP_BENEFICIARY_GENDER";           // 救助对象性别
    public const string TEMP_BENEFICIARY_AGE = "TEMP_BENEFICIARY_AGE";                 // 救助对象年龄
    public const string TEMP_BENEFICIARY_RELATION = "TEMP_BENEFICIARY_RELATION";       // 救助对象与户主关系

    // ── 月报_临时救助新增汇总表 ──
    public const string TR_REPORT_MONTH = "TR_REPORT_MONTH";                       // 申报月份（标题前缀）
    public const string TR_SEQ = "TR_SEQ";                                         // 序号（索引槽位：_1.._10）
    public const string TR_NAME = "TR_NAME";                                       // 姓名（申请人）
    public const string TR_ID_CARD = "TR_ID_CARD";                                 // 身份证号
    public const string TR_AGE = "TR_AGE";                                         // 年龄
    public const string TR_GENDER = "TR_GENDER";                                   // 性别
    public const string TR_FAMILY_SIZE = "TR_FAMILY_SIZE";                         // 家庭人口
    public const string TR_ADDRESS = "TR_ADDRESS";                                 // 家庭住址
    public const string TR_CATEGORY = "TR_CATEGORY";                               // 最低生活保障分类（个人类别）
    public const string TR_PHONE = "TR_PHONE";                                     // 联系方式
    public const string TR_AMOUNT = "TR_AMOUNT";                                   // 救助金额（小额=确定金额；大额留空）
    public const string TR_AUDIT_DATE = "TR_AUDIT_DATE";                           // 审批时间（小额=确认时间；大额留空）
    public const string TR_REASON = "TR_REASON";                                   // 申请理由（精简句）
    public const string TR_SELF_PAY = "TR_SELF_PAY";                               // 自付金额合计
    public const string TR_BANK_ACCOUNT = "TR_BANK_ACCOUNT";                       // 一卡通账号（最低生活保障申请 bank_account）

    // 家庭成员索引槽位（大额/小额审批表，1..5 行）
    public const string TEMP_MEMBER_NAME = "TEMP_MEMBER_NAME";                       // 家庭成员姓名（索引槽位：_1.._5）
    public const string TEMP_MEMBER_GENDER = "TEMP_MEMBER_GENDER";                   // 家庭成员性别
    public const string TEMP_MEMBER_RELATION = "TEMP_MEMBER_RELATION";               // 与户主关系
    public const string TEMP_MEMBER_ID_CARD = "TEMP_MEMBER_ID_CARD";                 // 家庭成员身份证号
    public const string TEMP_MEMBER_WORK_UNIT = "TEMP_MEMBER_WORK_UNIT";             // 家庭成员工作单位
    public const string TEMP_MEMBER_ANNUAL_INCOME = "TEMP_MEMBER_ANNUAL_INCOME";     // 家庭成员年收入

    // 审核审批表（原大额/小额审批表合并）与验收报告2 专用
    public const string TEMP_FAMILY_CATEGORY_CHECKLIST = "TEMP_FAMILY_CATEGORY_CHECKLIST"; // 户主家庭类别勾选清单（□/☑ 9 项固定映射）
    public const string TEMP_ACCEPTANCE_DATE = "TEMP_ACCEPTANCE_DATE";               // 验收时间（公示结束日期，空则打印当天）
    public const string TEMP_PARENT_UNIT = "TEMP_PARENT_UNIT";                       // 父级单位（大额→县民政部门意见，小额→镇政府意见）

    // ── 近亲属备案（批量备案信息/汇总表/关联信息表/救助对象信息表） ──
    // 工作人员字段（单值）
    public const string NEAR_RELATIVE_STAFF_NAME = "NEAR_RELATIVE_STAFF_NAME";       // 工作人员姓名（近亲属姓名）
    public const string NEAR_RELATIVE_STAFF_ID_CARD = "NEAR_RELATIVE_STAFF_ID_CARD"; // 工作人员身份证号
    public const string NEAR_RELATIVE_STAFF_PHONE = "NEAR_RELATIVE_STAFF_PHONE";     // 工作人员联系方式
    public const string NEAR_RELATIVE_WORK_UNIT = "NEAR_RELATIVE_WORK_UNIT";         // 工作单位
    public const string NEAR_RELATIVE_POSITION = "NEAR_RELATIVE_POSITION";           // 职务（职级）
    // 汇总表公共字段
    public const string NEAR_RELATIVE_REPORT_DATE = "NEAR_RELATIVE_REPORT_DATE";     // 填报日期
    public const string NEAR_RELATIVE_REPORT_UNIT = "NEAR_RELATIVE_REPORT_UNIT";     // 填报单位
    // 公共时间（审核确认时间）
    public const string NEAR_RELATIVE_AUDIT_TIME = "NEAR_RELATIVE_AUDIT_TIME";       // 审核确认时间/填报时间

    // 备案对象行字段（索引槽位：_1.._N，批量备案每页5行、汇总每页9组）
    public const string NEAR_RELATIVE_RELATION = "NEAR_RELATIVE_RELATION";           // 救助对象与工作人员关系（对象关系）
    public const string NEAR_RELATIVE_NAME = "NEAR_RELATIVE_NAME";                   // 救助对象姓名
    public const string NEAR_RELATIVE_ID_CARD = "NEAR_RELATIVE_ID_CARD";             // 救助对象身份证号
    public const string NEAR_RELATIVE_GENDER = "NEAR_RELATIVE_GENDER";               // 性别
    public const string NEAR_RELATIVE_BIRTH_DATE = "NEAR_RELATIVE_BIRTH_DATE";       // 出生年月
    public const string NEAR_RELATIVE_HUKOU_ADDRESS = "NEAR_RELATIVE_HUKOU_ADDRESS"; // 户籍地址
    public const string NEAR_RELATIVE_RESIDENCE_ADDRESS = "NEAR_RELATIVE_RESIDENCE_ADDRESS"; // 居住地址
    public const string NEAR_RELATIVE_FAMILY_ADDRESS = "NEAR_RELATIVE_FAMILY_ADDRESS";     // 家庭住址
    public const string NEAR_RELATIVE_FAMILY_SIZE = "NEAR_RELATIVE_FAMILY_SIZE";     // 家庭人口/共同生活人数
    public const string NEAR_RELATIVE_HELP_TYPE = "NEAR_RELATIVE_HELP_TYPE";         // 社会救助种类/保障类别
    public const string NEAR_RELATIVE_MONTH_AMOUNT = "NEAR_RELATIVE_MONTH_AMOUNT";   // 月保障金
    public const string NEAR_RELATIVE_REPORT_AMOUNT = "NEAR_RELATIVE_REPORT_AMOUNT"; // 报账金额
    public const string NEAR_RELATIVE_START_TIME = "NEAR_RELATIVE_START_TIME";       // 救助起始时间
    public const string NEAR_RELATIVE_FAMILY_RELATION = "NEAR_RELATIVE_FAMILY_RELATION"; // 家庭关系
    public const string NEAR_RELATIVE_APPLY_REASON = "NEAR_RELATIVE_APPLY_REASON";   // 申请原因
    public const string NEAR_RELATIVE_APPLY_TIME = "NEAR_RELATIVE_APPLY_TIME";       // 申请时间
    public const string NEAR_RELATIVE_FAMILY_DIFFICULTY = "NEAR_RELATIVE_FAMILY_DIFFICULTY"; // 家庭主要困难
    public const string NEAR_RELATIVE_ECONOMY_INVESTIGATE = "NEAR_RELATIVE_ECONOMY_INVESTIGATE"; // 家庭经济情况调查情况
    public const string NEAR_RELATIVE_TOWN_OPINION = "NEAR_RELATIVE_TOWN_OPINION";   // 乡镇（街道）意见
    public const string NEAR_RELATIVE_DYNAMIC_RECORD = "NEAR_RELATIVE_DYNAMIC_RECORD"; // 动态管理记录

    // 批量备案信息：对象行索引槽位（模板固定 5 行/页，前缀 _1.._5）
    public const string NEAR_RELATIVE_ROW_RELATION = "NEAR_RELATIVE_ROW_RELATION";           // {关系N}
    public const string NEAR_RELATIVE_ROW_NAME = "NEAR_RELATIVE_ROW_NAME";                   // {姓名N}
    public const string NEAR_RELATIVE_ROW_ID_CARD = "NEAR_RELATIVE_ROW_ID_CARD";             // {身份证号N}
    public const string NEAR_RELATIVE_ROW_HUKOU = "NEAR_RELATIVE_ROW_HUKOU";                 // {户籍地址N}
    public const string NEAR_RELATIVE_ROW_RESIDENCE = "NEAR_RELATIVE_ROW_RESIDENCE";         // {居住地址N}
    public const string NEAR_RELATIVE_ROW_HELP_TYPE = "NEAR_RELATIVE_ROW_HELP_TYPE";         // {社会救助类型N}
    public const string NEAR_RELATIVE_ROW_MONTH_AMOUNT = "NEAR_RELATIVE_ROW_MONTH_AMOUNT";   // {月保障金N}

    // 汇总表：行索引槽位（模板固定 9 组/页，前缀 _1.._9）
    public const string NEAR_RELATIVE_SUMMARY_STAFF_NAME = "NEAR_RELATIVE_SUMMARY_STAFF_NAME";       // 工作人员姓名 {姓名N}
    public const string NEAR_RELATIVE_SUMMARY_WORK_UNIT = "NEAR_RELATIVE_SUMMARY_WORK_UNIT";         // 工作单位 {工作单位N}
    public const string NEAR_RELATIVE_SUMMARY_POSITION = "NEAR_RELATIVE_SUMMARY_POSITION";           // 职务职级 {职务职级N}
    public const string NEAR_RELATIVE_SUMMARY_RELATION = "NEAR_RELATIVE_SUMMARY_RELATION";           // 对象关系 {对象关系N}
    public const string NEAR_RELATIVE_SUMMARY_NAME = "NEAR_RELATIVE_SUMMARY_NAME";                   // 救助姓名 {救助姓名N}
    public const string NEAR_RELATIVE_SUMMARY_ID_CARD = "NEAR_RELATIVE_SUMMARY_ID_CARD";             // 救助身份证号 {救助身份证号N}
    public const string NEAR_RELATIVE_SUMMARY_HUKOU = "NEAR_RELATIVE_SUMMARY_HUKOU";                 // 救助户籍地址 {救助户籍地址N}
    public const string NEAR_RELATIVE_SUMMARY_HELP_TYPE = "NEAR_RELATIVE_SUMMARY_HELP_TYPE";         // 救助保障类别 {救助保障类别N}
    public const string NEAR_RELATIVE_SUMMARY_FAMILY_SIZE = "NEAR_RELATIVE_SUMMARY_FAMILY_SIZE";     // 救助共同生活人数 {救助共同生活人数N}
    public const string NEAR_RELATIVE_SUMMARY_REPORT_AMOUNT = "NEAR_RELATIVE_SUMMARY_REPORT_AMOUNT"; // 救助报账金额 {救助报账金额N}

    // 档案门控标记：该申请存在关联备案（装配时置 "1"，档案输出页据此显隐近亲属模板）
    public const string NEAR_RELATIVE_HAS_DATA = "NEAR_RELATIVE_HAS_DATA";

    // ── 低保证明文件（档案查询页证明打印专用） ──
    public const string RECEIVE_UNIT_NAME = "RECEIVE_UNIT_NAME";                        // 接收单位名称
    public const string PROOF_CLASSIFICATION_RESULT = "PROOF_CLASSIFICATION_RESULT";    // 分类认定结果（证明专用，避免模板16冲突）
    public const string PROOF_GUARANTEE_AMOUNT = "PROOF_GUARANTEE_AMOUNT";              // 保障金额（证明专用）
    public const string PROOF_ENJOY_START_DATE = "PROOF_ENJOY_START_DATE";              // 享受起始日期
    public const string PROOF_ISSUE_DATE = "PROOF_ISSUE_DATE";                          // 开具日期

    // ── 后补追缴（档案_追缴资金办理单） ── 值为模板实际中文占位符（花括号内的部分）
    public const string RECOVERY_UNIT = "追缴单位";                       // 填报单位
    public const string RECOVERY_CODE = "追缴编码";                       // 编号（自动生成 YYYYMM-NNN）
    public const string RECOVERY_SEQ = "编号";                            // 行序号（单行模板固定 "1"）
    public const string RECOVERY_PERSON_NAME = "户主姓名";                 // 被追缴人姓名
    public const string RECOVERY_ID_CARD = "户主身份证号";                 // 身份证号码
    public const string RECOVERY_SOURCE_TYPE = "户主享受分类";             // 追缴类别
    public const string RECOVERY_AMOUNT = "应追缴金额";                    // 应缴款金额（元）
    public const string RECOVERED_AMOUNT = "实追缴金额";                   // 已缴款金额（元）
    public const string RECOVERY_REPORT_DATE = "上报时间";                 // 缴入民政局时间
    public const string RECOVERY_RANGE = "追缴起止时间";                   // 追缴资金起止年度

    // ── 渐退期审批表（档案_渐退期审批表） ── 值为模板实际中文占位符
    public const string GP_HEAD_NAME = "新户主姓名";                        // 新户主姓名
    public const string GP_HEAD_GENDER = "新户主性别";                      // 新户主性别
    public const string GP_HEAD_ID_CARD = "新户主身份证号";                  // 新户主身份证号
    public const string GP_HEAD_AGE = "新户主年龄";                         // 新户主年龄
    public const string GP_FAMILY_SIZE = "新家庭人口";                      // 新家庭人口
    public const string GP_CLASSIFICATION = "新享受类别";                    // 新享受类别
    public const string GP_ADDRESS = "新家庭住址";                          // 新家庭住址
    public const string GP_PHONE = "新电话号码";                            // 新电话号码
    public const string GP_CHANGE_DETAIL = "详细说明享受对象家庭人员、收入、财产状况变动情况"; // 变动情况说明
    public const string GP_PERIOD_MONTHS = "渐退期长度";                    // 渐退期月数
    public const string GP_START = "渐退期起";                              // 渐退期开始日期
    public const string GP_END = "渐退期止";                                // 渐退期结束日期
    public const string GP_PERIOD_RANGE = "渐退期启止时间";                  // 起止组合（yyyy年M月d日至yyyy年M月d日）
    public const string GP_EXIT_SITUATION = "退出渐退期情况";                // 退出说明（分型拼句，未退出留空）
    public const string GP_AUDIT_TIME = "审核确认时间";                    // 审核确认时间

    // ── 档案_保障金减少（渐退超限封顶减发专用） ── 值为模板实际中文占位符
    public const string GR_HEAD_NAME = "户主姓名";                        // 户主姓名
    public const string GR_ID_CARD = "身份证号";                          // 身份证号
    public const string GR_FAMILY_SIZE = "家庭人口";                      // 家庭人口
    public const string GR_OLD_AMOUNT = "原保障金额";                     // 渐退前原月保障金
    public const string GR_NEW_AMOUNT = "现保障金额";                     // 渐退期内封顶后月保障金
    public const string GR_DECREASE_AMOUNT = "减发金额";                  // 原-现
    public const string GR_CAP_BASIS = "封顶依据";                        // 户口类型标准×人数
    public const string GR_GRACE_START = "渐退期起";                      // 渐退开始
    public const string GR_GRACE_END = "渐退期止";                        // 渐退结束
    public const string GR_DEATH_DATE = "死亡日期";                       // 原户主死亡日期（可空）
    public const string GR_REASON = "减发原因";                          // 减发原因说明
    public const string GR_AUDIT_TIME = "审核确认时间";                   // 审核确认时间

    // ── 增减员调整表（档案_增员减员调整表） ──
    // 约定同下方 658 行：常量值 = ASCII 字段键（= config_json 的 fieldKey），
    // 模板中的中文花括号占位符由 config_json 的 placeholder 负责替换。
    // （2026-09-30 由中文值迁移：原值与 config 的 fieldKey 不一致导致 57 项映射全部失配）
    public const string ADJ_FILL_UNIT = "ADJ_FILL_UNIT";                      // 填报单位（config 由 OPERATOR_UNIT 兜底，本键未映射）
    public const string ADJ_HEAD_NAME = "ADJ_HEAD_NAME";                      // 户主姓名
    public const string ADJ_HEAD_GENDER = "ADJ_HEAD_GENDER";                  // 户主性别
    public const string ADJ_HEAD_BIRTH = "ADJ_HEAD_BIRTH";                    // 户主出生日期
    public const string ADJ_HEAD_NATION = "ADJ_HEAD_NATION";                  // 户主民族
    public const string ADJ_HEAD_FAMILY_SIZE = "ADJ_HEAD_FAMILY_SIZE";        // 户主家庭人口
    public const string ADJ_HEAD_FAMILY_TYPE = "ADJ_HEAD_FAMILY_TYPE";        // 户主家庭类型（xlsx 无对应占位符，未映射）
    public const string ADJ_HEAD_ADDRESS = "ADJ_HEAD_ADDRESS";                // 户主家庭居住地址
    public const string ADJ_HEAD_CLASSIFICATION = "ADJ_HEAD_CLASSIFICATION";  // 户主享受类别（config 由 COVER_CLASSIFICATION 取全称，本键未映射）
    public const string ADJ_HEAD_HUKOU = "ADJ_HEAD_HUKOU";                    // 户主户籍所在地
    public const string ADJ_HEAD_ID_CARD = "ADJ_HEAD_ID_CARD";                // 户主身份证号码

    // 增员信息（最多 3 人）
    public const string ADJ_ADD_NAME_1 = "ADJ_ADD_NAME_1";
    public const string ADJ_ADD_GENDER_1 = "ADJ_ADD_GENDER_1";
    public const string ADJ_ADD_ID_CARD_1 = "ADJ_ADD_ID_CARD_1";
    public const string ADJ_ADD_RELATION_1 = "ADJ_ADD_RELATION_1";
    public const string ADJ_ADD_HEALTH_1 = "ADJ_ADD_HEALTH_1";
    public const string ADJ_ADD_WORKPLACE_1 = "ADJ_ADD_WORKPLACE_1";
    public const string ADJ_ADD_REASON_1 = "ADJ_ADD_REASON_1";
    public const string ADJ_ADD_INCOME_1 = "ADJ_ADD_INCOME_1";
    public const string ADJ_ADD_NAME_2 = "ADJ_ADD_NAME_2";
    public const string ADJ_ADD_GENDER_2 = "ADJ_ADD_GENDER_2";
    public const string ADJ_ADD_ID_CARD_2 = "ADJ_ADD_ID_CARD_2";
    public const string ADJ_ADD_RELATION_2 = "ADJ_ADD_RELATION_2";
    public const string ADJ_ADD_HEALTH_2 = "ADJ_ADD_HEALTH_2";
    public const string ADJ_ADD_WORKPLACE_2 = "ADJ_ADD_WORKPLACE_2";
    public const string ADJ_ADD_REASON_2 = "ADJ_ADD_REASON_2";
    public const string ADJ_ADD_INCOME_2 = "ADJ_ADD_INCOME_2";
    public const string ADJ_ADD_NAME_3 = "ADJ_ADD_NAME_3";
    public const string ADJ_ADD_GENDER_3 = "ADJ_ADD_GENDER_3";
    public const string ADJ_ADD_ID_CARD_3 = "ADJ_ADD_ID_CARD_3";
    public const string ADJ_ADD_RELATION_3 = "ADJ_ADD_RELATION_3";
    public const string ADJ_ADD_HEALTH_3 = "ADJ_ADD_HEALTH_3";
    public const string ADJ_ADD_WORKPLACE_3 = "ADJ_ADD_WORKPLACE_3";
    public const string ADJ_ADD_REASON_3 = "ADJ_ADD_REASON_3";
    public const string ADJ_ADD_INCOME_3 = "ADJ_ADD_INCOME_3";

    // 减员信息（最多 3 人）
    public const string ADJ_REMOVE_NAME_1 = "ADJ_REMOVE_NAME_1";
    public const string ADJ_REMOVE_GENDER_1 = "ADJ_REMOVE_GENDER_1";
    public const string ADJ_REMOVE_ID_CARD_1 = "ADJ_REMOVE_ID_CARD_1";
    public const string ADJ_REMOVE_RELATION_1 = "ADJ_REMOVE_RELATION_1";
    public const string ADJ_REMOVE_HEALTH_1 = "ADJ_REMOVE_HEALTH_1";
    public const string ADJ_REMOVE_WORKPLACE_1 = "ADJ_REMOVE_WORKPLACE_1";
    public const string ADJ_REMOVE_REASON_1 = "ADJ_REMOVE_REASON_1";
    public const string ADJ_REMOVE_INCOME_1 = "ADJ_REMOVE_INCOME_1";
    public const string ADJ_REMOVE_NAME_2 = "ADJ_REMOVE_NAME_2";
    public const string ADJ_REMOVE_GENDER_2 = "ADJ_REMOVE_GENDER_2";
    public const string ADJ_REMOVE_ID_CARD_2 = "ADJ_REMOVE_ID_CARD_2";
    public const string ADJ_REMOVE_RELATION_2 = "ADJ_REMOVE_RELATION_2";
    public const string ADJ_REMOVE_HEALTH_2 = "ADJ_REMOVE_HEALTH_2";
    public const string ADJ_REMOVE_WORKPLACE_2 = "ADJ_REMOVE_WORKPLACE_2";
    public const string ADJ_REMOVE_REASON_2 = "ADJ_REMOVE_REASON_2";
    public const string ADJ_REMOVE_INCOME_2 = "ADJ_REMOVE_INCOME_2";
    public const string ADJ_REMOVE_NAME_3 = "ADJ_REMOVE_NAME_3";
    public const string ADJ_REMOVE_GENDER_3 = "ADJ_REMOVE_GENDER_3";
    public const string ADJ_REMOVE_ID_CARD_3 = "ADJ_REMOVE_ID_CARD_3";
    public const string ADJ_REMOVE_RELATION_3 = "ADJ_REMOVE_RELATION_3";
    public const string ADJ_REMOVE_HEALTH_3 = "ADJ_REMOVE_HEALTH_3";
    public const string ADJ_REMOVE_WORKPLACE_3 = "ADJ_REMOVE_WORKPLACE_3";
    public const string ADJ_REMOVE_REASON_3 = "ADJ_REMOVE_REASON_3";
    public const string ADJ_REMOVE_INCOME_3 = "ADJ_REMOVE_INCOME_3";

    // ── 社会救助对象动态管理记录（变更人群档案，模板_社会救助对象动态管理记录） ──
    // 约定与其他模板一致：常量值为 ASCII 字段键（= config_json 的 fieldKey），
    // 模板中的中文花括号占位符由 config_json 的 placeholder（fieldKey↔placeholder 映射）负责替换。
    public const string DM_CURRENT_UNIT = "DM_CURRENT_UNIT";                  // 填报单位（当前登录用户所在机构名）
    public const string DM_REPORT_YEAR = "DM_REPORT_YEAR";                    // 年度管理档案年份（纯数字）
    public const string DM_CURRENT_USER = "DM_CURRENT_USER";                  // 调查人
    public const string DM_CURRENT_DATE = "DM_CURRENT_DATE";                  // 公章落款日期 + 调查时间（共用）

    public const string DM_HEAD_NAME = "DM_HEAD_NAME";                        // 户主姓名
    public const string DM_FAMILY_SIZE = "DM_FAMILY_SIZE";                    // 共同生活成员计数（剔除 Support 类别）
    public const string DM_HEAD_HUKOU = "DM_HEAD_HUKOU";                      // 户籍地址
    public const string DM_HEAD_RESIDENCE = "DM_HEAD_RESIDENCE";              // 镇+村+门牌号
    public const string DM_CATEGORY = "DM_CATEGORY";                          // 分类全称
    public const string DM_CYCLE = "DM_CYCLE";                                // 固定"一年期"

    // 赡养（扶、抚）义务人 3 槽位（按义务人顺序取前 3 名，空槽留空）
    public const string DM_SUPPORTER_NAME_1 = "DM_SUPPORTER_NAME_1";
    public const string DM_SUPPORTER_RELATION_1 = "DM_SUPPORTER_RELATION_1";
    public const string DM_SUPPORTER_HEALTH_1 = "DM_SUPPORTER_HEALTH_1";
    public const string DM_SUPPORTER_OCCUPATION_1 = "DM_SUPPORTER_OCCUPATION_1";
    public const string DM_SUPPORTER_FEE_1 = "DM_SUPPORTER_FEE_1";
    public const string DM_SUPPORTER_NAME_2 = "DM_SUPPORTER_NAME_2";
    public const string DM_SUPPORTER_RELATION_2 = "DM_SUPPORTER_RELATION_2";
    public const string DM_SUPPORTER_HEALTH_2 = "DM_SUPPORTER_HEALTH_2";
    public const string DM_SUPPORTER_OCCUPATION_2 = "DM_SUPPORTER_OCCUPATION_2";
    public const string DM_SUPPORTER_FEE_2 = "DM_SUPPORTER_FEE_2";
    public const string DM_SUPPORTER_NAME_3 = "DM_SUPPORTER_NAME_3";
    public const string DM_SUPPORTER_RELATION_3 = "DM_SUPPORTER_RELATION_3";
    public const string DM_SUPPORTER_HEALTH_3 = "DM_SUPPORTER_HEALTH_3";
    public const string DM_SUPPORTER_OCCUPATION_3 = "DM_SUPPORTER_OCCUPATION_3";
    public const string DM_SUPPORTER_FEE_3 = "DM_SUPPORTER_FEE_3";

    // 家庭经济状况（年值口径；收入=主表年总收入，净收入=收入−刚性年值−就业成本）
    public const string DM_FAMILY_INCOME = "DM_FAMILY_INCOME";                // 家庭收入（年）
    public const string DM_RIGID_EXPENDITURE = "DM_RIGID_EXPENDITURE";        // 月值×12 还原年值
    public const string DM_EMPLOYMENT_COST = "DM_EMPLOYMENT_COST";            // nc_biz_employment_costs 年值汇总，空表"-"
    public const string DM_NET_INCOME = "DM_NET_INCOME";                      // 收入−刚性−就业成本
    public const string DM_PER_CAPITA_INCOME = "DM_PER_CAPITA_INCOME";        // 主表年人均（权威口径）
    public const string DM_MONEY_ASSET_TOTAL = "DM_MONEY_ASSET_TOTAL";        // 现金+存款+证券+商业保险
    public const string DM_MONEY_ASSET_PER_CAPITA = "DM_MONEY_ASSET_PER_CAPITA"; // 总额÷人数（一次舍入）
    public const string DM_REAL_ESTATE = "DM_REAL_ESTATE";                    // 房产聚合文本，无→"无"
    public const string DM_VEHICLES = "DM_VEHICLES";                          // 车辆聚合文本，无→"无"
    public const string DM_MACHINERY = "DM_MACHINERY";                        // 农机具聚合文本，无→"无"

    public const string DM_NEAR_RELATIVE = "DM_NEAR_RELATIVE";                // 近亲属备案关联描述，无→"无"
    public const string DM_OTHER_SITUATION = "DM_OTHER_SITUATION";            // 手写栏，留空
    public const string DM_TOWN_OPINION = "DM_TOWN_OPINION";                  // 入户调查结论

    // 增减员调整表底部公共字段（ASCII 键，约定同 658 行）
    public const string ADJ_FAMILY_DETAIL = "ADJ_FAMILY_DETAIL";              // 镇政府意见/家庭具体情况
    public const string ADJ_CHANGE_SUMMARY = "ADJ_CHANGE_SUMMARY";            // 增减摘要（xlsx 由{增加人数}{减少人数}承担，本键未映射）
    public const string ADJ_AUDIT_TIME = "ADJ_AUDIT_TIME";                    // 审批时间
    public const string ADJ_INCREASE_AMOUNT = "ADJ_INCREASE_AMOUNT";          // 增发金额
    public const string ADJ_DECREASE_AMOUNT = "ADJ_DECREASE_AMOUNT";          // 减发金额
    public const string ADJ_EFFECTIVE_DATE = "ADJ_EFFECTIVE_DATE";            // 执行时间

    // ===== 档案_增员减员调整表 / 档案_定期复核审批表（2026-09 新增；ASCII 键，中文占位符由 config placeholder 映射） =====
    // 增员减员调整表（户籍类型复用既有 HUKOU_TYPE_DISPLAY 字面量键）
    public const string ADJ_ADD_COUNT = "ADJ_ADD_COUNT";                              // 增加人数
    public const string ADJ_REMOVE_COUNT = "ADJ_REMOVE_COUNT";                        // 减少人数

    // 定期复核审批表
    public const string REVIEW_METHOD = "REVIEW_METHOD";                                // 复核办法
    public const string REVIEW_SITUATION = "REVIEW_SITUATION";                          // 定期复核情况（最近一次经济复核原因）
    public const string REVIEW_TIME = "REVIEW_TIME";                                    // 复核时间
    public const string REVIEW_CLASSIFICATION_CHANGE = "REVIEW_CLASSIFICATION_CHANGE";  // 待遇变化分类（保持/增发/减发/停发）
    public const string REVIEW_AMOUNT_CHANGE = "REVIEW_AMOUNT_CHANGE";                  // 决定句句尾（2026-10 起金额句已移至{定期复核情况}，此处仅类别变化：，享受类别由X调整为Y / ，类别认定为Y）
    public const string REVIEW_EFFECTIVE_DATE = "REVIEW_EFFECTIVE_DATE";                // 审核次月日期（M月d日，仅本表）
    public const string CLASSIFICATION_MAJOR = "CLASSIFICATION_MAJOR";                  // 享受类别缩写（低保/低保边缘/特困/刚性支出）
    public const string ARCHIVE_CLASS = "ARCHIVE_CLASS";                                // 档案分类 A类/B类
    public const string LAND_INCOME_SITUATION_V2 = "LAND_INCOME_SITUATION_V2";          // 土地收入情况（无"本人申请"段）
    public const string RIGID_AND_EXEMPTION = "RIGID_AND_EXEMPTION";                    // 刚性支出财产豁免情况（两段拼接）
    public const string PROPERTY_FOREST_LAND = "PROPERTY_FOREST_LAND";                  // 林地信息
    public const string SIDEBUSINESS_TYPE = "SIDEBUSINESS_TYPE";                        // 副业种类
    public const string SIDEBUSINESS_COUNT = "SIDEBUSINESS_COUNT";                      // 副业数量
    public const string INCOME_PROPERTY_DESC = "INCOME_PROPERTY_DESC";                  // 财产性收入明细叙述
}
