// ============================================================================
// Common（通用：申请人/家庭/成员/组织/入户调查/收入财产/公示/模板专用/档案目录封面告知书/特困/低保证明）
// 由 Constants\FieldKeys.cs 于 2026-10-16 按域拆分（纯组织改动：常量名与常量值一律未变）。
// 新增字段键纪律：常量加到对应域文件，同时 (1) Builder 供给点赋值 (2) 落本白名单
// (3) 禁中文键，值必须匹配 ^[A-Z0-9_]+$ 且与 config_json 的 fieldKey 一致。
// ============================================================================

namespace NewCosmos.Constants;

public static partial class FieldKeys
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
    public const string OPERATOR_ID_CARD = "OPERATOR_ID_CARD";                      // 经办人身份证号（登录用户 IdentityCard）
    public const string OPERATOR_TITLE = "OPERATOR_TITLE";                          // 经办人职务（登录用户 Position）
    public const string REPORT_DATE = "REPORT_DATE";
    public const string CIVIL_ASSISTANT_NAME = "CIVIL_ASSISTANT_NAME";

    // 签名（图片占位符，非文本字段——由 PrintExecuteService 取回签名 PNG 后经 ReplaceImageFields 内嵌）
    public const string SIGNATURE_APPLICANT = "SIGNATURE_APPLICANT";               // 申请人本人签名
    public const string SIGNATURE_AGENT = "SIGNATURE_AGENT";                       // 代理人签名
    public const string SIGNATURE_OPERATOR = "SIGNATURE_OPERATOR";                 // 经办人签名

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
    // 批5 net-new（2026-10-16）：近亲属域/变更域/临救域目标键合并落点（此前代码零常量/零供给，
    // 含 config 既存孤儿读者 REPORT_UNIT、APPLICANT_AGE；需先建常量再双写）
    public const string RESIDENCE_ADDRESS = "RESIDENCE_ADDRESS";                    // 居住地
    public const string TOWN_OPINION = "TOWN_OPINION";                              // 乡镇（街道）意见
    public const string REPORT_UNIT = "REPORT_UNIT";                                // 填报单位
    public const string APPLICANT_AGE = "APPLICANT_AGE";                            // 申请人年龄

    // 批D1a 孤儿转正（2026-10-16）：以下 30 键原为「config 有行、代码仅裸串供给」（体检 Unknown 桶A），
    // 建常量纳入白名单；同日 ArchiveProductionViewModel 内裸串 ["X"] 全量改为 [FieldKeys.X]（同值重构，输出零变化）。
    // 照料服务协议族（T221/T222）
    public const string CAREGIVER_NAME = "CAREGIVER_NAME";                          // 照料服务人姓名
    public const string CAREGIVER_ID_CARD = "CAREGIVER_ID_CARD";                    // 照料服务人身份证号
    public const string CAREGIVER_RELATIONSHIP = "CAREGIVER_RELATIONSHIP";          // 监护人关系
    public const string CAREGIVER_PHONE = "CAREGIVER_PHONE";                        // 监护人联系电话
    public const string CAREGIVER_GENDER = "CAREGIVER_GENDER";                      // 丙方性别
    public const string CAREGIVER_AGE = "CAREGIVER_AGE";                            // 丙方年龄
    public const string CAREGIVER_ADDRESS = "CAREGIVER_ADDRESS";                    // 丙方/监护人家庭住址
    public const string CAREGIVER_WORK_UNIT = "CAREGIVER_WORK_UNIT";                // 丙方单位名称
    public const string CAREGIVER_SUBSIDY_AMOUNT = "CAREGIVER_SUBSIDY_AMOUNT";      // 照料护理费（纯数字）
    public const string CAREGIVER_SUBSIDY_UPPER = "CAREGIVER_SUBSIDY_UPPER";        // 照料护理费（中文大写）
    public const string CAREGIVER_ORG_LEGAL = "CAREGIVER_ORG_LEGAL";                // 照料单位法人
    public const string CAREGIVER_ORG_ADDRESS = "CAREGIVER_ORG_ADDRESS";            // 照料单位通讯地址
    public const string CAREGIVER_ORG_POSTAL = "CAREGIVER_ORG_POSTAL";              // 照料单位邮政编码
    public const string CAREGIVER_ORG_PHONE = "CAREGIVER_ORG_PHONE";                // 照料单位联系电话
    // 邻里访问/申请书/复核表收入财财产族（T162/T170/T224/T254）
    public const string INCOME_SOURCE = "INCOME_SOURCE";                            // 收入来源
    public const string INCOME_BREEDING_DESC = "INCOME_BREEDING_DESC";              // 生产/养殖收入
    public const string INCOME_BUSINESS_DESC = "INCOME_BUSINESS_DESC";              // 营业/经营收入
    public const string INCOME_LABOR_DESC = "INCOME_LABOR_DESC";                    // 劳务/务工收入
    public const string INCOME_SITUATION = "INCOME_SITUATION";                      // 经济收入状况
    public const string PROPERTY_FARM_EQUIPMENT = "PROPERTY_FARM_EQUIPMENT";        // 农机具
    public const string PROPERTY_HOUSE_DESC = "PROPERTY_HOUSE_DESC";                // 房屋情况
    public const string APPLICANT_LIFE_SITUATION = "APPLICANT_LIFE_SITUATION";      // 申请人生活情况
    public const string FAMILY_MEMBER_INFO = "FAMILY_MEMBER_INFO";                  // 户主及家庭成员
    // 户口性质显示（T219/T222/T255）
    public const string HUKOU_TYPE_DISPLAY = "HUKOU_TYPE_DISPLAY";                  // 户口类型/住房类别显示
    // 集中供养协议/照料协议期（T220/T221）
    public const string INSTITUTION_NAME = "INSTITUTION_NAME";                      // 供养机构名称
    public const string INSTITUTION_ADDRESS = "INSTITUTION_ADDRESS";                // 机构地址
    public const string INSTITUTION_PHONE = "INSTITUTION_PHONE";                    // 机构电话
    public const string SUPPORT_START_DATE = "SUPPORT_START_DATE";                  // 协议开始日期
    public const string SUPPORT_END_DATE = "SUPPORT_END_DATE";                      // 协议结束日期
    public const string AGREEMENT_PERIOD = "AGREEMENT_PERIOD";                      // 协议期限

    // 批D1b 孤儿转正（2026-10-16）：月报族 10 键（体检 Unknown 桶A 第二批）。
    // 供给在 PrintService / PrintService.PdfGeneration 的裸串，同日转 FieldKeys 引用（同值，输出零变化）。
    public const string REPORT_TITLE = "REPORT_TITLE";                              // 月报/明细表报表标题
    public const string MEETING_TIME = "MEETING_TIME";                              // 会议时间
    public const string HOST = "HOST";                                              // 主持人
    public const string RECORDER = "RECORDER";                                      // 记录人
    public const string ATTENDEES = "ATTENDEES";                                    // 出席人员
    public const string ABSENTEES = "ABSENTEES";                                    // 缺席人员
    public const string MEMBER_COUNT = "MEMBER_COUNT";                              // 人员数量
    public const string RESOLUTION_NAMES = "RESOLUTION_NAMES";                      // 人员名单
    public const string APPLY_CATEGORY = "APPLY_CATEGORY";                          // 申请分类
    public const string NEW_MEMBER_INFO = "NEW_MEMBER_INFO";                        // 新增成员信息

    // 批D1c 孤儿转正（2026-10-16）：T3 档案_咨询登记台账回访三键——**仅建常量、无任何供给**（回访业务代码
    // 全库零命中），打印值保持现状空串（转正前后输出零变化）。"回访功能缺失"为独立业务待办：
    // 补功能=录入回访记录+供给；删列=须连同模板文档占位符一起删（只删 config 行会让 {回访情况} 等
    // 占位符原文残留在打印件上，比空值更差——故当前保留 config 行）。
    public const string VISIT_DATE = "VISIT_DATE";                                  // 回访时间（无供给）
    public const string VISIT_OPERATOR = "VISIT_OPERATOR";                          // 回访经办人员（无供给）
    public const string VISIT_STATUS = "VISIT_STATUS";                              // 回访情况（无供给）
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
    public const string PROMISE_DATE = "PROMISE_DATE";                            // 诚信承诺落款日期（首次审批日，未审批=申请日期；已批档案钳下限公示开始日）

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
    // 公示起止合成（"{公示开始} 至 {公示结束}"整串，值已在代码内中文格式化，config format 留空）——
    // T182 档案_公示文件 {公示日期} 用；由 PUBLICITY_START_DATE/END_DATE 同源拼装，避免打印当天漂移。
    public const string PUBLICITY_RANGE = "PUBLICITY_RANGE";

    // 每月公示名单（模板：公共_每月公示名单）表头占位符
    // ── 公示直替换键（批D6 定性 2026-10-16，冻结）──
    // 值 = 占位符原文 / 残缺花括号行前缀（+N+"}" 拼整键），唯一消费方 PublicityOutputService.BuildPublicityFields
    // 构造键后 engine.ReplaceFields 直替换（不经 config fields 映射；config 若引用它们会被 NonAscii 正确拦截）。
    // 禁改值、禁 ASCII 化、禁写入 config fields——改值即破公示单打印。
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
    public const string DESTITUTE_SELF_CARE_ABILITY = "DESTITUTE_SELF_CARE_ABILITY"; // 自理能力（三档：自理/半自理/不能自理）
    public const string DESTITUTE_SELF_CARE_DETAIL = "DESTITUTE_SELF_CARE_DETAIL";  // 自理能力六项明细（逐项勾选式：进食☑能□不能；…）
    public const string DESTITUTE_CARE_LEVEL = "DESTITUTE_CARE_LEVEL";              // 照料等级

    // 照料服务协议（模板77）/ 分散供养协议（协议期按自然年，起=生成日、止=本年12月31日）
    public const string CARE_AGREEMENT_START_DATE = "CARE_AGREEMENT_START_DATE";     // 协议期起（生成日 yyyy-MM-dd）
    public const string CARE_AGREEMENT_END_DATE = "CARE_AGREEMENT_END_DATE";         // 协议期止（本年12月31日）
    public const string CAREGIVER_BANK_NAME = "CAREGIVER_BANK_NAME";                 // 分散照料人开户行
    public const string CAREGIVER_BANK_ACCOUNT = "CAREGIVER_BANK_ACCOUNT";           // 分散照料银行卡号
    public const string SCATTERED_AUDIT_TIME = "SCATTERED_AUDIT_TIME";               // 分散供养协议审核确认时间（业务审核确认日 yyyy年M月d日）

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

    // ── 低保证明文件（档案查询页证明打印专用） ──
    public const string RECEIVE_UNIT_NAME = "RECEIVE_UNIT_NAME";                        // 接收单位名称
    public const string PROOF_CLASSIFICATION_RESULT = "PROOF_CLASSIFICATION_RESULT";    // 分类认定结果（证明专用，避免模板16冲突）
    public const string PROOF_GUARANTEE_AMOUNT = "PROOF_GUARANTEE_AMOUNT";              // 保障金额（证明专用）
    public const string PROOF_ENJOY_START_DATE = "PROOF_ENJOY_START_DATE";              // 享受起始日期
    public const string PROOF_ISSUE_DATE = "PROOF_ISSUE_DATE";                          // 开具日期
}
