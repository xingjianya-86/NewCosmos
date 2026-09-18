using NewCosmos.Constants;
using NewCosmos.Models.Entities;

namespace NewCosmos.Helpers;

/// <summary>
/// 土地确权份额计算（唯一权威实现，禁止在调用方重抄口径）。
/// 口径：
/// - 存活享有 / 死亡继承计入份额；无土地权 / 外来土地不计入（LandStatusConstants.ShouldCount）；
/// - "死亡继承"人员的份额转移给本组指定继承人（LandInheritTo）名下，
///   继承人须为本组有效人员；未指定或继承人无效时该份额悬空（计入分母、不参与分子）；
/// - 家庭享有面积 = 总台账面积 ÷ 有效总份额 × 本户有效份额（申请表单 CalculateLandConfirmationArea 同源）。
/// </summary>
public static class LandShareCalculator
{
    /// <summary>
    /// 计算组内"姓名 → 有效份额"字典（含死亡继承份额转移；键为持份人/继承人登记姓名原文）。
    /// </summary>
    public static Dictionary<string, decimal> ComputeEffectiveShares(LandConfirmationGroup group)
    {
        var effectiveShares = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var person in group.Persons)
        {
            if (!LandStatusConstants.ShouldCount(person.LandStatus)) continue;

            if (person.LandStatus == LandStatusConstants.DECEASED_INHERITANCE)
            {
                // 死亡继承：份额累加到指定继承人名下；未指定/继承人无效则悬空
                if (string.IsNullOrWhiteSpace(person.LandInheritTo)) continue;
                var heir = group.Persons.FirstOrDefault(p =>
                    string.Equals(p.Name, person.LandInheritTo, StringComparison.OrdinalIgnoreCase)
                    && LandStatusConstants.ShouldCount(p.LandStatus));
                if (heir == null) continue;
                effectiveShares[heir.Name] = effectiveShares.GetValueOrDefault(heir.Name) + (decimal)person.SharesCount;
                continue;
            }

            effectiveShares[person.Name] = effectiveShares.GetValueOrDefault(person.Name) + (decimal)person.SharesCount;
        }
        return effectiveShares;
    }
}
