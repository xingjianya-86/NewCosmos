// ============================================================================
// Change（增减员调整表 + 社会救助对象动态管理记录 + 定期复核审批表）
// 由 Constants\FieldKeys.cs 于 2026-10-16 按域拆分（纯组织改动：常量名与常量值一律未变）。
// 新增字段键纪律：常量加到对应域文件，同时 (1) Builder 供给点赋值 (2) 落本白名单
// (3) 禁中文键，值必须匹配 ^[A-Z0-9_]+$ 且与 config_json 的 fieldKey 一致。
// ============================================================================

namespace NewCosmos.Constants;

public static partial class FieldKeys
{
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
