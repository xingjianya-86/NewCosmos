using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 高龄信息变更字段白名单/风险/身份字段拒绝/变更单号格式/中文标签。
/// 白名单与 nc_biz_elderly_applications 列名一一对应，任何调整都必须同步迁移与审批 SQL。
/// </summary>
public class ElderlyInfoChangeConstantsTests
{
    [Fact]
    public void AllowedFieldKeys_覆盖合同全部11个字段()
    {
        Assert.Equal(11, ElderlyInfoChangeConstants.AllowedFieldKeys.Length);
        Assert.Contains(ElderlyInfoChangeConstants.FieldPhone, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldFamilyAddress, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldDetailAddress, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldBankName, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldBankAccount, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldAgentName, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldAgentRelation, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldAgentReceiveName, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldAgentReceiveRelation, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldAgentReceiveBankName, ElderlyInfoChangeConstants.AllowedFieldKeys);
        Assert.Contains(ElderlyInfoChangeConstants.FieldAgentReceiveBankAccount, ElderlyInfoChangeConstants.AllowedFieldKeys);
    }

    [Theory]
    [InlineData("phone", true)]
    [InlineData("family_address", true)]
    [InlineData("detail_address", true)]
    [InlineData("bank_name", true)]
    [InlineData("bank_account", true)]
    [InlineData("agent_name", true)]
    [InlineData("agent_receive_bank_account", true)]
    [InlineData("PHONE", true)]
    [InlineData("name", false)]
    [InlineData("id_card", false)]
    [InlineData("category", false)]
    [InlineData("identity_flag", false)]
    [InlineData("hukou_address", false)]
    [InlineData("hukou_village", false)]
    [InlineData("hukou_city_id", false)]
    [InlineData("remark", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowed_白名单与身份字段拒绝(string? fieldKey, bool expected)
        => Assert.Equal(expected, ElderlyInfoChangeConstants.IsAllowed(fieldKey));

    [Theory]
    [InlineData("name", true)]
    [InlineData("id_card", true)]
    [InlineData("category", true)]
    [InlineData("identity_flag", true)]
    [InlineData("hukou_address", true)]
    [InlineData("HUKOU_VILLAGE", true)]
    [InlineData("phone", false)]
    [InlineData("bank_account", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsIdentityField_身份户籍类别判定(string? fieldKey, bool expected)
        => Assert.Equal(expected, ElderlyInfoChangeConstants.IsIdentityField(fieldKey));

    [Theory]
    [InlineData("phone", "LOW")]
    [InlineData("family_address", "LOW")]
    [InlineData("detail_address", "LOW")]
    [InlineData("bank_name", "HIGH")]
    [InlineData("bank_account", "HIGH")]
    [InlineData("agent_name", "HIGH")]
    [InlineData("agent_relation", "HIGH")]
    [InlineData("agent_receive_name", "HIGH")]
    [InlineData("agent_receive_relation", "HIGH")]
    [InlineData("agent_receive_bank_name", "HIGH")]
    [InlineData("agent_receive_bank_account", "HIGH")]
    [InlineData("unknown", "")]
    [InlineData(null, "")]
    public void GetRisk_风险级别(string? fieldKey, string expected)
        => Assert.Equal(expected, ElderlyInfoChangeConstants.GetRisk(fieldKey));

    [Fact]
    public void GetRisk_仅三个低风险字段()
    {
        var lowRisk = ElderlyInfoChangeConstants.AllowedFieldKeys
            .Where(k => ElderlyInfoChangeConstants.GetRisk(k) == ElderlyInfoChangeConstants.RiskLow)
            .ToArray();

        Assert.Equal(3, lowRisk.Length);
        Assert.Equal(
            new[] { "phone", "family_address", "detail_address" }.OrderBy(k => k),
            lowRisk.OrderBy(k => k));
    }

    [Theory]
    [InlineData("phone", "联系电话")]
    [InlineData("family_address", "家庭住址")]
    [InlineData("detail_address", "家庭详细地址")]
    [InlineData("bank_name", "社保卡开户行")]
    [InlineData("bank_account", "社保卡账号")]
    [InlineData("agent_name", "代办人姓名")]
    [InlineData("agent_relation", "代办人关系")]
    [InlineData("agent_receive_name", "代领人姓名")]
    [InlineData("agent_receive_relation", "代领人关系")]
    [InlineData("agent_receive_bank_name", "代领人开户行")]
    [InlineData("agent_receive_bank_account", "代领人账号")]
    [InlineData("unknown_key", "unknown_key")]
    [InlineData(null, "")]
    public void GetFieldLabel_中文标签(string? fieldKey, string expected)
        => Assert.Equal(expected, ElderlyInfoChangeConstants.GetFieldLabel(fieldKey));

    [Fact]
    public void BuildChangeNo_格式为BG加17位时间戳()
    {
        var value = ElderlyInfoChangeConstants.BuildChangeNo(new DateTime(2026, 11, 26, 12, 30, 45, 123));

        Assert.Equal("BG20261126123045123", value);
        Assert.StartsWith("BG", value);
        Assert.Equal(19, value.Length);
        Assert.True(value[2..].All(char.IsDigit));
    }

    [Theory]
    [InlineData("2026-12", true)]
    [InlineData("2026-01", true)]
    [InlineData("2026-13", false)]
    [InlineData("2026-00", false)]
    [InlineData("2026-1", false)]
    [InlineData("202612", false)]
    [InlineData("1999-12", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidEffectiveMonth_格式校验(string? month, bool expected)
        => Assert.Equal(expected, ElderlyInfoChangeConstants.IsValidEffectiveMonth(month));

    [Fact]
    public void RequiresEffectiveMonth_银行字段触发()
    {
        Assert.True(ElderlyInfoChangeConstants.RequiresEffectiveMonth(new[] { "phone", "bank_account" }));
        Assert.True(ElderlyInfoChangeConstants.RequiresEffectiveMonth(new[] { "agent_receive_bank_name" }));
        Assert.False(ElderlyInfoChangeConstants.RequiresEffectiveMonth(new[] { "phone", "family_address" }));
        Assert.False(ElderlyInfoChangeConstants.RequiresEffectiveMonth(Array.Empty<string>()));
    }

    [Fact]
    public void GetDefaultEffectiveMonth_默认下个月()
    {
        var month = ElderlyInfoChangeConstants.GetDefaultEffectiveMonth(new DateTime(2026, 12, 15));
        Assert.Equal("2027-01", month);
    }

    [Theory]
    [InlineData("Pending", "待审批")]
    [InlineData("Approved", "已通过")]
    [InlineData("Rejected", "已驳回")]
    [InlineData("Cancelled", "已作废")]
    [InlineData("Other", "Other")]
    [InlineData(null, "")]
    public void GetStatusName_状态中文名(string? status, string expected)
        => Assert.Equal(expected, ElderlyInfoChangeConstants.GetStatusName(status));
}
