using System.Collections.Generic;

namespace NewCosmos.Models.Requests;

/// <summary>
/// 快速核查提交请求    /// </summary>
public class QuickAssetCheckSubmitRequest
{
    /// <summary>
    /// 申请日期
    /// </summary>
    public DateTime ApplicationDate { get; set; } = DateTime.Today;

    /// <summary>
    /// 家庭地址（完整地址）    /// </summary>
    public string FamilyAddress { get; set; } = string.Empty;

    /// <summary>
    /// 申请原因
    /// </summary>
    public string ApplicationReason { get; set; } = string.Empty;

    /// <summary>
    /// 村社区    /// </summary>
    public string Community { get; set; } = string.Empty;

    /// <summary>
    /// 联系电话
    /// </summary>
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>
    /// 授权人（家庭成员）列表    /// </summary>
    public List<AssetCheckItemDto> Applicants { get; set; } = new();

    /// <summary>
    /// 是否委托代理    /// </summary>
    public bool IsAgent { get; set; }

    /// <summary>
    /// 代理人姓名    /// </summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>
    /// 代理人身份证号    /// </summary>
    public string AgentIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 代理人关系    /// </summary>
    public string AgentRelationship { get; set; } = string.Empty;

    /// <summary>
    /// 代理人证件类型    /// </summary>
    public string AgentIdType { get; set; } = "ResidentIdCard";

    /// <summary>
    /// 操作员用户ID
    /// </summary>
    public int? OperatorUserId { get; set; }

    /// <summary>
    /// 操作员姓名    /// </summary>
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>
    /// 操作员账号    /// </summary>
    public string OperatorAccount { get; set; } = string.Empty;

    /// <summary>
    /// 操作员单位名称    /// </summary>
    public string OperatorUnitName { get; set; } = string.Empty;

    /// <summary>
    /// 批次号    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// 核查年份
    /// </summary>
    public int VerificationYear { get; set; } = DateTime.Today.Year;

    /// <summary>
    /// 核查月份
    /// </summary>
    public int VerificationMonth { get; set; } = DateTime.Today.Month;
}