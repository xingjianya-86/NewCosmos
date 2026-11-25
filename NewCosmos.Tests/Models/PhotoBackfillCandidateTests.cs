using NewCosmos.Models.Entities;
using Xunit;

namespace NewCosmos.Tests.Models;

/// <summary>
/// 照片补传候选业务域显示名：四域（含资产核查）必须映射为中文，
/// 否则补传列表会直接显示 "AssetVerification" 代码。
/// </summary>
public class PhotoBackfillCandidateTests
{
    [Xunit.Fact]
    public void 资产核查域_显示为中文()
    {
        Assert.Equal("资产核查", PhotoBackfillCandidate.ToBusinessDisplayName("AssetVerification"));
    }

    [Xunit.Fact]
    public void 跨域人员_业务域中文用间隔号连接()
    {
        var candidate = new PhotoBackfillCandidate
        {
            BusinessTypes = "AssetVerification/ElderlyBenefits"
        };

        Assert.Equal("资产核查 · 高龄津贴", candidate.BusinessTypeNames);
    }
}
