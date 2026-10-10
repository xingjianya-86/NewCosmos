namespace NewCosmos.Services.Core;

/// <summary>
/// 输出根一次性迁移与预览临时目录清理。
/// 背景：文档输出根先从历史相对"输出"目录切换为 config/document_output.yaml 的 output.base_directory（2026-10），
/// 后支持用户在「系统设置」改到任意目录（用户级覆盖 document_output.user.json，2026-10）。
/// 旧根判定与状态记录见 OutputRootMigrationService（{AppData}\output_root_state.json）。
/// </summary>
public interface IOutputRootMigrationService
{
    /// <summary>
    /// 启动时一次性迁移：把状态文件记录的旧根（无状态文件=旧默认 {AppData}/CosmosApp/ArchiveOutput）
    /// 复制到当前输出根，并改写留痕表路径列（nc_biz_print_records.file_path/pdf_path、
    /// nc_biz_archive_files.output_path、nc_biz_asset_report_history.file_path）；
    /// 随后处理历史相对"输出"目录。
    /// 幂等：状态文件已等于当前根即跳过；DB 改写失败不更新状态，下次启动重试。
    /// </summary>
    Task MigrateLegacyOutputAsync(CancellationToken ct = default);

    /// <summary>
    /// 用户在「系统设置」改输出根后的即时迁移（调用方须先 OutputPathHelper.Configure 新根）：
    /// 旧根 → 新根复制 + 留痕表路径改写 + 清理旧根预览临时文件，并更新状态文件。
    /// 失败仅记日志，状态不更新 → 下次启动重试；新旧根互为包含时拒绝（防自复制死循环）。
    /// </summary>
    Task MigrateToAsync(CancellationToken ct = default);

    /// <summary>
    /// 按 document_output.yaml 的 cleanup 规则清理预览临时目录
    /// （超龄文件删除 + 总数上限），每次启动执行；失败仅记日志。
    /// </summary>
    void CleanupTempDirectory();
}
