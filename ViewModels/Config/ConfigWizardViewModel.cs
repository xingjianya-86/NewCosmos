using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Options;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.Config;

public partial class ConfigWizardViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    [ObservableProperty]
    private ObservableCollection<string> _missingConfigFiles = new();

    [ObservableProperty]
    private bool _isAndroid;

    [ObservableProperty]
    private bool _isNotAndroid = true;

    [ObservableProperty]
    private string _dbHost = "127.0.0.1";

    [ObservableProperty]
    private string _dbPort = "5432";

    [ObservableProperty]
    private string _dbName = "new_cosmos";

    [ObservableProperty]
    private string _dbUsername = "new_cosmos";

    [ObservableProperty]
    private string _dbPassword = string.Empty;

    public ConfigWizardViewModel(IServiceProvider serviceProvider, IConfigService configService, ILoggerService logger, IDialogService dialogService)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _logger = logger;
        _dialogService = dialogService;

#if ANDROID
        IsAndroid = true;
        IsNotAndroid = false;
#endif

        LoadMissingFiles();
        LoadExistingDbConfig();
    }

    private void LoadMissingFiles()
    {
        var missing = _configService.GetMissingConfigFiles();
        MissingConfigFiles.Clear();
        foreach (var file in missing)
        {
            MissingConfigFiles.Add(file);
        }
    }

    private void LoadExistingDbConfig()
    {
        try
        {
            var options = _configService.GetDatabaseOptions();
            if (!string.IsNullOrEmpty(options.Host)) DbHost = options.Host;
            if (options.Port > 0) DbPort = options.Port.ToString();
            if (!string.IsNullOrEmpty(options.DatabaseName)) DbName = options.DatabaseName;
            if (!string.IsNullOrEmpty(options.Username)) DbUsername = options.Username;
            if (!string.IsNullOrEmpty(options.Password) && !options.Password.StartsWith("enc:"))
                DbPassword = options.Password;
        }
        catch
        {
            // 使用默认值
        }
    }

    [RelayCommand]
    private async Task GenerateDefaultConfig()
    {
        try
        {
            _logger.Info("开始生成默认配置文件");

            var generated = _configService.GenerateDefaultConfigFiles();

            if (generated.Count == 0)
            {
                await _dialogService.DisplayAlertAsync("提示", "配置文件均已存在，未重新生成。如需重置，请先手动删除对应文件", "确定");
                return;
            }

            LoadMissingFiles();
            _logger.Info($"已生成默认配置文件: {string.Join(", ", generated)}");

            var message = MissingConfigFiles.Count == 0
                ? $"已生成默认配置：{string.Join("、", generated)}。\n\n" +
                  "数据库连接已按默认值填写，密码为默认值，应用首次连接后自动加密保存。\n" +
                  "如需修改，请编辑 config\\database.ini，然后点击「重新检查」。"
                : $"已生成：{string.Join("、", generated)}，仍有配置文件缺失，请重新检查";
            await _dialogService.DisplayAlertAsync("成功", message, "确定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成默认配置失败");
            await _dialogService.DisplayAlertAsync("错误", $"生成失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task SaveDatabaseConfig()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(DbHost) || string.IsNullOrWhiteSpace(DbPassword))
            {
                await _dialogService.DisplayAlertAsync("提示", "主机和密码不能为空", "确定");
                return;
            }

            if (!int.TryParse(DbPort, out var port) || port <= 0 || port > 65535)
            {
                await _dialogService.DisplayAlertAsync("提示", "端口号无效", "确定");
                return;
            }

            var options = new DatabaseOptions
            {
                Host = DbHost.Trim(),
                Port = port,
                DatabaseName = DbName.Trim(),
                Username = DbUsername.Trim(),
                Password = DbPassword.Trim(),
                ConnectionTimeout = 5,
                CommandTimeout = 30,
                MaxPoolSize = 30,
                MinPoolSize = 5,
                SslMode = "Disable",
                TrustServerCertificate = true,
                IncludeErrorDetail = true,
                ApplicationName = "NewCosmos-Android"
            };

            _configService.SaveDatabaseOptions(options);

            await _dialogService.DisplayAlertAsync("成功", "数据库配置已保存", "确定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存数据库配置失败");
            await _dialogService.DisplayAlertAsync("错误", $"保存失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task OpenConfigDirectory()
    {
        try
        {
#if ANDROID
            await _dialogService.DisplayAlertAsync("配置目录",
                "Android 配置文件位于应用私有目录，需使用文件管理器访问。\n\n" +
                "请编辑 database.ini 填写数据库密码，然后点击「重新检查」。",
                "确定");
#else
            _configService.EnsureConfigDirectoryExists();
            await Launcher.OpenAsync(new Uri(Path.Combine(AppContext.BaseDirectory, "config")));
#endif
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开配置目录失败");
        }
    }

    [RelayCommand]
    private async Task OpenNetworkAccessAsync()
    {
        try
        {
            var page = App.Services!.GetRequiredService<Pages.Config.NetworkAccessPage>();
            if (Helpers.WindowNavigator.CurrentPage?.Navigation != null)
                await Helpers.WindowNavigator.CurrentPage.Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开网络接入页面失败");
            await _dialogService.DisplayAlertAsync("错误", $"打开网络接入失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task RecheckConfig()
    {
        try
        {
            if (_configService.CheckConfigFilesExist())
            {
                await _dialogService.DisplayAlertAsync("成功", "配置文件检查通过，应用将重新启动", "确定");

                Helpers.WindowNavigator.CurrentPage = App.Services!.GetRequiredService<Pages.Auth.LoginPage>();
            }
            else
            {
                LoadMissingFiles();
                await _dialogService.DisplayAlertAsync("提示", "仍有配置文件缺失", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新检查配置失败");
        }
    }
}
