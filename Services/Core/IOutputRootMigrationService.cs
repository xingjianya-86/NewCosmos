namespace NewCosmos.Services.Core;

/// <summary>
/// 输出根一次性迁移与预览临时目录清理。
/// 背景：文档输出根从历史相对"输出"目录切换为 config/document_output.yaml 的 output.base_directory（2026-10）。
/// </summary>
public interface IOutputRootMigrationService
{
    /// <summary>
    /// 一次性迁移：复制旧"输出"目录全部文件到新输出根（不删除源），
    /// 并改写留痕表路径列（nc_biz_print_records.file_path/pdf_path、
    /// nc_biz_archive_files.output_path、nc_biz_asset_report_history.file_path）。
    /// 幂等：新根下存在 .output_migration.json 标记即跳过；DB 改写失败不写标记，下次启动重试。
    /// </summary>
    Task MigrateLegacyOutputAsync(CancellationToken ct = default);

    /// <summary>
    /// 按 document_output.yaml 的 cleanup 规则清理预览临时目录
    /// （超龄文件删除 + 总数上限），每次启动执行；失败仅记日志。
    /// </summary>
    void CleanupTempDirectory();
}
