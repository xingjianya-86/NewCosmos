using NewCosmos.Models.Results;
using NewCosmos.Models.Schema;

namespace NewCosmos.Services.Core;

public interface ISeedMergeService
{
    Task<Result<MergeResult>> MergeSeedDataAsync(
        SeedMergeDefinition definition,
        List<Dictionary<string, object>> seedRows,
        CancellationToken ct = default);
}
