using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests;

public class ElderlyCertificationConstantsTests
{
    [Theory]
    [InlineData("通过", ElderlyCertificationConstants.CertStatusVerified)]
    [InlineData("认证通过", ElderlyCertificationConstants.CertStatusVerified)]
    [InlineData("已核查", ElderlyCertificationConstants.CertStatusVerified)]
    [InlineData("不通过", ElderlyCertificationConstants.CertStatusFailed)]
    [InlineData("拒绝核查", ElderlyCertificationConstants.CertStatusRefused)]
    [InlineData("不配合", ElderlyCertificationConstants.CertStatusRefused)]
    [InlineData("已去世", ElderlyCertificationConstants.CertStatusDeceased)]
    [InlineData("死亡", ElderlyCertificationConstants.CertStatusDeceased)]
    [InlineData("户籍迁出", ElderlyCertificationConstants.CertStatusMovedOut)]
    [InlineData("无法联系", ElderlyCertificationConstants.CertStatusNotLocated)]
    [InlineData("查找不到本人", ElderlyCertificationConstants.CertStatusNotLocated)]
    [InlineData("免认证", ElderlyCertificationConstants.CertStatusExempt)]
    [InlineData("未认证", ElderlyCertificationConstants.CertStatusPending)]
    [InlineData("待核查", ElderlyCertificationConstants.CertStatusPending)]
    [InlineData("", ElderlyCertificationConstants.CertStatusPending)]
    [InlineData("随便写的", ElderlyCertificationConstants.CertStatusInvalid)]
    public void ParseCertStatus_MapsChineseOptions(string text, string expected)
    {
        Assert.Equal(expected, ElderlyCertificationConstants.ParseCertStatus(text));
    }

    [Theory]
    [InlineData("人脸识别", ElderlyCertificationConstants.VerifyMethodFace)]
    [InlineData("龙江人社APP", ElderlyCertificationConstants.VerifyMethodFace)]
    [InlineData("上门核查", ElderlyCertificationConstants.VerifyMethodOnsite)]
    [InlineData("视频", ElderlyCertificationConstants.VerifyMethodVideo)]
    [InlineData("照片", ElderlyCertificationConstants.VerifyMethodPhoto)]
    [InlineData("其他", ElderlyCertificationConstants.VerifyMethodOther)]
    public void ParseVerifyMethod_MapsChineseOptions(string text, string expected)
    {
        Assert.Equal(expected, ElderlyCertificationConstants.ParseVerifyMethod(text));
    }

    [Fact]
    public void StopCandidate_IncludesPendingRefusedAndDeath_ExcludesVerified()
    {
        Assert.True(ElderlyCertificationConstants.IsStopCandidate(ElderlyCertificationConstants.CertStatusPending));
        Assert.True(ElderlyCertificationConstants.IsStopCandidate(ElderlyCertificationConstants.CertStatusRefused));
        Assert.True(ElderlyCertificationConstants.IsStopCandidate(ElderlyCertificationConstants.CertStatusDeceased));
        Assert.True(ElderlyCertificationConstants.IsStopCandidate(ElderlyCertificationConstants.CertStatusNotLocated));
        Assert.False(ElderlyCertificationConstants.IsStopCandidate(ElderlyCertificationConstants.CertStatusVerified));
        Assert.False(ElderlyCertificationConstants.IsStopCandidate(ElderlyCertificationConstants.CertStatusExempt));
        Assert.False(ElderlyCertificationConstants.IsStopCandidate(ElderlyCertificationConstants.CertStatusInvalid));
    }

    [Fact]
    public void EvidenceType_InfersFromFileName()
    {
        Assert.Equal(ElderlyCertificationConstants.EvidenceTypeIdCard,
            ElderlyCertificationConstants.InferEvidenceType("110101195001011234_身份证.jpg", null));
        Assert.Equal(ElderlyCertificationConstants.EvidenceTypeOnsite,
            ElderlyCertificationConstants.InferEvidenceType("上门认证_1.jpg", null));
        Assert.Equal(ElderlyCertificationConstants.EvidenceTypeFace,
            ElderlyCertificationConstants.InferEvidenceType("人脸认证截图.png", null));
    }
}
