using NewCosmos.Constants;
using NewCosmos.Models.Exceptions;
using NewCosmos.Models.Options;

namespace NewCosmos.Services.Core;

/// <summary>
/// 配置服务实现
/// 统一管理所有INI配置文件
/// </summary>
public class ConfigService : IConfigService
{
    /// <summary>加密密码的前缀标记：Password=enc:&lt;base64&gt;</summary>
    private const string EncryptedPasswordPrefix = "enc:";

    /// <summary>DPAPI 附加熵（防止其他程序用同一 DPAPI 范围直接解密）</summary>
    private static readonly byte[] PasswordEntropy = global::System.Text.Encoding.UTF8.GetBytes("NewCosmos.DbCredential.v1");

    private readonly string _configDirectory;
    
    private AppOptions _appOptions = null!;
    private DatabaseOptions _databaseOptions = null!;
    private StorageOptions _storageOptions = null!;
    private UIOptions _uiOptions = null!;
    private PerformanceOptions _performanceOptions = null!;
    private PreferencesOptions _preferencesOptions = null!;
    private SchemaOptions _schemaOptions = null!;
    private NetworkOptions _networkOptions = null!;
    private UpdateOptions _updateOptions = null!;

    /// <summary>INI 文件内容缓存：app.ini 会被 4 个 Get*Options 各读一遍，缓存后进程内只读一次磁盘</summary>
    private readonly Dictionary<string, string[]> _iniLinesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _iniCacheLock = new();

    public ConfigService()
    {
        _configDirectory = GetConfigDirectory();
        Serilog.Log.Information("[ConfigService] 配置目录: {ConfigDirectory}", _configDirectory);
    }

    /// <summary>
    /// 读取 INI 文件行（带缓存），并对跨 section 的重名键发出告警——
    /// 本解析器按 key 全文件匹配、忽略 section，跨段重名会静默后者覆盖前者。
    /// </summary>
    private string[] ReadIniLines(string filePath)
    {
        lock (_iniCacheLock)
        {
            if (_iniLinesCache.TryGetValue(filePath, out var cached))
                return cached;

            var lines = File.ReadAllLines(filePath);
            _iniLinesCache[filePath] = lines;

            // 重名键检测（仅告警，不改变行为）
            var seenKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var currentSection = "";
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    currentSection = trimmed;
                    continue;
                }
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;
                var parts = trimmed.Split('=', 2);
                if (parts.Length != 2)
                    continue;
                var key = parts[0].Trim();
                if (seenKeys.TryGetValue(key, out var firstSection) && firstSection != currentSection)
                {
                    Serilog.Log.Warning("[ConfigService] {File} 中键 {Key} 在 {S1} 与 {S2} 重复出现，后者将覆盖前者",
                        Path.GetFileName(filePath), key, firstSection, currentSection);
                }
                else
                {
                    seenKeys[key] = currentSection;
                }
            }

            return lines;
        }
    }

    private string GetConfigDirectory()
    {
        var productionConfigPath = Path.Combine(AppContext.BaseDirectory, "config");

        // database.ini 不再复制进构建输出（凭据不随安装包分发），
        // 优先选择真正包含 database.ini 的目录：
        // - 部署机：exe 旁 config/ 内有向导生成的 database.ini → 用它
        // - 开发机：从输出目录逐级向上探测项目 config/（MAUI 输出含 RID 段，
        //   深度为 bin/Debug/net9.0-.../win10-x64 共 4 级，固定"../../../"会探空）
        if (File.Exists(Path.Combine(productionConfigPath, "database.ini")))
            return Path.GetFullPath(productionConfigPath);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "database.ini")))
                return Path.GetFullPath(candidate);
        }

        return Path.GetFullPath(productionConfigPath);
    }

    public AppOptions GetAppOptions()
    {
        if (_appOptions != null)
            return _appOptions;

        var filePath = Path.Combine(_configDirectory, "app.ini");
        
        if (!File.Exists(filePath))
            throw new ConfigurationException("app.ini", $"配置文件不存在: {filePath}");

        _appOptions = new AppOptions();
        
        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "Version": _appOptions.Version = value; break;
                case "ApplicationName": _appOptions.ApplicationName = value; break;
                case "WindowTitle": _appOptions.WindowTitle = value; break;
                case "BCycleSettleDay":
                    // B 线统计结算日（默认 15；上级业务截止 20 号 → 20）。非法值回退默认并告警，不阻断启动。
                    if (int.TryParse(value, out var settleDay)
                        && settleDay >= BusinessCycleConstants.MinSettleDay
                        && settleDay <= BusinessCycleConstants.MaxSettleDay)
                    {
                        _appOptions.BCycleSettleDay = settleDay;
                    }
                    else
                    {
                        Serilog.Log.Warning("[ConfigService] app.ini BCycleSettleDay 配置无效: {Value}，使用默认 {Default}",
                            value, BusinessCycleConstants.DefaultSettleDay);
                    }
                    break;
            }
        }

        _appOptions.Validate();
        Serilog.Log.Information("[ConfigService] 应用配置加载完成: Version={Version}", _appOptions.Version);
        
        return _appOptions;
    }

    public DatabaseOptions GetDatabaseOptions()
    {
        if (_databaseOptions != null)
            return _databaseOptions;

        var filePath = Path.Combine(_configDirectory, "database.ini");
        
        if (!File.Exists(filePath))
            throw new ConfigurationException("database.ini", $"数据库配置文件不存在: {filePath}");

        _databaseOptions = new DatabaseOptions();
        
        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "Host": _databaseOptions.Host = value; break;
                case "Port": if (int.TryParse(value, out var port)) _databaseOptions.Port = port; break;
                case "DatabaseName": _databaseOptions.DatabaseName = value; break;
                case "Username": _databaseOptions.Username = value; break;
                case "Password": _databaseOptions.Password = value; break;
                case "ConnectionTimeout": if (int.TryParse(value, out var connTimeout)) _databaseOptions.ConnectionTimeout = connTimeout; break;
                case "CommandTimeout": if (int.TryParse(value, out var cmdTimeout)) _databaseOptions.CommandTimeout = cmdTimeout; break;
                case "MaxPoolSize": if (int.TryParse(value, out var maxPool)) _databaseOptions.MaxPoolSize = maxPool; break;
                case "MinPoolSize": if (int.TryParse(value, out var minPool)) _databaseOptions.MinPoolSize = minPool; break;
                case "IsProduction": if (bool.TryParse(value, out var isProd)) _databaseOptions.IsProduction = isProd; break;
                case "SslMode": _databaseOptions.SslMode = value; break;
                case "TrustServerCertificate": if (bool.TryParse(value, out var trustCert)) _databaseOptions.TrustServerCertificate = trustCert; break;
                case "IncludeErrorDetail": if (bool.TryParse(value, out var includeDetail)) _databaseOptions.IncludeErrorDetail = includeDetail; break;
                case "ApplicationName": _databaseOptions.ApplicationName = value; break;
                case "PgDumpPath": _databaseOptions.PgDumpPath = value; break;
            }
        }

        // DPAPI 凭据保护：密文（enc: 前缀）解密使用；明文自动加密回写文件
        _databaseOptions.Password = ResolvePassword(filePath, _databaseOptions.Password);

        _databaseOptions.Validate();
        Serilog.Log.Information("[ConfigService] 数据库配置加载完成: Host={Host}", _databaseOptions.Host);

        return _databaseOptions;
    }

    /// <summary>
    /// 解析数据库密码：
    /// - "enc:" 前缀 → DPAPI(LocalMachine) 解密后返回明文（仅驻留内存，用于连接串）；
    /// - 明文 → 首次运行自动加密回写 ini（历史注释声称的"自动加密"由此真正实现）。
    /// LocalMachine 范围：同一台机器的所有 Windows 账户均可解密（窗口机多人共用场景），
    /// 但密文不可复制到其他机器使用。
    /// </summary>
    private static string ResolvePassword(string filePath, string rawValue)
    {
        if (string.IsNullOrEmpty(rawValue))
            return rawValue;

        if (rawValue.StartsWith(EncryptedPasswordPrefix, StringComparison.Ordinal))
        {
            try
            {
                var cipher = Convert.FromBase64String(rawValue[EncryptedPasswordPrefix.Length..]);
                var plain = global::System.Security.Cryptography.ProtectedData.Unprotect(
                    cipher, PasswordEntropy, global::System.Security.Cryptography.DataProtectionScope.LocalMachine);
                return global::System.Text.Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                throw new ConfigurationException("database.ini",
                    "数据库密码解密失败。密文是机器级加密的，不能从其他机器复制过来；" +
                    "请在本机重新填写明文密码，应用启动时会自动加密。原始错误: " + ex.Message);
            }
        }

        TryEncryptPasswordInFile(filePath, rawValue);
        return rawValue;
    }

    private static void TryEncryptPasswordInFile(string filePath, string plainPassword)
    {
        try
        {
            var cipher = global::System.Security.Cryptography.ProtectedData.Protect(
                global::System.Text.Encoding.UTF8.GetBytes(plainPassword), PasswordEntropy,
                global::System.Security.Cryptography.DataProtectionScope.LocalMachine);
            var encValue = EncryptedPasswordPrefix + Convert.ToBase64String(cipher);

            var lines = File.ReadAllLines(filePath);
            var replaced = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if (replaced) break;
                var parts = lines[i].Split('=', 2);
                if (parts.Length == 2 && parts[0].Trim().Equals("Password", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "Password=" + encValue;
                    replaced = true;
                }
            }

            if (replaced)
            {
                File.WriteAllLines(filePath, lines);
                Serilog.Log.Information("[ConfigService] 数据库密码已自动加密回写 (DPAPI LocalMachine)");
            }
        }
        catch (Exception ex)
        {
            // 回写失败不阻断启动（如配置目录只读）；下次启动会重试
            Serilog.Log.Warning("[ConfigService] 数据库密码自动加密回写失败: {Message}", ex.Message);
        }
    }

    public StorageOptions GetStorageOptions()
    {
        if (_storageOptions != null)
            return _storageOptions;

        var appOptions = GetAppOptions();
        var filePath = Path.Combine(_configDirectory, "app.ini");
        
        if (!File.Exists(filePath))
            throw new ConfigurationException("app.ini", $"配置文件不存在: {filePath}");

        _storageOptions = new StorageOptions
        {
            ApplicationName = appOptions.ApplicationName
        };
        
        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "BackupDirectoryName": _storageOptions.BackupDirectoryName = value; break;
                case "LogDirectoryName": _storageOptions.LogDirectoryName = value; break;
                case "TempDirectoryName": _storageOptions.TempDirectoryName = value; break;
                case "UseAppDataDirectory": if (bool.TryParse(value, out var useAppData)) _storageOptions.UseAppDataDirectory = useAppData; break;
            }
        }

        _storageOptions.Validate();
        Serilog.Log.Information("[ConfigService] 存储配置加载完成: BackupPath={BackupPath}", _storageOptions.GetBackupPath());
        
        return _storageOptions;
    }

    public UIOptions GetUIOptions()
    {
        if (_uiOptions != null)
            return _uiOptions;

        var filePath = Path.Combine(_configDirectory, "app.ini");
        
        if (!File.Exists(filePath))
            throw new ConfigurationException("app.ini", $"配置文件不存在: {filePath}");

        _uiOptions = new UIOptions();
        
        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "BackgroundImagesDirectory": _uiOptions.BackgroundImagesDirectory = value; break;
                case "BackgroundImageFormat": _uiOptions.BackgroundImageFormat = value; break;
                case "DefaultBackgroundImage": _uiOptions.DefaultBackgroundImage = value; break;
                case "UseRandomBackground": if (bool.TryParse(value, out var useRandom)) _uiOptions.UseRandomBackground = useRandom; break;
                case "RememberBackgroundChoice": if (bool.TryParse(value, out var remember)) _uiOptions.RememberBackgroundChoice = remember; break;
            }
        }

        _uiOptions.Validate();
        Serilog.Log.Information("[ConfigService] UI配置加载完成");
        
        return _uiOptions;
    }

    public PerformanceOptions GetPerformanceOptions()
    {
        if (_performanceOptions != null)
            return _performanceOptions;

        var filePath = Path.Combine(_configDirectory, "performance.ini");
        
        if (!File.Exists(filePath))
            throw new ConfigurationException("performance.ini", $"配置文件不存在: {filePath}");

        _performanceOptions = new PerformanceOptions();
        
        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "MaxRetries": if (int.TryParse(value, out var maxRetries)) _performanceOptions.MaxRetries = maxRetries; break;
                case "RetryDelayMilliseconds": if (int.TryParse(value, out var delay)) _performanceOptions.RetryDelayMilliseconds = delay; break;
                case "ExponentialBackoff": if (bool.TryParse(value, out var expBackoff)) _performanceOptions.ExponentialBackoff = expBackoff; break;
                case "SlowOperationThresholdMs": if (int.TryParse(value, out var slowMs)) _performanceOptions.SlowOperationThresholdMs = slowMs; break;
                case "PrintTaskTimeoutMinutes": if (int.TryParse(value, out var printTimeout)) _performanceOptions.PrintTaskTimeoutMinutes = printTimeout; break;
                case "PrintRetryBaseDelayMs": if (int.TryParse(value, out var retryBase)) _performanceOptions.PrintRetryBaseDelayMs = retryBase; break;
                case "PrintRetryIncrementMs": if (int.TryParse(value, out var retryInc)) _performanceOptions.PrintRetryIncrementMs = retryInc; break;
                case "PermissionCacheMinutes": if (int.TryParse(value, out var permCache)) _performanceOptions.PermissionCacheMinutes = permCache; break;
                case "PermissionVersionCheckIntervalSeconds": if (int.TryParse(value, out var permCheck)) _performanceOptions.PermissionVersionCheckIntervalSeconds = permCheck; break;
            }
        }

        _performanceOptions.Validate();
        Serilog.Log.Information("[ConfigService] 性能配置加载完成");
        
        return _performanceOptions;
    }

    public PreferencesOptions GetPreferencesOptions()
    {
        if (_preferencesOptions != null)
            return _preferencesOptions;

        var filePath = Path.Combine(_configDirectory, "app.ini");
        
        if (!File.Exists(filePath))
            throw new ConfigurationException("app.ini", $"配置文件不存在: {filePath}");

        _preferencesOptions = new PreferencesOptions();
        
        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "RememberMeKey": _preferencesOptions.RememberMeKey = value; break;
                case "RememberedUsernameKey": _preferencesOptions.RememberedUsernameKey = value; break;
                case "RememberedPasswordKey": _preferencesOptions.RememberedPasswordKey = value; break;
                case "RememberPasswordKey": _preferencesOptions.RememberPasswordKey = value; break;
                case "LastBackgroundKey": _preferencesOptions.LastBackgroundKey = value; break;
            }
        }

        _preferencesOptions.Validate();
        Serilog.Log.Information("[ConfigService] Preferences配置加载完成");
        
        return _preferencesOptions;
    }

    public SchemaOptions GetSchemaOptions()
    {
        if (_schemaOptions != null)
            return _schemaOptions;

        var filePath = Path.Combine(_configDirectory, "database.ini");
        
        if (!File.Exists(filePath))
            throw new ConfigurationException("database.ini", $"数据库配置文件不存在: {filePath}");

        _schemaOptions = new SchemaOptions();
        
        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "CurrentVersion": _schemaOptions.CurrentVersion = value; break;
                case "InitializedAt": _schemaOptions.InitializedAt = value; break;
                case "InitializedBy": _schemaOptions.InitializedBy = value; break;
                case "TablePrefixFilter": _schemaOptions.TablePrefixFilter = value; break;
            }
        }

        _schemaOptions.Validate();
        Serilog.Log.Information("[ConfigService] Schema配置加载完成: Version={Version}", _schemaOptions.CurrentVersion);
        
        return _schemaOptions;
    }

    public void SaveSchemaOptions(SchemaOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var filePath = Path.Combine(_configDirectory, "database.ini");
        
        if (!File.Exists(filePath))
            throw new ConfigurationException("database.ini", $"数据库配置文件不存在: {filePath}");

        var lines = File.ReadAllLines(filePath).ToList();
        var inSchemaSection = false;
        var schemaKeys = new HashSet<string> { "CurrentVersion", "InitializedAt", "InitializedBy", "TablePrefixFilter" };
        var updatedKeys = new HashSet<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            
            if (line == "[Schema]")
            {
                inSchemaSection = true;
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (inSchemaSection && updatedKeys.Count < 4)
                {
                    foreach (var key in schemaKeys)
                    {
                        if (!updatedKeys.Contains(key))
                        {
                            var value = key switch
                            {
                                "CurrentVersion" => options.CurrentVersion,
                                "InitializedAt" => options.InitializedAt ?? string.Empty,
                                "InitializedBy" => options.InitializedBy ?? string.Empty,
                                "TablePrefixFilter" => options.TablePrefixFilter ?? string.Empty,
                                _ => string.Empty
                            };
                            lines.Insert(i, $"{key}={value}");
                            i++;
                        }
                    }
                }
                inSchemaSection = false;
                continue;
            }

            if (inSchemaSection)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    if (schemaKeys.Contains(key))
                    {
                        var value = key switch
                        {
                            "CurrentVersion" => options.CurrentVersion,
                            "InitializedAt" => options.InitializedAt ?? string.Empty,
                            "InitializedBy" => options.InitializedBy ?? string.Empty,
                            "TablePrefixFilter" => options.TablePrefixFilter ?? string.Empty,
                            _ => parts[1]
                        };
                        lines[i] = $"{key}={value}";
                        updatedKeys.Add(key);
                    }
                }
            }
        }

        File.WriteAllLines(filePath, lines);
        _schemaOptions = options;
        Serilog.Log.Information("[ConfigService] Schema配置已保存: Version={Version}", options.CurrentVersion);
    }

    public NetworkOptions GetNetworkOptions()
    {
        if (_networkOptions != null)
            return _networkOptions;

        _networkOptions = new NetworkOptions();
        var filePath = Path.Combine(_configDirectory, "network.ini");

        // 可选文件：缺失时使用默认值（不影响启动）
        if (!File.Exists(filePath))
        {
            Serilog.Log.Information("[ConfigService] network.ini 不存在，使用默认网络配置");
            return _networkOptions;
        }

        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();
            switch (key)
            {
                case "Mode": _networkOptions.Mode = value; break;
                case "PrivateNetworkId": _networkOptions.PrivateNetworkId = value; break;
                case "MoonId": _networkOptions.MoonId = value; break;
                case "PublicNetworkId": _networkOptions.PublicNetworkId = value; break;
                case "DbHost": _networkOptions.DbHost = value; break;
                case "DbPort": if (int.TryParse(value, out var port)) _networkOptions.DbPort = port; break;
                case "WebControllerUrl": _networkOptions.WebControllerUrl = value; break;
            }
        }

        Serilog.Log.Information("[ConfigService] 网络配置加载完成: Mode={Mode}", _networkOptions.Mode);
        return _networkOptions;
    }

    public UpdateOptions GetUpdateOptions()
    {
        if (_updateOptions != null)
            return _updateOptions;

        _updateOptions = new UpdateOptions();
        var filePath = Path.Combine(_configDirectory, "update.ini");

        // 可选文件：缺失时 Enabled=false（在线更新关闭，不影响启动）
        if (!File.Exists(filePath))
        {
            Serilog.Log.Information("[ConfigService] update.ini 不存在，在线更新功能关闭");
            return _updateOptions;
        }

        foreach (var line in ReadIniLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();
            switch (key)
            {
                case "Enabled":
                    if (bool.TryParse(value, out var enabled)) _updateOptions.Enabled = enabled;
                    break;
                case "ManifestUrls":
                    _updateOptions.ManifestUrls = value
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    break;
                case "CheckOnStartup":
                    if (bool.TryParse(value, out var checkOnStartup)) _updateOptions.CheckOnStartup = checkOnStartup;
                    break;
                case "CheckIntervalMinutes":
                    if (int.TryParse(value, out var interval)) _updateOptions.CheckIntervalMinutes = interval;
                    break;
                case "HttpTimeoutSeconds":
                    if (int.TryParse(value, out var httpTimeout)) _updateOptions.HttpTimeoutSeconds = httpTimeout;
                    break;
                case "DownloadTimeoutSeconds":
                    if (int.TryParse(value, out var downloadTimeout)) _updateOptions.DownloadTimeoutSeconds = downloadTimeout;
                    break;
            }
        }

        _updateOptions.Normalize();
        Serilog.Log.Information("[ConfigService] 在线更新配置加载完成: Enabled={Enabled}, Urls={UrlCount}",
            _updateOptions.Enabled, _updateOptions.ManifestUrls.Count);
        return _updateOptions;
    }

    public void SaveNetworkOptions(NetworkOptions options)
    {
        EnsureConfigDirectoryExists();
        var filePath = Path.Combine(_configDirectory, "network.ini");

        var sb = new global::System.Text.StringBuilder();
        sb.AppendLine("# 网络接入配置（ZeroTier）");
        sb.AppendLine("# Mode: Public=公共服务器(ZeroTier Central) / Private=私有化服务器(自建控制器)");
        sb.AppendLine("# 由「系统配置向导 - 网络接入」自动维护；本文件不含密钥。");
        sb.AppendLine();
        sb.AppendLine("[Network]");
        sb.AppendLine($"Mode={options.Mode}");
        sb.AppendLine($"PrivateNetworkId={options.PrivateNetworkId}");
        sb.AppendLine($"MoonId={options.MoonId}");
        sb.AppendLine($"PublicNetworkId={options.PublicNetworkId}");
        sb.AppendLine($"DbHost={options.DbHost}");
        sb.AppendLine($"DbPort={options.DbPort}");
        sb.AppendLine($"WebControllerUrl={options.WebControllerUrl}");

        File.WriteAllText(filePath, sb.ToString(),
            new global::System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        lock (_iniCacheLock) { _iniLinesCache.Remove(filePath); }
        _networkOptions = options;
        Serilog.Log.Information("[ConfigService] 网络配置已保存: Mode={Mode}", options.Mode);
    }

    public void UpdateDatabaseEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ConfigurationException("DatabaseOptions.Host", "数据库主机地址不能为空");
        if (port <= 0 || port > 65535)
            throw new ConfigurationException("DatabaseOptions.Port", "数据库端口无效");

        var filePath = Path.Combine(_configDirectory, "database.ini");
        if (!File.Exists(filePath))
            throw new ConfigurationException("database.ini", $"数据库配置文件不存在: {filePath}");

        var lines = File.ReadAllLines(filePath);
        var hostUpdated = false;
        var portUpdated = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            if (!hostUpdated && key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "Host=" + host;
                hostUpdated = true;
            }
            else if (!portUpdated && key.Equals("Port", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "Port=" + port;
                portUpdated = true;
            }
        }

        // 键缺失（异常配置）时追加，保证解析器可见；解析按 key 全文件匹配，不受 section 影响
        if (!hostUpdated || !portUpdated)
        {
            var list = lines.ToList();
            if (!hostUpdated) list.Add("Host=" + host);
            if (!portUpdated) list.Add("Port=" + port);
            lines = list.ToArray();
        }

        File.WriteAllLines(filePath, lines, new global::System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        lock (_iniCacheLock) { _iniLinesCache.Remove(filePath); }

        if (_databaseOptions != null)
        {
            _databaseOptions.Host = host;
            _databaseOptions.Port = port;
        }

        Serilog.Log.Information("[ConfigService] 数据库地址已更新: Host={Host}, Port={Port}", host, port);
    }

    public void EnsureConfigDirectoryExists()
    {
        if (!Directory.Exists(_configDirectory))
        {
            Directory.CreateDirectory(_configDirectory);
            Serilog.Log.Information("[ConfigService] 配置目录已创建: {ConfigDirectory}", _configDirectory);
        }
    }

    public bool CheckConfigFilesExist()
    {
        var requiredFiles = new[] { "app.ini", "database.ini", "performance.ini" };

        foreach (var fileName in requiredFiles)
        {
            var filePath = Path.Combine(_configDirectory, fileName);
            if (!File.Exists(filePath))
            {
                Serilog.Log.Warning("[ConfigService] 配置文件缺失: {FileName}, 目录: {Directory}", fileName, _configDirectory);
                return false;
            }
        }
        
        return true;
    }

    public List<string> GetMissingConfigFiles()
    {
        var requiredFiles = new[] { "app.ini", "database.ini", "performance.ini" };
        var missingFiles = new List<string>();
        
        foreach (var fileName in requiredFiles)
        {
            var filePath = Path.Combine(_configDirectory, fileName);
            if (!File.Exists(filePath))
            {
                missingFiles.Add(fileName);
            }
        }
        
        return missingFiles;
    }

    public List<string> GenerateDefaultConfigFiles()
    {
        EnsureConfigDirectoryExists();

        var generated = new List<string>();

        // 网络配置先落盘：database.ini 的默认主机取 network.ini 的 DbHost
        // （默认 127.0.0.1；私有化/远程部署可在网络接入页配置后同步）
        var networkDbHost = GetNetworkOptions().DbHost?.Trim() ?? string.Empty;
        var databaseIniContent = string.IsNullOrWhiteSpace(networkDbHost)
            ? DefaultDatabaseIni
            : DefaultDatabaseIni.Replace("Host=127.0.0.1", "Host=" + networkDbHost, StringComparison.Ordinal);

        var templates = new (string FileName, string Content)[]
        {
            ("app.ini", DefaultAppIni),
            ("performance.ini", DefaultPerformanceIni),
            ("network.ini", DefaultNetworkIni),
            ("database.ini", databaseIniContent),
        };

        foreach (var (fileName, content) in templates)
        {
            var filePath = Path.Combine(_configDirectory, fileName);
            if (File.Exists(filePath))
                continue;

            // UTF-8 无 BOM，与现有配置文件编码一致
            File.WriteAllText(filePath, content, new global::System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            generated.Add(fileName);
            Serilog.Log.Information("[ConfigService] 已生成默认配置文件: {FileName}", fileName);
        }

        return generated;
    }

    /// <summary>app.ini 默认模板（与随包分发的 config\app.ini 保持一致，发版时同步版本号）</summary>
    private const string DefaultAppIni = """
        # 应用配置文件
        # 此文件包含应用元数据和UI配置

        [General]
        Version=1.1.20260912
        ApplicationName=帝皇权杖δ-me13
        WindowTitle=帝皇权杖δ-me13 社会救助管理系统

        [Storage]
        BackupDirectoryName=Backups
        LogDirectoryName=Logs
        TempDirectoryName=Temp
        TemplateCachePath=Cache/Templates
        UseAppDataDirectory=true

        [UI]
        BackgroundImagesDirectory=backgrounds
        BackgroundImageFormat=.webp
        DefaultBackgroundImage=background1.webp
        UseRandomBackground=true
        RememberBackgroundChoice=true

        [Preferences]
        RememberMeKey=RememberMe
        RememberedUsernameKey=RememberedUsername
        RememberedPasswordKey=RememberedPassword
        RememberPasswordKey=RememberPassword
        LastBackgroundKey=LastBackground

        [Export]
        ExportBaseDirectoryName=帝皇权杖δ-me13
        ExportSchemaReportDirectory=Schema检查报告
        ExportArchiveDirectory=档案导出
        ExportMonthlyReportDirectory=月报表
        ExportDataBackupDirectory=数据备份
        ExportSystemLogDirectory=系统日志
        UseDocumentsDirectory=true
        """;

    /// <summary>performance.ini 默认模板（与随包分发的 config\performance.ini 保持一致）</summary>
    private const string DefaultPerformanceIni = """
        # 性能配置文件
        # 此文件包含性能相关参数配置

        [Retry]
        MaxRetries=3
        RetryDelayMilliseconds=1000
        ExponentialBackoff=true

        [Timeout]
        ConnectionTimeoutSeconds=5
        CommandTimeoutSeconds=30

        [Thresholds]
        SlowOperationThresholdMs=500
        PrintTaskTimeoutMinutes=5
        PrintRetryBaseDelayMs=2000
        PrintRetryIncrementMs=1000
        PermissionCacheMinutes=3
        PermissionVersionCheckIntervalSeconds=30

        [ConnectionPool]
        MinPoolSize=10
        MaxPoolSize=30
        """;

    /// <summary>
    /// database.ini 默认模板：默认数据库连接 + 默认密码明文（部署机内网默认库）。
    /// 密码在应用首次读取时由 ResolvePassword 自动加密为 enc: 密文；
    /// 该文件本身绝不随构建/安装包分发（csproj Exclude + iss Excludes）。
    /// </summary>
    private const string DefaultDatabaseIni = """
        # 综合数据库配置文件（默认配置，由配置向导生成）
        # 此文件包含数据库连接和操作控制的配置设置
        # 格式：标准INI格式，使用节(section)和键值对(key=value)

        [General]
        # 配置文件格式版本
        Version=1.0
        # 默认使用的数据库模块
        DefaultModule=primary

        # 模块 1: 主数据库配置
        [PrimaryConnection]
        # 数据库类型: PostgreSQL
        Type=PostgreSQL
        # 数据库服务器主机名或IP地址
        Host=127.0.0.1
        # 数据库服务器端口 (PostgreSQL: 5432)
        Port=5432
        # 数据库名称
        DatabaseName=new_cosmos
        # 连接超时时间（秒）
        ConnectionTimeout=5

        [PrimaryAuthentication]
        # 数据库用户名
        Username=new_cosmos
        # 数据库密码：请填写明文，应用首次读取时自动加密为 enc: 前缀的密文（DPAPI 机器级加密）。
        # 密文不可复制到其他机器使用；如需修改密码，请重新填写明文，启动后自动加密。
        Password=your_password

        [PrimaryPooling]
        # 连接池最大连接数
        MaxPoolSize=30
        # 连接池最小连接数
        MinPoolSize=10

        [PrimaryPostgreSQL]
        # PostgreSQL SSL模式
        SslMode=Disable
        # 是否信任服务器证书
        TrustServerCertificate=true

        [PrimaryEnvironment]
        # 是否为生产环境
        IsProduction=true
        # 数据库连接的应用程序名称
        ApplicationName=NewCosmos

        # 控制分类部分
        # 配置标志、验证规则和操作控制

        [Validation]
        # 是否验证数据库连接
        EnableConnectionValidation=true
        # 连接验证超时时间（秒）
        ConnectionValidationTimeout=5
        # 是否验证数据库架构
        EnableSchemaValidation=true

        [Operational]
        # 是否使用连接池
        EnableConnectionPooling=true
        # 是否支持事务
        EnableTransactionSupport=true

        [Performance]
        # 是否启用连接弹性
        EnableConnectionResiliency=true
        # 命令超时时间（秒）
        CommandTimeout=30
        # 批量操作大小
        BatchSize=1000
        # 连接保活间隔（秒）
        KeepAliveInterval=60
        # 是否启用连接保活
        EnableKeepAlive=true

        # 备份配置部分
        [Backup]
        # 备份工具路径（PostgreSQL bin目录）
        PgDumpPath=pg_dump
        # 备份文件名前缀
        BackupFilePrefix=cosmos_backup
        # 备份文件扩展名
        BackupFileExtension=.sql
        # 备份保留天数
        RetentionDays=30
        # 是否压缩备份
        EnableCompression=false

        # Schema版本控制部分
        [Schema]
        # 当前Schema版本号
        CurrentVersion=1.0
        # 初始化时间（首次初始化后自动填写）
        InitializedAt=
        # 初始化操作者（首次初始化后自动填写）
        InitializedBy=
        # 表名前缀过滤（只检测此前缀开头的表，为空则检测全部）
        TablePrefixFilter=nc_
        """;

    /// <summary>network.ini 默认模板（网络接入配置，可选文件）</summary>
    private const string DefaultNetworkIni = """
        # 网络接入配置（ZeroTier）
        # Mode: Public=公共服务器(ZeroTier Central) / Private=私有化服务器(自建控制器)

        [Network]
        Mode=Private
        PrivateNetworkId=
        MoonId=
        PublicNetworkId=
        DbHost=127.0.0.1
        DbPort=5432
        WebControllerUrl=
        """;

}