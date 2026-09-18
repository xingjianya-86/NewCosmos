using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

public interface ISchemaSyncService
{
    /// <summary>
    /// 按 Resources/Schema YAML（经 ISchemaService 合并加载）中的规范定义重建表结构
    /// （DROP + CREATE，破坏性操作）。YAML 是表结构的唯一权威来源。
    /// 表已存在时必须显式传入 allowDrop=true 才会删除重建，否则返回失败以防数据丢失；
    /// DROP 与 CREATE 在同一事务内执行（调用方已有环境事务时直接加入）。
    /// </summary>
    Task<Result> SyncTableSchemaAsync(
        string tableName,
        bool allowDrop = false,
        IProgress<ProgressContext> progress = null,
        CancellationToken ct = default);
}
