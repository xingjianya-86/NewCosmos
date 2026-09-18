using System.Collections;
using Microsoft.Maui.Controls;

namespace NewCosmos.Converters;

/// <summary>
/// 列表行交替模板选择器：按项在 CollectionView.ItemsSource 中的索引奇偶选择 Even/Odd 模板，
/// 实现白 / 浅灰（#F8F9FA）交替行色。不依赖 x:Reference，规避 SourceGen 下 DataTemplate 命名作用域问题。
/// 用法：定义 Even/Odd 两个 DataTemplate（内容相同、根背景不同），并设置 EvenTemplate/OddTemplate。
/// </summary>
public class AlternatingRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate EvenTemplate { get; set; } = null!;

    public DataTemplate OddTemplate { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (container is CollectionView cv && cv.ItemsSource is IList list)
        {
            var index = list.IndexOf(item);
            return index >= 0 && index % 2 == 1 ? OddTemplate : EvenTemplate;
        }
        return EvenTemplate;
    }
}