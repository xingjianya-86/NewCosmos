using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 高龄信息变更单（nc_biz_elderly_info_change_records）。
/// 一次提交一行；字段明细见 <see cref="ElderlyInfoChangeDetail"/>，共用 ChangeNo。
/// </summary>
public class ElderlyInfoChangeRecord
{
    public long Id { get; set; }
    public string ChangeNo { get; set; } = string.Empty;
    public long? CampaignId { get; set; }
    public long? TaskId { get; set; }
    public long? ApplicationId { get; set; }
    public string IdCard { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ChangeType { get; set; } = ElderlyInfoChangeConstants.RiskLow;
    public string? Reason { get; set; }
    public string Status { get; set; } = ElderlyInfoChangeConstants.StatusPending;
    public string? SourceType { get; set; }
    public string? SourceFile { get; set; }
    public int? SourceRow { get; set; }
    public string? EffectiveMonth { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewOpinion { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>字段明细（服务层查询后填充）。</summary>
    public List<ElderlyInfoChangeDetail> Details { get; set; } = new();

    public string StatusName => ElderlyInfoChangeConstants.GetStatusName(Status);
    public bool IsPending => string.Equals(Status, ElderlyInfoChangeConstants.StatusPending, StringComparison.Ordinal);
    public bool IsHighRisk => string.Equals(ChangeType, ElderlyInfoChangeConstants.RiskHigh, StringComparison.Ordinal);
    public string RiskName => IsHighRisk ? "高风险" : "低风险";
    public string DetailsSummary => Details.Count == 0
        ? "无字段明细"
        : $"{Details.Count} 项：{string.Join("、", Details.Select(d => d.FieldLabel))}";
    public string CreatedAtDisplay => CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
    public string ReviewedAtDisplay => ReviewedAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
    public string EffectiveMonthDisplay => string.IsNullOrWhiteSpace(EffectiveMonth) ? "-" : EffectiveMonth!;
    public string SourceDisplay => SourceType switch
    {
        ElderlyInfoChangeConstants.SourceCertificationImport => "认证导入",
        ElderlyInfoChangeConstants.SourceCertification => "年度认证",
        ElderlyInfoChangeConstants.SourceManual => "手工登记",
        null or "" => "手工登记",
        _ => SourceType!
    };
}

/// <summary>信息变更字段明细（nc_biz_elderly_info_change_details）。</summary>
public class ElderlyInfoChangeDetail
{
    public long Id { get; set; }
    public long ChangeId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string? FieldLabel { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime? CreatedAt { get; set; }

    public string Label => string.IsNullOrWhiteSpace(FieldLabel)
        ? ElderlyInfoChangeConstants.GetFieldLabel(FieldKey)
        : FieldLabel!;
    public bool IsHighRisk => ElderlyInfoChangeConstants.IsHighRisk(FieldKey);
    public string RiskName => IsHighRisk ? "高风险" : "低风险";
    public string OldValueDisplay => string.IsNullOrWhiteSpace(OldValue) ? "（空）" : OldValue!;
    public string NewValueDisplay => string.IsNullOrWhiteSpace(NewValue) ? "（空）" : NewValue!;
}

/// <summary>
/// 审批时锁定的申请主表当前值（仅信息变更允许字段 + 状态），避免把整行 380+ 列读入内存。
/// </summary>
public class ElderlyInfoChangeApplicationValues
{
    public long Id { get; set; }
    public string IdCard { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? FamilyAddress { get; set; }
    public string? DetailAddress { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? AgentName { get; set; }
    public string? AgentRelation { get; set; }
    public string? AgentReceiveName { get; set; }
    public string? AgentReceiveRelation { get; set; }
    public string? AgentReceiveBankName { get; set; }
    public string? AgentReceiveBankAccount { get; set; }

    /// <summary>按白名单字段 key 读取当前值（未命中返回空串）。</summary>
    public string GetValue(string fieldKey) => ElderlyInfoChangeConstants.NormalizeFieldKey(fieldKey) switch
    {
        ElderlyInfoChangeConstants.FieldPhone => Phone ?? string.Empty,
        ElderlyInfoChangeConstants.FieldFamilyAddress => FamilyAddress ?? string.Empty,
        ElderlyInfoChangeConstants.FieldDetailAddress => DetailAddress ?? string.Empty,
        ElderlyInfoChangeConstants.FieldBankName => BankName ?? string.Empty,
        ElderlyInfoChangeConstants.FieldBankAccount => BankAccount ?? string.Empty,
        ElderlyInfoChangeConstants.FieldAgentName => AgentName ?? string.Empty,
        ElderlyInfoChangeConstants.FieldAgentRelation => AgentRelation ?? string.Empty,
        ElderlyInfoChangeConstants.FieldAgentReceiveName => AgentReceiveName ?? string.Empty,
        ElderlyInfoChangeConstants.FieldAgentReceiveRelation => AgentReceiveRelation ?? string.Empty,
        ElderlyInfoChangeConstants.FieldAgentReceiveBankName => AgentReceiveBankName ?? string.Empty,
        ElderlyInfoChangeConstants.FieldAgentReceiveBankAccount => AgentReceiveBankAccount ?? string.Empty,
        _ => string.Empty
    };
}

/// <summary>创建变更单时的字段输入（旧值可空，空则由服务端取申请主表当前值）。</summary>
public class ElderlyInfoChangeDetailInput
{
    public string FieldKey { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
