using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Helpers;

/// <summary>
/// 档案链"在保前分类"解析（单点）：《档案_变更告知书》"停止/不予认定"方向判定（装配端
/// ArchiveProductionViewModel）与归档终结拒保补写 CategoryStop（ApplicationStatusService）共用，
/// 两侧口径永不劈叉。取数顺序：
/// ① 链上（application_id OR new_application_id）最近一条 old_classification 非空的变更记录；
/// ② 回退 original_application_id 上游链上**离本档最近**且分类非空的祖先档案 classification_result
///    （覆盖"停旧建新产物的创建记录 old_classification 为空"的存量行）。
/// 两者皆无 → Success(null)（调用方按"无在保前分类"处理：告知书出"不予认定"、补写跳过）；
/// 查询失败 → Failure（§7 显式失败，装配端降级告警、归档写入侧回滚）。
/// </summary>
public static class ChainPreClassification
{
    public static async Task<Result<string?>> ResolveAsync(
        IDatabaseService db, long applicationId, CancellationToken ct = default)
    {
        // ① 链上最近一条非空 old_classification（死亡/成员变更/复核等记录的"变更前分类"）
        const string fromRecordsSql = @"SELECT old_classification FROM nc_biz_change_records
                               WHERE (application_id = $1 OR new_application_id = $1)
                                 AND deleted_at IS NULL
                                 AND old_classification IS NOT NULL AND old_classification <> ''
                               ORDER BY changed_at DESC, id DESC LIMIT 1";
        var recordResult = await db.QuerySingleAsync<string>(fromRecordsSql, ct, applicationId);
        if (recordResult.IsFailure)
            return Result.Failure<string?>(recordResult.ErrorCode!, recordResult.Message!);
        if (!string.IsNullOrEmpty(recordResult.Value))
            return Result.Success<string?>(recordResult.Value);

        // ② 上游档案链（沿 original_application_id 递归到祖先，depth 最小=离本档最近）。
        //    祖先不带 deleted_at 过滤：软删档案的分类仍是本档的"变更前事实"。
        const string fromAncestorSql = @"WITH RECURSIVE anc AS (
                SELECT id, original_application_id, classification_result, 0 AS depth
                FROM nc_biz_applications WHERE id = $1
                UNION ALL
                SELECT p.id, p.original_application_id, p.classification_result, c.depth + 1
                FROM nc_biz_applications p
                JOIN anc c ON p.id = c.original_application_id
            )
            SELECT classification_result FROM anc
            WHERE depth > 0 AND classification_result IS NOT NULL AND classification_result <> ''
            ORDER BY depth ASC LIMIT 1";
        var ancestorResult = await db.QuerySingleAsync<string>(fromAncestorSql, ct, applicationId);
        if (ancestorResult.IsFailure)
            return Result.Failure<string?>(ancestorResult.ErrorCode!, ancestorResult.Message!);
        return Result.Success<string?>(ancestorResult.Value);
    }
}
