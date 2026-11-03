using NewCosmos.Constants;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 识别结果分配规划器单测：预匹配（证号/姓名命中）、名单外默认新增赡养人、冲突检测。
/// </summary>
public class OcrAssignmentPlannerTests
{
    private static readonly List<OcrAssignmentPlanner.MemberRow> Members = new()
    {
        new(OcrAssignmentPlanner.TargetKeys.Head, "张三", "11010119900307651X"),
        new(OcrAssignmentPlanner.TargetKeys.Member("1"), "李四", "110101198801011234"),
        new(OcrAssignmentPlanner.TargetKeys.Member("2"), "王五", ""),
    };

    private static Dictionary<string, string> IdFields(string name, string idCard)
        => new() { [OcrFieldKeys.Name] = name, [OcrFieldKeys.IdCard] = idCard };

    [Fact]
    public void SuggestTarget_IdCardHit_ReturnsMemberRow()
    {
        var result = OcrAssignmentPlanner.SuggestTarget(IdFields("张三", "110101198801011234"), Members);
        Assert.Equal(OcrAssignmentPlanner.TargetKeys.Member("1"), result);
    }

    [Fact]
    public void SuggestTarget_IdCardHit_IgnoresCase()
    {
        var result = OcrAssignmentPlanner.SuggestTarget(IdFields("", "110101198801011234".ToLowerInvariant()), Members);
        Assert.Equal(OcrAssignmentPlanner.TargetKeys.Member("1"), result);
    }

    [Fact]
    public void SuggestTarget_IdCardHitTakesPrecedenceOverName()
    {
        // 证号命中李四、姓名是张三 → 证号优先
        var result = OcrAssignmentPlanner.SuggestTarget(IdFields("张三", "110101198801011234"), Members);
        Assert.Equal(OcrAssignmentPlanner.TargetKeys.Member("1"), result);
    }

    [Fact]
    public void SuggestTarget_NameHit_ReturnsRow()
    {
        var result = OcrAssignmentPlanner.SuggestTarget(IdFields("王五", ""), Members);
        Assert.Equal(OcrAssignmentPlanner.TargetKeys.Member("2"), result);
    }

    [Fact]
    public void SuggestTarget_NameHitHead_ReturnsHead()
    {
        var result = OcrAssignmentPlanner.SuggestTarget(IdFields("张三", "999999999999999999"), Members);
        Assert.Equal(OcrAssignmentPlanner.TargetKeys.Head, result);
    }

    [Fact]
    public void SuggestTarget_NoMatch_ReturnsNewSupporter()
    {
        var result = OcrAssignmentPlanner.SuggestTarget(IdFields("赵六", "110101199901011234"), Members);
        Assert.Equal(OcrAssignmentPlanner.TargetKeys.NewSupporter, result);
    }

    [Fact]
    public void SuggestTarget_BankRecord_ReturnsNull()
    {
        // 表单类记录（银行卡）不参与人物匹配
        var fields = new Dictionary<string, string>
        {
            [OcrFieldKeys.BankName] = "中国银行",
            [OcrFieldKeys.CardNumber] = "6217003810012345678"
        };
        Assert.Null(OcrAssignmentPlanner.SuggestTarget(fields, Members));
    }

    [Fact]
    public void SuggestTarget_EmptyFields_ReturnsNull()
    {
        Assert.Null(OcrAssignmentPlanner.SuggestTarget(new Dictionary<string, string>(), Members));
    }

    [Fact]
    public void FindConflicts_DuplicateTarget_ReturnsKeyOnce()
    {
        var conflicts = OcrAssignmentPlanner.FindConflicts(new[]
        {
            "member:1", "head", "member:1", "head", "member:1"
        });
        Assert.Equal(new[] { "member:1", "head" }, conflicts);
    }

    [Fact]
    public void FindConflicts_IgnoreAndNull_NotConflict()
    {
        var conflicts = OcrAssignmentPlanner.FindConflicts(new[]
        {
            "ignore", null, "ignore", null
        });
        Assert.Empty(conflicts);
    }

    [Fact]
    public void FindConflicts_AllUnique_Empty()
    {
        var conflicts = OcrAssignmentPlanner.FindConflicts(new[] { "head", "member:1", "member:2" });
        Assert.Empty(conflicts);
    }

    [Fact]
    public void IsBlank_Works()
    {
        Assert.True(OcrAssignmentPlanner.IsBlank(null));
        Assert.True(OcrAssignmentPlanner.IsBlank(""));
        Assert.True(OcrAssignmentPlanner.IsBlank("  "));
        Assert.False(OcrAssignmentPlanner.IsBlank("x"));
    }

    [Fact]
    public void GetTrimmed_TrimsValue()
    {
        var fields = new Dictionary<string, string> { [OcrFieldKeys.Name] = " 张三 " };
        Assert.Equal("张三", OcrAssignmentPlanner.GetTrimmed(fields, OcrFieldKeys.Name));
        Assert.Equal(string.Empty, OcrAssignmentPlanner.GetTrimmed(fields, OcrFieldKeys.IdCard));
    }
}
