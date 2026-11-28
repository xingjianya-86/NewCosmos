using NewCosmos.Constants;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests;

public class ElderlyCertificationStopPreviewHelperTests
{
    [Theory]
    [InlineData(ElderlyCertificationConstants.CertStatusPending, ElderlyBenefitConstants.StopReasonCertificationFail)]
    [InlineData(ElderlyCertificationConstants.CertStatusRefused, ElderlyBenefitConstants.StopReasonCertificationFail)]
    [InlineData(ElderlyCertificationConstants.CertStatusFailed, ElderlyBenefitConstants.StopReasonCertificationFail)]
    [InlineData(ElderlyCertificationConstants.CertStatusNotLocated, ElderlyBenefitConstants.StopReasonCertificationFail)]
    [InlineData(ElderlyCertificationConstants.CertStatusDeceased, ElderlyBenefitConstants.StopReasonDeceased)]
    [InlineData(ElderlyCertificationConstants.CertStatusMovedOut, ElderlyBenefitConstants.StopReasonMovedOut)]
    public void GetSuggestedStopReason_MapsCertStatus(string certStatus, string expected)
    {
        Assert.Equal(expected, ElderlyCertificationStopPreviewHelper.GetSuggestedStopReason(certStatus));
    }

    [Theory]
    [InlineData(ElderlyCertificationConstants.CertStatusVerified)]
    [InlineData(ElderlyCertificationConstants.CertStatusExempt)]
    [InlineData(ElderlyCertificationConstants.CertStatusStopped)]
    [InlineData(ElderlyCertificationConstants.CertStatusInvalid)]
    public void GetSuggestedStopReason_ReturnsNullForNonCandidate(string certStatus)
    {
        Assert.Null(ElderlyCertificationStopPreviewHelper.GetSuggestedStopReason(certStatus));
    }

    [Fact]
    public void BuildPresetNote_OnlyForNotLocated()
    {
        Assert.Equal(ElderlyCertificationStopPreviewHelper.NotLocatedNote,
            ElderlyCertificationStopPreviewHelper.BuildPresetNote(ElderlyCertificationConstants.CertStatusNotLocated));
        Assert.Null(ElderlyCertificationStopPreviewHelper.BuildPresetNote(ElderlyCertificationConstants.CertStatusFailed));
    }

    [Fact]
    public void ComputeSuggestedDueStopDate_UsesDeathDateForDeceased()
    {
        var deathDate = new DateTime(2026, 3, 8);
        var due = ElderlyCertificationStopPreviewHelper.ComputeSuggestedDueStopDate(
            ElderlyCertificationConstants.CertStatusDeceased,
            deathDate,
            new DateTime(2026, 12, 31),
            1);

        Assert.Equal(deathDate, due);
    }

    [Fact]
    public void ComputeSuggestedDueStopDate_AddsCampaignOffsetForOtherStatuses()
    {
        var due = ElderlyCertificationStopPreviewHelper.ComputeSuggestedDueStopDate(
            ElderlyCertificationConstants.CertStatusFailed,
            null,
            new DateTime(2026, 12, 31),
            1);

        Assert.Equal(new DateTime(2027, 1, 31), due);
    }

    [Fact]
    public void ComputeSuggestedDueStopDate_NegativeOffsetFallsBackToPeriodEnd()
    {
        var due = ElderlyCertificationStopPreviewHelper.ComputeSuggestedDueStopDate(
            ElderlyCertificationConstants.CertStatusPending,
            null,
            new DateTime(2026, 12, 31),
            -3);

        Assert.Equal(new DateTime(2026, 12, 31), due);
    }

    [Fact]
    public void BuildPendingHint_ClosedOrPastDeadlineIsOverdue()
    {
        var periodEnd = new DateTime(2026, 12, 31);
        var today = new DateTime(2026, 11, 26);

        Assert.Equal("待截止", ElderlyCertificationStopPreviewHelper.BuildPendingHint(
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CampaignStatusCollecting,
            periodEnd,
            today));

        Assert.Equal("逾期未核查", ElderlyCertificationStopPreviewHelper.BuildPendingHint(
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CampaignStatusClosed,
            periodEnd,
            today));

        Assert.Equal("逾期未核查", ElderlyCertificationStopPreviewHelper.BuildPendingHint(
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CampaignStatusCollecting,
            today.AddDays(-1),
            today));
    }

    [Fact]
    public void BuildPendingHint_EmptyForConclusiveStatus()
    {
        Assert.Equal(string.Empty, ElderlyCertificationStopPreviewHelper.BuildPendingHint(
            ElderlyCertificationConstants.CertStatusFailed,
            ElderlyCertificationConstants.CampaignStatusClosed,
            new DateTime(2026, 12, 31),
            new DateTime(2026, 11, 26)));
    }

    [Fact]
    public void IsOverduePending_MatchesHint()
    {
        var today = new DateTime(2026, 11, 26);

        Assert.True(ElderlyCertificationStopPreviewHelper.IsOverduePending(
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CampaignStatusClosed,
            new DateTime(2026, 12, 31),
            today));

        Assert.False(ElderlyCertificationStopPreviewHelper.IsOverduePending(
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CampaignStatusCollecting,
            new DateTime(2026, 12, 31),
            today));
    }
}
