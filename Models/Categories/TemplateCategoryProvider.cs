using NewCosmos.Constants;
using NewCosmos.Models.Enums;

namespace NewCosmos.Models.Categories;

public static class TemplateCategoryProvider
{
    public static List<TemplateCategoryNode> BuildCategoryTree()
    {
        return new List<TemplateCategoryNode>
        {
            BuildLowIncomeGroup(),
            BuildTemporaryAssistanceGroup(),
            BuildElderlyBenefitsGroup(),
            BuildAssetVerificationGroup(),
            BuildMonthlyReportGroup()
        };
    }

    private static TemplateCategoryNode BuildMonthlyReportGroup()
    {
        return new TemplateCategoryNode
        {
            Label = "月报表",
            Path = "月报表",
            IsExpanded = true,
            Children =
            {
                new TemplateCategoryNode
                {
                    Label = "业务操作",
                    Path = "月报表/业务操作",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("新增救助明细_最低生活保障", "新增救助明细_最低生活保障"),
                        Leaf("新增救助明细_最低生活保障边缘家庭", "新增救助明细_最低生活保障边缘家庭"),
                        Leaf("新增救助明细_特困人员", "新增救助明细_特困人员"),
                        Leaf("新增救助明细_刚性支出困难家庭", "新增救助明细_刚性支出困难家庭"),
                        Leaf("停保汇总表_最低生活保障", "停保汇总表_最低生活保障"),
                        Leaf("停保汇总表_最低生活保障边缘家庭", "停保汇总表_最低生活保障边缘家庭"),
                        Leaf("停保汇总表_特困人员", "停保汇总表_特困人员"),
                        Leaf("停保汇总表_刚性支出困难家庭", "停保汇总表_刚性支出困难家庭"),
                        Leaf("保障金增发表_最低生活保障", "保障金增发表_最低生活保障"),
                        Leaf("保障金减发表_最低生活保障", "保障金减发表_最低生活保障"),
                        Leaf("施保金减发", "分类施保金减发人员表（最低生活保障）"),
                        Leaf("自然减员表", "人员变动_自然减员月报表"),
                        Leaf("档案_退出对象兜底情况纠治表", "退出对象兜底情况纠治表（批量）"),
                        Leaf("会议记录", "会议记录"),
                        Leaf("会议记录_一事一议", "会议记录_一事一议")
                    }
                }
            }
        };
    }

    private static TemplateCategoryNode BuildLowIncomeGroup()
    {
        return new TemplateCategoryNode
        {
            Label = "低收入人口",
            Path = "低收入人口",
            IsExpanded = true,
            Children =
            {
                BuildBenefitTypeDimension(),
                BuildOperationDimension()
            }
        };
    }

    private static TemplateCategoryNode BuildBenefitTypeDimension()
    {
        return new TemplateCategoryNode
        {
            Label = "保障类型",
            Path = "低收入人口/保障类型",
            IsExpanded = true,
            Children =
            {
                new TemplateCategoryNode
                {
                    Label = "最低生活保障",
                    Path = "低收入人口/保障类型/最低生活保障",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("最低生活保障对象", ClassificationConstants.RuralSubsistence),
                        Leaf("最低生活保障对象", ClassificationConstants.UrbanSubsistence),
                        Leaf("最低生活保障对象（单人）", ClassificationConstants.RuralLowIncomeSingle),
                        Leaf("最低生活保障对象（单人）", ClassificationConstants.UrbanLowIncomeSingle)
                    }
                },
                new TemplateCategoryNode
                {
                    Label = "最低生活保障边缘家庭",
                    Path = "低收入人口/保障类型/最低生活保障边缘家庭",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("最低生活保障边缘家庭", ClassificationConstants.RuralLowIncome),
                        Leaf("最低生活保障边缘家庭", ClassificationConstants.UrbanLowIncome)
                    }
                },
                new TemplateCategoryNode
                {
                    Label = "特困人员",
                    Path = "低收入人口/保障类型/特困人员",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("特困人员（集中供养）", ClassificationConstants.RuralDestituteCentralized),
                        Leaf("特困人员（集中供养）", ClassificationConstants.UrbanDestituteCentralized),
                        Leaf("特困人员（分散供养）", ClassificationConstants.RuralDestituteScattered),
                        Leaf("特困人员（分散供养）", ClassificationConstants.UrbanDestituteScattered)
                    }
                },
                new TemplateCategoryNode
                {
                    Label = "刚性支出困难家庭",
                    Path = "低收入人口/保障类型/刚性支出困难家庭",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("刚性支出困难家庭", ClassificationConstants.RuralRigidExpenditure),
                        Leaf("刚性支出困难家庭", ClassificationConstants.UrbanRigidExpenditure)
                    }
                }
            }
        };
    }

    private static TemplateCategoryNode BuildOperationDimension()
    {
        return new TemplateCategoryNode
        {
            Label = "业务操作",
            Path = "低收入人口/业务操作",
            IsExpanded = true,
            Children =
            {
                Leaf("新增档案", "新增"),
                new TemplateCategoryNode
                {
                    Label = "变更",
                    Path = "低收入人口/业务操作/变更",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("经济复核", "经济复核"),
                        Leaf("资金变更", "资金变更"),
                        Leaf("人员变更", "人员变更"),
                        Leaf("户主变更", "户主变更"),
                        Leaf("档案_保障金减少", "档案_保障金减少"),
                        Leaf("档案_增员减员调整表", "档案_增员减员调整表"),
                        Leaf("档案_渐退期审批表", "档案_渐退期审批表"),
                        Leaf("档案_变更告知书", "档案_变更告知书"),
                        Leaf("档案_定期复核审批表", "档案_定期复核审批表")
                    }
                },
                Leaf("停止档案", "停止"),
                Leaf("证明文件", "证明文件")
            }
        };
    }

    private static TemplateCategoryNode BuildTemporaryAssistanceGroup()
    {
        return new TemplateCategoryNode
        {
            Label = "临时救助",
            Path = "临时救助",
            IsExpanded = true,
            Children =
            {
                new TemplateCategoryNode
                {
                    Label = "业务操作",
                    Path = "临时救助/业务操作",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("大额救助", "临时救助/大额"),
                        Leaf("小额快速救助", "临时救助/小额")
                    }
                }
            }
        };
    }

    private static TemplateCategoryNode BuildElderlyBenefitsGroup()
    {
        return new TemplateCategoryNode
        {
            Label = "普惠高龄",
            Path = "普惠高龄",
            IsExpanded = true,
            Children =
            {
                new TemplateCategoryNode
                {
                    Label = "业务操作",
                    Path = "普惠高龄/业务操作",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("新增", "普惠高龄/新增"),
                        Leaf("变更", "普惠高龄/变更"),
                        Leaf("停止", "普惠高龄/停止")
                    }
                }
            }
        };
    }

    private static TemplateCategoryNode BuildAssetVerificationGroup()
    {
        return new TemplateCategoryNode
        {
            Label = "资产核查",
            Path = "资产核查",
            IsExpanded = true,
            Children =
            {
                new TemplateCategoryNode
                {
                    Label = "业务操作",
                    Path = "资产核查/业务操作",
                    IsExpanded = true,
                    Children =
                    {
                        Leaf("资产核查授权业务", ClassificationConstants.AssetVerification),
                        Leaf("资产核查月报", "AssetVerificationMonthlyReport")
                    }
                }
            }
        };
    }

    private static TemplateCategoryNode Leaf(string label, string path)
    {
        return new TemplateCategoryNode
        {
            Label = label,
            Path = path,
            IsLeaf = true
        };
    }
}
