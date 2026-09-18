using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Services.Core;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.Config;

public partial class ConfigWizardViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<string> _missingConfigFiles = new();

    public ConfigWizardViewModel(IConfigService configService, ILoggerService logger, IDialogService dialogService)
    {
        _configService = configService;
        _logger = logger;
        _dialogService = dialogService;

        LoadMissingFiles();
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
    private async Task OpenConfigDirectory()
    {
        try
        {
            _configService.EnsureConfigDirectoryExists();

            await Launcher.OpenAsync(new Uri(Path.Combine(AppContext.BaseDirectory, "config")));
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