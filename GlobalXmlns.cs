using Microsoft.Maui.Controls;
using XmlnsPrefixAttribute = Microsoft.Maui.Controls.XmlnsPrefixAttribute;

// .NET 10 GlobalXmlns：全项目 XML 命名空间聚合
// XAML 根命名空间统一为 http://schemas.microsoft.com/dotnet/maui/global，
// 各文件无需再声明 xmlns: 样板。
// 参考：https://learn.microsoft.com/dotnet/maui/whats-new/dotnet-10
//
// 注意（语义与 WPF 一致）：
//   XmlnsDefinition(xmlNamespace, clrNamespace) —— 把 CLR 命名空间的类型注入指定 XML 命名空间；
//   XmlnsPrefix(xmlNamespace, prefix)           —— 给 XML 命名空间起前缀（非 CLR 命名空间！）。
// 因此以下全局前缀都指向同一个 global 命名空间，前缀仅作书写习惯保留。

// ── 注入 global 命名空间的 CLR 命名空间 ──
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Components")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Controls")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Converters")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Constants")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Models")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Models.Categories")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Models.Requests")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Models.Results")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Pages.SocialAssistance")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Services.Domain.AssetVerification")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Services.Domain.ChangeManagement")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Services.Domain.UserManagement")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.Services.System")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.ArchiveManagement")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.AssetVerification")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.Auth")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.ChangeManagement")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.Config")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.DatabaseManagement")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.ElderlyBenefits")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.Main")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.Reprint")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.Reprint.Providers")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.Reporting")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.Recovery")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.Shared")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.SocialAssistance")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.TempRelief")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "NewCosmos.ViewModels.UserManagement")]

// 不注入的命名空间（重名消歧，相关文件保留经典 clr-namespace 声明）：
//   NewCosmos.Models.Entities               —— Application 与 Microsoft.Maui.Controls.Application 重名
//   NewCosmos.Models.Entities.UserManagement —— Role 与 Services.Domain.UserManagement.Role 重名

// ── global 命名空间的全局前缀（前缀 → 同一 global URI） ──
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "components")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "controls")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "converters")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "constants")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "cat")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "results")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "pages")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "assetsvc")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "chg")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "domain")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/maui/global", "sys")]
