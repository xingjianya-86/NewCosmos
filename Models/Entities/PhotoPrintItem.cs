using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Entities;

/// <summary>照片打印版式选项（Picker 绑定用）。</summary>
public sealed record PhotoLayoutOption(string Code, string Name);

/// <summary>
/// 档案输出页「同时打印已上传照片」列表项：每张照片可单独勾选与选择打印版式（大小）。
/// </summary>
public partial class PhotoPrintItem : ObservableObject
{
    public long PhotoId { get; set; }

    /// <summary>照片类别代码（ID_CARD / HOUSEHOLD_REGISTER / ...）。</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>照片类别中文名。</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>照片备注（如"户口本第2页"）。</summary>
    public string PhotoName { get; set; } = string.Empty;

    public string CreatedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>该张照片的打印版式（整页/半页/证件原寸）。</summary>
    [ObservableProperty]
    private PhotoLayoutOption _selectedLayout = null!;

    public string DisplayText => string.IsNullOrWhiteSpace(PhotoName)
        ? $"{CategoryName}（{CreatedAtText}）"
        : $"{CategoryName} · {PhotoName}";
}
