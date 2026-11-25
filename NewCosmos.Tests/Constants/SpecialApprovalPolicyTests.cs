using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 一事一议（黑民规〔2026〕10 号）：8 类适用情形 / 6 项负面清单 / 状态门槛。
/// </summary>
public class SpecialApprovalPolicyTests
{
    [Fact]
    public void 八类情形与六项负面清单是稳定词表()
    {
        Assert.Equal(8, SpecialApprovalConstants.CaseTypes.Length);
        Assert.Equal(6, SpecialApprovalConstants.NegativeItems.Length);
        Assert.Equal("现行救助政策无明确规定，实际操作中难以判定或不给予救助将造成基本生活困难的疑难事项",
            SpecialApprovalConstants.GetCaseTypeName("PolicyUnclear"));
        Assert.Null(SpecialApprovalConstants.GetCaseTypeName("NoSuch"));
        Assert.NotNull(SpecialApprovalConstants.GetNegativeItemName("ConcealOrBlacklist"));
        // 正式文未把“多套房产/高价车/大额金融资产”列为独立负面项
        Assert.Null(SpecialApprovalConstants.GetNegativeItemName("PropertyExceeded"));
    }

    [Fact]
    public void 提交校验_情形必选且未知情形拒绝()
    {
        Assert.Equal("请选择适用的「一事一议」情形",
            SpecialApprovalConstants.ValidateCaseSelection("", Array.Empty<string>()));
        Assert.Contains("无效的适用情形",
            SpecialApprovalConstants.ValidateCaseSelection("NoSuch", Array.Empty<string>()));
        Assert.Null(SpecialApprovalConstants.ValidateCaseSelection("PolicyUnclear", Array.Empty<string>()));
        Assert.Null(SpecialApprovalConstants.ValidateCaseSelection(" CityDesignated ", new[] { "" }));
    }

    [Fact]
    public void 提交校验_命中负面清单即阻断并给出名称()
    {
        var error = SpecialApprovalConstants.ValidateCaseSelection("PolicyUnclear", new[] { "ConcealOrBlacklist" });
        Assert.NotNull(error);
        Assert.Contains("信用黑名单", error);
    }

    [Theory]
    [InlineData(SpecialApprovalConstants.StatusDraft, true)]
    [InlineData(SpecialApprovalConstants.StatusReturnedForCorrection, true)]
    [InlineData(SpecialApprovalConstants.StatusSubmitted, false)]
    [InlineData(SpecialApprovalConstants.StatusReturned, false)]
    public void CanSubmit_仅草稿与退回补正(string status, bool expected)
        => Assert.Equal(expected, SpecialApprovalConstants.CanSubmit(status));

    [Fact]
    public void 状态门槛_评议与备案()
    {
        Assert.True(SpecialApprovalConstants.CanRecordConclusion(SpecialApprovalConstants.StatusSubmitted));
        Assert.False(SpecialApprovalConstants.CanRecordConclusion(SpecialApprovalConstants.StatusDraft));

        Assert.True(SpecialApprovalConstants.CanMarkFiled(SpecialApprovalConstants.StatusReturned));
        Assert.True(SpecialApprovalConstants.CanMarkFiled(SpecialApprovalConstants.StatusApproved));
        Assert.False(SpecialApprovalConstants.CanMarkFiled(SpecialApprovalConstants.StatusSubmitted));

        Assert.True(SpecialApprovalConstants.IsNotPassed(SpecialApprovalConstants.StatusNotPassed));
        Assert.False(SpecialApprovalConstants.IsNotPassed(SpecialApprovalConstants.StatusReturned));
    }

    [Fact]
    public void 未完成状态阻塞申请提交()
    {
        Assert.True(SpecialApprovalConstants.BlocksApplicationSubmit(SpecialApprovalConstants.StatusDraft));
        Assert.True(SpecialApprovalConstants.BlocksApplicationSubmit(SpecialApprovalConstants.StatusSubmitted));
        Assert.True(SpecialApprovalConstants.BlocksApplicationSubmit(SpecialApprovalConstants.StatusReturnedForCorrection));
        Assert.False(SpecialApprovalConstants.BlocksApplicationSubmit(SpecialApprovalConstants.StatusReturned));
        Assert.False(SpecialApprovalConstants.BlocksApplicationSubmit(SpecialApprovalConstants.StatusFiled));
        Assert.False(SpecialApprovalConstants.BlocksApplicationSubmit(SpecialApprovalConstants.StatusNotPassed));
    }

    [Fact]
    public void 负面清单落库文本往返()
    {
        var joined = SpecialApprovalConstants.JoinNegativeItems(new[] { "A", " B ", "A", "" });
        Assert.Equal("A,B", joined);

        var split = SpecialApprovalConstants.SplitNegativeItems(joined);
        Assert.Equal(new[] { "A", "B" }, split);
        Assert.Empty(SpecialApprovalConstants.SplitNegativeItems(null));
    }
}
