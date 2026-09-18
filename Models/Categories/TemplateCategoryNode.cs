using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace NewCosmos.Models.Categories;

public partial class TemplateCategoryNode : ObservableObject
{
    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLeaf;

    [ObservableProperty]
    private int _depth;

    public Action OnExpandToggled { get; set; } = () => { };

    partial void OnIsExpandedChanged(bool value)
    {
        OnExpandToggled?.Invoke();
    }

    [RelayCommand]
    private void ToggleSelf()
    {
        IsExpanded = !IsExpanded;
    }

    public ObservableCollection<TemplateCategoryNode> Children { get; } = new();

    partial void OnIsCheckedChanged(bool value)
    {
        SetChildrenChecked(this, value);
    }

    private static void SetChildrenChecked(TemplateCategoryNode node, bool value)
    {
        foreach (var child in node.Children)
        {
            child.IsChecked = value;
            SetChildrenChecked(child, value);
        }
    }

    public IEnumerable<TemplateCategoryNode> Flatten()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var c in child.Flatten())
                yield return c;
    }

    public IEnumerable<string> GetSelectedPaths()
    {
        foreach (var child in Children)
        {
            foreach (var p in child.CollectChecked())
            {
                yield return p;
            }
        }
    }

    private IEnumerable<string> CollectChecked()
    {
        if (IsChecked && IsLeaf)
        {
            yield return Path;
        }
        foreach (var child in Children)
        {
            foreach (var p in child.CollectChecked())
            {
                yield return p;
            }
        }
    }
}