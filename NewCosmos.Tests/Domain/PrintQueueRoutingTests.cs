using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.Printing;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 方案A（本地预览 + 打印动作推送 Windows Agent）纯逻辑口径：
/// 签名占位拒绝定向推送、入队权限码与本地打印同源、队列路由结果包装、[Print] 开关默认值。
/// </summary>
public class PrintQueueRoutingTests
{
    private static Template TemplateWithConfig(string configJson) =>
        new() { Id = 1, Name = "测试模板", FileType = "docx", ConfigJson = configJson };

    [Fact]
    public void 模板含签名占位_拒绝定向推送()
    {
        var template = TemplateWithConfig(
            """{"fields":[{"placeholder":"{本人签名}","fieldKey":"SIGNATURE_APPLICANT"}]}""");

        Assert.True(PrintPushGuard.HasSignaturePlaceholder(template));
    }

    [Fact]
    public void 模板无签名占位_允许定向推送()
    {
        var template = TemplateWithConfig(
            """{"fields":[{"placeholder":"{姓名}","fieldKey":"APPLICANT_NAME"}]}""");

        Assert.False(PrintPushGuard.HasSignaturePlaceholder(template));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ 这不是合法 JSON")]
    public void 模板配置缺失或损坏_不按签名拦截(string? configJson)
    {
        // 配置损坏不因解析失败把推送堵死：本地打印路径同样会报错
        Assert.False(PrintPushGuard.HasSignaturePlaceholder(TemplateWithConfig(configJson ?? string.Empty)));
    }

    [Fact]
    public void 请求字段含签名键_判定为含签名()
    {
        var request = new PrintJobRequest
        {
            Fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SIGNATURE_AGENT"] = "base64"
            }
        };

        Assert.True(PrintPushGuard.HasSignatureField(request));
        Assert.False(PrintPushGuard.HasSignatureField(new PrintJobRequest()));
    }

    [Theory]
    [InlineData(PrintBusinessTypes.Recovery, PermissionCodes.PRINT_RECOVERY)]
    [InlineData(PrintBusinessTypes.TempRelief, PermissionCodes.PRINT_TEMPRELIEF)]
    [InlineData(PrintBusinessTypes.ElderlyBenefits, PermissionCodes.PRINT_ELDERLYBENEFITS)]
    [InlineData(PrintBusinessTypes.LowIncomeProof, PermissionCodes.PRINT_LOWINCOMEPROOF)]
    [InlineData(PrintBusinessTypes.DeathStatement, PermissionCodes.PRINT_LOWINCOMEPROOF)]
    [InlineData(PrintBusinessTypes.FamilyApplication, PermissionCodes.PRINT_FAMILYAPPLICATION)]
    [InlineData(PrintBusinessTypes.AssetVerification, PermissionCodes.PRINT_ASSETVERIFICATION)]
    [InlineData(PrintBusinessTypes.EconomicReview, PermissionCodes.PRINT_ECONOMICREVIEW)]
    [InlineData(PrintBusinessTypes.NearRelative, PermissionCodes.PRINT_FAMILYAPPLICATION)]
    public void 入队权限码与本地打印同源映射(string businessType, string expectedPermission)
    {
        // 入队前校验必须复用 PrintExecuteService 的中心映射，禁止另抄一份
        Assert.True(PrintExecuteService.TryGetPrintPermissionCode(businessType, out var actual));
        Assert.Equal(expectedPermission, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SomeUnknownBusinessType")]
    public void 未登记业务类型_拒绝入队(string businessType)
    {
        Assert.False(PrintExecuteService.TryGetPrintPermissionCode(businessType, out _));
    }

    [Fact]
    public void 本地打印成功_包装为非队列结果()
    {
        var record = new PrintRecord { BatchNo = "BATCH-1", FilePath = "C:/a.docx" };

        var outcome = QueuePrintOutcome.FromLocal(Result.Success(record));

        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.Value!.UsedQueue);
        Assert.Equal("BATCH-1", outcome.Value.JobNo);
        Assert.Same(record, outcome.Value.LocalRecord);
    }

    [Fact]
    public void 本地打印失败_错误码与消息原样透传()
    {
        var outcome = QueuePrintOutcome.FromLocal(
            Result.Failure<PrintRecord>(ErrorCodes.PRINT_FAILED, "打印机脱机"));

        Assert.True(outcome.IsFailure);
        Assert.Equal(ErrorCodes.PRINT_FAILED, outcome.ErrorCode);
        Assert.Equal("打印机脱机", outcome.Message);
    }

    [Fact]
    public void 打印队列默认配置_本地模式零回归()
    {
        var options = new AppOptions { Version = "1", ApplicationName = "x", WindowTitle = "x" };

        Assert.Equal(PrintQueueConstants.OutputModeLocal, options.PrintOutputMode);
        Assert.Equal(PrintQueueConstants.DefaultWaitSeconds, options.PrintQueueWaitSeconds);
        Assert.True(options.PrintQueueFallbackToLocal);
        Assert.Equal("Local", options.PrintOutputMode);
        Assert.Equal(60, options.PrintQueueWaitSeconds);
    }
}
