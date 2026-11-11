using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 成员减员「人员死亡」哨兵（MemberChangeReasonConstants）：
/// RebuildAndReview 死亡减员批量 CTE 依赖 IsDeath(ReasonCode) 命中写死亡记录，
/// 哨兵值与字典项 item_key 必须保持同名（"Death"）。
/// </summary>
public class MemberChangeReasonConstantsTests
{
    [Theory]
    [InlineData("Death", true)]
    [InlineData("death", true)]   // OrdinalIgnoreCase
    [InlineData("DEATH", true)]
    [InlineData("MoveOut", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDeath_死亡哨兵判定(string? code, bool expected)
        => Assert.Equal(expected, MemberChangeReasonConstants.IsDeath(code));

    [Fact]
    public void RemoveDeath_字面量与字典item_key约定一致()
        => Assert.Equal("Death", MemberChangeReasonConstants.RemoveDeath);

    [Theory]
    [InlineData("Death", "人员死亡")]
    [InlineData("Other", "Other")]   // 未知 code 原样返回，不洗空
    [InlineData(null, "")]
    public void GetRemoveName_名称映射(string? code, string expected)
        => Assert.Equal(expected, MemberChangeReasonConstants.GetRemoveName(code));
}
