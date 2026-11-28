using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using Xunit;

namespace NewCosmos.Tests;

public class ElderlyInfoChangeImportHelperTests
{
    private static ElderlyInfoChangeApplicationValues Current() => new()
    {
        Id = 1,
        IdCard = "110101195001011234",
        Name = "张三",
        Status = ElderlyBenefitConstants.StatusConfirmed,
        Phone = "13800000000",
        FamilyAddress = "旧地址",
        BankName = "旧银行",
        BankAccount = "6222000000000000001",
        AgentName = "旧代办人",
        AgentRelation = "子女",
        AgentReceiveName = "旧代领人",
        AgentReceiveRelation = "子女",
        AgentReceiveBankName = "旧代领行",
        AgentReceiveBankAccount = "6222000000000000002"
    };

    [Fact]
    public void Build_OnlyKeepsChangedWhitelistedFields()
    {
        var row = new ElderlyCertImportRowPreview
        {
            RowNo = 3,
            Phone = " 13900000000 ",
            Address = "旧地址",
            BankName = "新银行",
            BankAccount = "6222 0000 0000 0000 003",
            AgentName = "旧代办人"
        };

        var result = ElderlyInfoChangeImportHelper.Build(row, Current());

        Assert.True(result.HasAnyValue);
        Assert.False(result.HasInvalidBankAccount);
        Assert.Collection(result.Details,
            d =>
            {
                Assert.Equal(ElderlyInfoChangeConstants.FieldPhone, d.FieldKey);
                Assert.Equal("13800000000", d.OldValue);
                Assert.Equal("13900000000", d.NewValue);
            },
            d =>
            {
                Assert.Equal(ElderlyInfoChangeConstants.FieldBankName, d.FieldKey);
                Assert.Equal("旧银行", d.OldValue);
                Assert.Equal("新银行", d.NewValue);
            },
            d =>
            {
                Assert.Equal(ElderlyInfoChangeConstants.FieldBankAccount, d.FieldKey);
                Assert.Equal("6222000000000000001", d.OldValue);
                Assert.Equal("6222000000000000003", d.NewValue);
            });
    }

    [Fact]
    public void Build_ReturnsEmptyWhenAllValuesMatchCurrent()
    {
        var row = new ElderlyCertImportRowPreview
        {
            RowNo = 4,
            Phone = "13800000000",
            Address = "旧地址",
            BankAccount = "6222000000000000001"
        };

        var result = ElderlyInfoChangeImportHelper.Build(row, Current());

        Assert.True(result.HasAnyValue);
        Assert.Empty(result.Details);
        Assert.False(result.HasInvalidBankAccount);
    }

    [Theory]
    [InlineData("6222 0000 0000 0000 0003", "62220000000000000003")]
    [InlineData("'62220000000000000003", "62220000000000000003")]
    [InlineData("６２２２００００００００００００００３", "6222000000000000003")]
    public void NormalizeBankAccount_StripsFormatting(string raw, string expected)
    {
        Assert.Equal(expected, ElderlyInfoChangeImportHelper.NormalizeBankAccount(raw));
    }

    [Theory]
    [InlineData("6.222E+18")]
    [InlineData("6.22200E+18")]
    [InlineData("62220000")]
    public void Build_InvalidBankAccountIsSkippedButOtherFieldsRemain(string raw)
    {
        var row = new ElderlyCertImportRowPreview
        {
            RowNo = 5,
            Phone = "13900000000",
            BankAccount = raw
        };

        var result = ElderlyInfoChangeImportHelper.Build(row, Current());

        Assert.True(result.HasInvalidBankAccount);
        Assert.Single(result.Details);
        Assert.Equal(ElderlyInfoChangeConstants.FieldPhone, result.Details[0].FieldKey);
    }

    [Fact]
    public void HasValues_DetectsAgentFields()
    {
        Assert.False(ElderlyInfoChangeImportHelper.HasValues(new ElderlyCertImportRowPreview()));
        Assert.True(ElderlyInfoChangeImportHelper.HasValues(new ElderlyCertImportRowPreview
        {
            AgentReceiveBankAccount = "62220000000000000003"
        }));
    }
}
