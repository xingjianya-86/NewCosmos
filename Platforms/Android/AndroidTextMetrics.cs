using Android.Views;
using Android.Widget;

namespace NewCosmos.Platforms.Android;

/// <summary>
/// Android 文字行盒工具：递归关闭子树 TextView 的 includeFontPadding。
/// 主流文字控件已由 MauiProgram 的 ViewHandler.ViewMapper 统一处理；
/// SearchBar 的平台控件是 SearchView（ViewGroup，非 TextView），文本在其内部 EditText 上，
/// Mapper 里拿不到，故递归查找补上（全库仅 UnifiedReprintPage 一处用 SearchBar）。
/// </summary>
internal static class AndroidTextMetrics
{
    /// <summary>递归将 <paramref name="group"/> 子树内所有 TextView 的 IncludeFontPadding 置 false。</summary>
    public static void DisableFontPadding(ViewGroup group)
    {
        for (var i = 0; i < group.ChildCount; i++)
        {
            switch (group.GetChildAt(i))
            {
                case ViewGroup child:
                    DisableFontPadding(child);
                    break;
                case TextView tv:
                    tv.SetIncludeFontPadding(false); // .NET 10 绑定 IncludeFontPadding 无 setter，走方法形态
                    break;
            }
        }
    }
}
