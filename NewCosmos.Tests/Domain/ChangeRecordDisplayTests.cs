using NewCosmos.Constants;
using NewCosmos.Services.Domain.ChangeManagement;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 变更历史显示名映射（<see cref="ChangeRecord.ChangeTypeDisplay"/>）：
/// 新增 change_type 必须给中文显示名，否则变更历史列表回退原值、裸露英文码（VerifyStop→"核验停止" 锁点）。
/// </summary>
public class ChangeRecordDisplayTests
{
    [Fact]
    public void 核验停止_显示为中文()
    {
        var record = new ChangeRecord { ChangeType = DictionaryConstants.ChangeType.VERIFY_STOP };
        Assert.Equal("核验停止", record.ChangeTypeDisplay);
    }

    [Fact]
    public void 未知类型_回退原值()
    {
        var record = new ChangeRecord { ChangeType = "SomethingNew" };
        Assert.Equal("SomethingNew", record.ChangeTypeDisplay);
    }
}
