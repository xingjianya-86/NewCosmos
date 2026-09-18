using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

public interface ILandContractService
{
    Task<Result<List<LandConfirmationRecord>>> GetRecordsByNamesAsync(
        List<string> names, CancellationToken ct = default);

    // 原 GetRecordsByIdCardsAsync 已删除：底层表无身份证列，方法不可实现且无调用方（见 LandContractService 注释）
}
