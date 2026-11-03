using NewCosmos.Constants;
using NewCosmos.Models.Enums;
using NewCosmos.Services.StateMachine;
using Xunit;

namespace NewCosmos.Tests.StateMachine;

/// <summary>
/// 申请状态机口径锚点（AGENTS §14：ApplicationStateMachine 为唯一权威，真实写入方=ApplicationStatusService）。
/// 重点锁住：归档即审核（Draft 不可直达 Approved）、恢复保障唯一出口、Refused 终态、Completed 旧数据兼容边。
/// </summary>
public class ApplicationStateMachineTests
{
    [Theory]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Submitted)]      // 提交
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Refused)]         // 拒绝
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Approved)]    // 审批
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Refused)]
    [InlineData(ApplicationStatus.Approved, ApplicationStatus.Stopped)]      // 停保
    [InlineData(ApplicationStatus.Stopped, ApplicationStatus.Approved)]       // 恢复保障（唯一出口）
    [InlineData(ApplicationStatus.Completed, ApplicationStatus.Stopped)]     // 旧数据兼容边
    public void CanTransition_允许的转换(ApplicationStatus from, ApplicationStatus to)
        => Assert.True(ApplicationStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Approved)]   // 不允许跳过 Submitted（归档放行走 ApplicationStatusService，不走状态机）
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Stopped)]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Completed)]
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Stopped)] // 普通路径不放行（StopAsync 的 allowSubmittedOverride 属业务放行）
    [InlineData(ApplicationStatus.Refused, ApplicationStatus.Approved)]  // Refused 终态
    [InlineData(ApplicationStatus.Refused, ApplicationStatus.Stopped)]
    [InlineData(ApplicationStatus.Stopped, ApplicationStatus.Stopped)]
    [InlineData(ApplicationStatus.Approved, ApplicationStatus.Draft)]    // 不可回草稿
    public void CanTransition_禁止的转换(ApplicationStatus from, ApplicationStatus to)
        => Assert.False(ApplicationStateMachine.CanTransition(from, to));

    [Fact]
    public void ValidateTransition_禁止转换_返回INVALID_TRANSITION与可读消息()
    {
        var result = ApplicationStateMachine.ValidateTransition(ApplicationStatus.Draft, ApplicationStatus.Approved);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.INVALID_TRANSITION, result.ErrorCode);
        Assert.Contains("不允许从", result.Message);
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft, true)]
    [InlineData(ApplicationStatus.Submitted, false)]
    [InlineData(ApplicationStatus.Approved, false)]
    [InlineData(ApplicationStatus.Stopped, false)]
    public void IsEditable_仅草稿可编辑(ApplicationStatus status, bool expected)
        => Assert.Equal(expected, ApplicationStateMachine.IsEditable(status));
}
