namespace NewCosmos.Constants;

/// <summary>
/// 文档动作统一文案（四件套：预览 / 打印 / 保存 / 全部保存）。
/// 带 PDF 预览的文书生成功能（档案输出、档案查询、历史补打、月报表、高龄月报、资产核查）
/// 的按钮文案一律取自这里，禁止 XAML 各自硬编码同义词。
/// 语义见 docs\20261007_文档动作与输出路径统一规范.md。
/// </summary>
public static class DocumentActionText
{
    /// <summary>生成临时 PDF 并在页面内预览（产物落预览临时目录）</summary>
    public const string Preview = "预览";

    /// <summary>预览当前勾选项（多选列表页）</summary>
    public const string PreviewSelected = "预览选中";

    /// <summary>生成 + 落盘固定输出根 + 调打印机</summary>
    public const string Print = "打印";

    /// <summary>打印当前勾选项（多选列表页）</summary>
    public const string PrintSelected = "打印选中";

    /// <summary>仅生成落盘到固定输出根，不打印</summary>
    public const string Save = "保存";

    /// <summary>仅生成勾选项落盘（多选列表页），不打印</summary>
    public const string SaveSelected = "保存选中";

    /// <summary>仅生成全部落盘，不打印</summary>
    public const string SaveAll = "全部保存";

    /// <summary>打开固定输出根目录（替代"选择导出目录"交互）</summary>
    public const string OpenOutputFolder = "打开输出目录";
}
