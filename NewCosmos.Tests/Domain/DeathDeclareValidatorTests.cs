using NewCosmos.Constants;
using NewCosmos.Services.Domain.ArchiveManagement;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 申报提交前置校验（DeathDeclareValidator）：
/// 9 段既有顺序校验 + 2026-11-08 规范化格式追加的条目二（遗体处置分支）与条目五（矛盾纠纷）两段硬门。
/// 约定：首条失败即返回，全部通过返回 null。
/// </summary>
public class DeathDeclareValidatorTests
{
    private const string ValidIdCard = "11010519491231002X"; // IdCardValidatorTests 同款合法样例

    /// <summary>构造一份各段均合法的输入（各测试只覆写其关注的段）；
    /// omitDeathTime=true 表示未选择死亡时刻（DeathDateTime 保持 null，模拟组件未选）</summary>
    private static DeathDeclareValidator.Input Valid(
        string? name = "张三",
        string? idCard = ValidIdCard,
        DateTime? deathDateTime = null,
        bool omitDeathTime = false,
        string? declaredByName = "李四",
        string? declaredByIdCard = ValidIdCard,
        string? declaredByType = "村民委员会",
        string? reporterName = "王五",
        bool isTownScope = false,
        string? declaredVillage = null,
        int businessOptionCount = 0,
        int businessCheckedCount = 0,
        string? dispositionBranch = DeathDeclarationConstants.DispositionCremation,
        DateTime? cremationDate = null,
        bool omitCremationDate = false,
        string? cremationPlace = "XX殡仪馆",
        DateTime? burialDate = null,
        string? burialPlace = null,
        string? remainsArrangement = null,
        string? disputeStatus = DeathDeclarationConstants.DisputeNone,
        string? disputeDesc = null) => new()
    {
        Name = name,
        IdCard = idCard,
        DeathDateTime = omitDeathTime ? null : (deathDateTime ?? DateTime.Now.AddDays(-1)),
        DeclaredByName = declaredByName,
        DeclaredByIdCard = declaredByIdCard,
        DeclaredByType = declaredByType,
        ReporterName = reporterName,
        IsTownScope = isTownScope,
        DeclaredVillage = declaredVillage,
        BusinessMatterOptionCount = businessOptionCount,
        BusinessMatterCheckedCount = businessCheckedCount,
        DispositionBranch = dispositionBranch,
        CremationDate = omitCremationDate ? null : (cremationDate ?? DateTime.Today.AddDays(-1)),
        CremationPlace = cremationPlace,
        BurialDate = burialDate,
        BurialPlace = burialPlace,
        RemainsArrangement = remainsArrangement,
        DisputeStatus = disputeStatus,
        DisputeDesc = disputeDesc,
        DisputeNone = DeathDeclarationConstants.DisputeNone,
        DisputeHas = DeathDeclarationConstants.DisputeHas
    };

    // ── 全段通过 ──

    [Fact]
    public void Validate_各段合法_返回null()
        => Assert.Null(DeathDeclareValidator.Validate(Valid()));

    // ── 既有 9 段（顺序敏感：首条失败即返回）──

    [Fact]
    public void Validate_姓名为空_返回姓名提示() =>
        Assert.Equal("请填写死者姓名", DeathDeclareValidator.Validate(Valid(name: "  ")));

    [Fact]
    public void Validate_身份证为空_返回身份证提示() =>
        Assert.Equal("请填写死者身份证号", DeathDeclareValidator.Validate(Valid(idCard: "")));

    [Fact]
    public void Validate_死亡时刻未选_返回选时刻提示() =>
        Assert.Equal("请选择死亡日期与时间", DeathDeclareValidator.Validate(Valid(omitDeathTime: true)));

    [Fact]
    public void Validate_死亡时刻为null时优先于姓名之外的段_仍先报姓名()
    {
        // 首条失败即返回：姓名与时刻同时缺失，先报姓名
        Assert.Equal("请填写死者姓名",
            DeathDeclareValidator.Validate(Valid(name: null, omitDeathTime: true)));
    }

    [Fact]
    public void Validate_死亡时刻在未来_返回时序提示() =>
        Assert.Equal("死亡时间不能晚于当前时间",
            DeathDeclareValidator.Validate(Valid(deathDateTime: DateTime.Now.AddHours(1))));

    [Fact]
    public void Validate_申报人身份证非法_返回校验提示() =>
        Assert.Equal("申报人身份证号校验不通过，请核对后重新填写",
            DeathDeclareValidator.Validate(Valid(declaredByIdCard: "123456")));

    [Fact]
    public void Validate_业务事项有选项但零勾选_返回勾选提示() =>
        Assert.Equal("请至少勾选一项相关业务事项",
            DeathDeclareValidator.Validate(Valid(businessOptionCount: 5, businessCheckedCount: 0)));

    [Fact]
    public void Validate_业务事项字典未加载_不拦()
    {
        // 选项数 0 = 字典未加载 → 该项放行（防种子未播种卡死提交）
        Assert.Null(DeathDeclareValidator.Validate(Valid(businessOptionCount: 0, businessCheckedCount: 0)));
    }

    [Fact]
    public void Validate_报告人为空_返回报告人提示() =>
        Assert.Equal("请填写发现（报告）人姓名", DeathDeclareValidator.Validate(Valid(reporterName: "")));

    [Fact]
    public void Validate_镇级范围缺所在村_返回所在村提示() =>
        Assert.Equal("当前组织未绑定村社区，请填写「所在村」（文书落款与抬头需要）",
            DeathDeclareValidator.Validate(Valid(isTownScope: true, declaredVillage: "")));

    // ── 条目二 遗体处置（2026-11-08 规范化格式）──

    [Fact]
    public void Validate_处置方式未选_返回处置提示() =>
        Assert.Equal("请选择遗体处置方式（火化 / 安葬 / 其他）",
            DeathDeclareValidator.Validate(Valid(dispositionBranch: "")));

    [Fact]
    public void Validate_火化分支缺日期_返回火化日期提示() =>
        Assert.Equal("请选择火化日期",
            DeathDeclareValidator.Validate(Valid(dispositionBranch: DeathDeclarationConstants.DispositionCremation, omitCremationDate: true)));

    [Fact]
    public void Validate_火化分支缺地点_返回火化地点提示() =>
        Assert.Equal("请填写火化地点（殡仪馆）",
            DeathDeclareValidator.Validate(Valid(dispositionBranch: DeathDeclarationConstants.DispositionCremation, cremationPlace: "")));

    [Fact]
    public void Validate_安葬分支缺日期_返回安葬日期提示() =>
        Assert.Equal("请选择安葬日期（已安葬或拟安葬）",
            DeathDeclareValidator.Validate(Valid(dispositionBranch: DeathDeclarationConstants.DispositionBurial, burialDate: null)));

    [Fact]
    public void Validate_安葬分支缺地点_返回安葬地点提示() =>
        Assert.Equal("请填写安葬地点",
            DeathDeclareValidator.Validate(Valid(
                dispositionBranch: DeathDeclarationConstants.DispositionBurial,
                burialDate: DateTime.Today.AddDays(7), burialPlace: "")));

    [Fact]
    public void Validate_安葬分支允许未来日期()
    {
        // 拟安葬：未来日期通过日期必填校验（组件侧上限由 BurialDateLimit 放开，此处只验必填语义）
        Assert.Null(DeathDeclareValidator.Validate(Valid(
            dispositionBranch: DeathDeclarationConstants.DispositionBurial,
            burialDate: DateTime.Today.AddDays(7), burialPlace: "XX公益性墓地")));
    }

    [Fact]
    public void Validate_其他分支缺说明_返回说明提示() =>
        Assert.Equal("处置方式为「其他」时请填写遗体处置说明",
            DeathDeclareValidator.Validate(Valid(dispositionBranch: DeathDeclarationConstants.DispositionOther, remainsArrangement: "")));

    [Fact]
    public void Validate_其他分支有说明_通过()
    {
        Assert.Null(DeathDeclareValidator.Validate(Valid(
            dispositionBranch: DeathDeclarationConstants.DispositionOther,
            remainsArrangement: "现停放于XX殡仪馆，拟于2026年11月20日安葬于XX公益性墓地")));
    }

    // ── 条目五 矛盾纠纷（2026-11-08 规范化格式）──

    [Fact]
    public void Validate_矛盾纠纷未选_返回矛盾提示() =>
        Assert.Equal("请选择矛盾纠纷及信访隐患情况（无 / 有）",
            DeathDeclareValidator.Validate(Valid(disputeStatus: "")));

    [Fact]
    public void Validate_矛盾纠纷为有但缺说明_返回说明必填提示() =>
        Assert.Equal("勾选「有」时请填写矛盾纠纷及信访隐患说明",
            DeathDeclareValidator.Validate(Valid(disputeStatus: DeathDeclarationConstants.DisputeHas, disputeDesc: "")));

    [Fact]
    public void Validate_矛盾纠纷为有且有说明_通过()
    {
        Assert.Null(DeathDeclareValidator.Validate(Valid(
            disputeStatus: DeathDeclarationConstants.DisputeHas, disputeDesc: "死者家属与邻居因地界有纠纷，已调解")));
    }

    [Fact]
    public void Validate_矛盾纠纷为无_不要求说明()
        => Assert.Null(DeathDeclareValidator.Validate(Valid(disputeStatus: DeathDeclarationConstants.DisputeNone, disputeDesc: "")));

    // ── 顺序：条目二/五 排在既有段之后（姓名缺失时报姓名，不报条目二）──

    [Fact]
    public void Validate_姓名缺失优先于条目二校验() =>
        Assert.Equal("请填写死者姓名", DeathDeclareValidator.Validate(Valid(name: null, dispositionBranch: "")));
}
