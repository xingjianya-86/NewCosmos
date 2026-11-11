namespace NewCosmos.Constants;

/// <summary>
/// 公文字体检查常量（单一来源，csproj 打包与运行时检测共用，禁散落魔法值）。
/// 字体目录为本机资产（gitignore 不入库，AGENTS §2），随包分发仅影响本机构建产物。
/// </summary>
public static class OfficialFontConstants
{
    /// <summary>公文字体目录名（相对安装目录；与 csproj 的 None Include 路径一致）</summary>
    public const string FontDirectoryName = "公文字体";

    /// <summary>单字体文件扩展名（小写，比较时忽略大小写）</summary>
    public const string TtfExtension = ".ttf";

    /// <summary>字体集合扩展名（TTC：一个文件含多个字体，取首个解析族名）</summary>
    public const string TtcExtension = ".ttc";

    /// <summary>一键安装目标：当前用户字体目录（免管理员，HKCU 注册表配套）</summary>
    public const string UserFontInstallDirectoryName = "Microsoft\\Windows\\Fonts";

    /// <summary>HKCU 字体注册表键（Windows NT 当前版本字体表，值名=族名+后缀，值数据=文件名）</summary>
    public const string UserFontRegistryKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

    /// <summary>注册表值名后缀（Windows 约定）</summary>
    public const string RegistryValueSuffix = " (TrueType)";
}
