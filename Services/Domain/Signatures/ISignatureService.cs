using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Signatures;

/// <summary>
/// 签名留痕服务：保存手写签名 PNG，供文书渲染时按业务上下文取回并嵌入。
/// 签名为生物特征留痕数据，仅存 PNG 图像，不做比对识别。
/// </summary>
public interface ISignatureService
{
    /// <summary>保存一条签名，返回新记录 ID。</summary>
    Task<Result<long>> SaveSignatureAsync(Signature signature, CancellationToken ct = default);

    /// <summary>
    /// 取回指定上下文的最新签名（通常 role=本人）。
    /// businessId 为空时仅按身份证号 + 业务类型 + 角色匹配最近一条。
    /// </summary>
    Task<Result<Signature?>> GetLatestAsync(
        string applicantIdCard,
        string businessType,
        long? businessId = null,
        string signerRole = "本人",
        CancellationToken ct = default);
}
