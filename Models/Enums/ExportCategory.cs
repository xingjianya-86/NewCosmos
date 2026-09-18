namespace NewCosmos.Models.Enums;

/// <summary>
/// 导出分类枚举
/// 定义不同业务场景的导出目    /// </summary>
public enum ExportCategory
{
    /// <summary>
    /// Schema检查报    /// </summary>
    SchemaReport,

    /// <summary>
    /// 档案导出
    /// </summary>
    ArchiveExport,

    /// <summary>
    /// 月报    /// </summary>
    MonthlyReport,

    /// <summary>
    /// 数据备份
    /// </summary>
    DataBackup,

    /// <summary>
    /// 系统日志
    /// </summary>
    SystemLog
}
