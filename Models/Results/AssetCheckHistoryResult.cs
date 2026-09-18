using System.Collections.Generic;

namespace NewCosmos.Models.Results;

public class AssetCheckHistoryResult
{
    public string BatchId { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public string ApplicantIdType { get; set; } = string.Empty;
    public string ApplicantIdCard { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;

    public string FamilyAddress { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string ApplicationReason { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;

    public List<FamilyMemberResult> FamilyMembers { get; set; } = new();

    public bool HasAgent { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentIdCard { get; set; } = string.Empty;
    public string AgentRelationship { get; set; } = string.Empty;
    public string AgentIdType { get; set; } = string.Empty;
}

public class FamilyMemberResult
{
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string IdType { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public bool IsHead { get; set; }
}