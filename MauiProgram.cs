using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Helpers;
using NewCosmos.Models.Options;
using NewCosmos.Services.Templates;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ElderlyBenefits;

using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.Import;
using NewCosmos.Services.Import.RuralSubsistence;
using NewCosmos.Services.Import.UrbanSubsistence;
using NewCosmos.Services.Import.LowIncomeEdge;
using NewCosmos.Services.Import.RigidExpenditure;
using NewCosmos.Services.Import.Destitute;
using NewCosmos.Services.Platform;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Domain.SpecialApproval;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.Services.Lottery;
using NewCosmos.ViewModels.SocialAssistance;
using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.ViewModels.ChangeManagement;
using NewCosmos.ViewModels.Main;
using NewCosmos.ViewModels.AssetVerification;
using NewCosmos.ViewModels.Reporting;
using NewCosmos.ViewModels.ElderlyBenefits;
using NewCosmos.ViewModels.TempRelief;
using NewCosmos.ViewModels.UserManagement;
using NewCosmos.ViewModels.Recovery;
using NewCosmos.ViewModels.DatabaseManagement;
using NewCosmos.ViewModels.Auth;
using NewCosmos.ViewModels.Config;
using NewCosmos.ViewModels.Lottery;
using NewCosmos.ViewModels.DutyManagement;
using NewCosmos.Pages.Main;
using NewCosmos.Pages.Auth;
using NewCosmos.Pages.DatabaseManagement;
using NewCosmos.Pages.Config;
using NewCosmos.Pages.UserManagement;
using NewCosmos.Pages.TempRelief;
using NewCosmos.Pages.Recovery;
using NewCosmos.Pages.Lottery;
using NewCosmos.Pages.DutyManagement;

namespace NewCosmos;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        #region 核心服务注册

        builder.Services.AddSingleton<ILoggerService>(sp => new LoggerService(
            sp.GetRequiredService<StorageOptions>(),
            sp.GetRequiredService<AppOptions>(),
            sp.GetRequiredService<PerformanceOptions>()));
        builder.Services.AddSingleton<IConfigService, ConfigService>();
        builder.Services.AddSingleton<ISessionStore, SessionStore>();
        builder.Services.AddSingleton<IWindowTitleService, WindowTitleService>();
        builder.Services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();
        builder.Services.AddSingleton<INavigationService, Navigation.NavigationService>();
        builder.Services.AddSingleton<ILoadingProgressRunner, Navigation.LoadingProgressRunner>();
        builder.Services.AddSingleton<IInitializationService, InitializationService>();
        builder.Services.AddSingleton<ISystemService, SystemService>();
        builder.Services.AddSingleton<IBackgroundLoaderService, BackgroundLoaderService>();
        builder.Services.AddSingleton<Helpers.IPinyinConverter, Helpers.PinyinConverter>();
#if WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, Platforms.Windows.WindowsFolderPickerService>();
        builder.Services.AddSingleton<IFilePickerService, Platforms.Windows.WindowsFilePickerService>();
        builder.Services.AddSingleton<IPrinterService, Platforms.Windows.WindowsPrinterService>();
        builder.Services.AddSingleton<IIdentityReader, Platforms.Windows.WindowsIdentityReader>();
        builder.Services.AddSingleton<IAppPackageInstaller, Platforms.Windows.WindowsPackageInstaller>();
#elif ANDROID
        builder.Services.AddSingleton<IFolderPickerService, NewCosmos.Services.Platform.AndroidFolderPickerService>();
        builder.Services.AddSingleton<IFilePickerService, NewCosmos.Services.Platform.AndroidFilePickerService>();
        builder.Services.AddSingleton<IPrinterService, NewCosmos.Services.Platform.AndroidPrinterService>();
        builder.Services.AddSingleton<IIdentityReader, NewCosmos.Services.Platform.AndroidIdentityReader>();
        builder.Services.AddSingleton<IAppPackageInstaller, NewCosmos.Services.Platform.AndroidPackageInstaller>();
        // 原生 Camera2 预览控件（身份证扫描取景框）
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<NewCosmos.Controls.CameraPreviewView, NewCosmos.Platforms.Android.Camera.CameraPreviewViewHandler>());
#endif

        builder.Services.AddSingleton<IDictCacheService, DictCacheService>();
        builder.Services.AddSingleton<IFileService, FileService>();

        // 在线更新与客户端版本台账
        builder.Services.AddSingleton<IUpdateService, UpdateService>();
        builder.Services.AddSingleton<IAppUpdateCoordinator, AppUpdateCoordinator>();
        builder.Services.AddSingleton<NewCosmos.Services.System.IClientVersionService, NewCosmos.Services.System.ClientVersionService>();

        #endregion

        #region 数据库配置注册

        builder.Services.AddSingleton<DatabaseOptions>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            return configService.GetDatabaseOptions();
        });

        builder.Services.AddSingleton<AppOptions>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            return configService.GetAppOptions();
        });

        builder.Services.AddSingleton<StorageOptions>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            return configService.GetStorageOptions();
        });

        builder.Services.AddSingleton<UIOptions>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            return configService.GetUIOptions();
        });

        builder.Services.AddSingleton<PerformanceOptions>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            return configService.GetPerformanceOptions();
        });

        builder.Services.AddSingleton<PreferencesOptions>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            return configService.GetPreferencesOptions();
        });

        builder.Services.AddSingleton<IDatabaseService, PostgreSqlDatabaseService>();

        #endregion

        #region 领域服务注册 - 社会救助

        builder.Services.AddSingleton<IApplicationService, ApplicationService>();
        // 状态流转单一权威入口（§9 规范2：状态机单一权威 + §8 审计留痕）
        builder.Services.AddSingleton<IApplicationStatusService, ApplicationStatusService>();
        builder.Services.AddSingleton<IHouseholdSurveyService, HouseholdSurveyService>();
        builder.Services.AddSingleton<IFamilyMemberService, FamilyMemberService>();
        builder.Services.AddSingleton<IClassificationService, ClassificationService>();
        builder.Services.AddSingleton<IIncomeCalculationService, IncomeCalculationService>();
        builder.Services.AddSingleton<IGuaranteeAmountService, GuaranteeAmountService>();
        builder.Services.AddSingleton<IGracePeriodService, GracePeriodService>();
        builder.Services.AddSingleton<IPersonSearchService, PersonSearchService>();
        builder.Services.AddSingleton<ILandContractService, LandContractService>();
        builder.Services.AddSingleton<ISubsidyDataService, SubsidyDataService>();
        builder.Services.AddSingleton<ISupporterService, SupporterService>();
        builder.Services.AddSingleton<ICaregiverService, CaregiverService>();
        builder.Services.AddSingleton<IEconomicDetailService, EconomicDetailService>();
        builder.Services.AddSingleton<ICapabilityAssessmentService, CapabilityAssessmentService>();
        builder.Services.AddSingleton<ICollegeStudentService, CollegeStudentService>();

        #endregion

        #region 领域服务注册 - 普惠高龄

        builder.Services.AddSingleton<ElderlyCategoryService>();
        builder.Services.AddSingleton<IElderlyApplicationService, ElderlyApplicationService>();

        #endregion

        #region 领域服务注册 - 临时救助

        builder.Services.AddSingleton<ITempReliefService, TempReliefService>();

        #endregion

        #region 领域服务注册 - 后补追缴

        builder.Services.AddSingleton<IRecoveryService, RecoveryService>();

        #endregion

        #region 领域服务注册 - 近亲属备案

        builder.Services.AddSingleton<INearRelativeService, NearRelativeService>();

        #endregion

        #region 领域服务注册 - 资产核查

        builder.Services.AddSingleton<IAssetVerificationService, AssetVerificationService>();
        builder.Services.AddSingleton<IAssetVerificationReportService, AssetVerificationReportService>();
        builder.Services.AddSingleton<IPdfVerificationService, PdfVerificationService>();

        #endregion

        #region 领域服务注册 - 用户管理

        builder.Services.AddSingleton<IUserService, UserService>();
        builder.Services.AddSingleton<IOrganizationService, OrganizationService>();
        
        #endregion
        
        #region 权限服务注册（新权限系统）
        
        builder.Services.AddSingleton<INewPermissionService, NewPermissionService>();
        builder.Services.AddSingleton<IDataScopeService, DataScopeService>();
        builder.Services.AddSingleton<IRoleService, RoleService>();
        
        #endregion

        #region 系统服务注册 - 对话服务

        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<ILoadingProgressService, LoadingProgressService>();

        #endregion

        #region 系统服务注册 - 数据库管理

        builder.Services.AddSingleton<ISchemaSyncService, SchemaSyncService>();
        builder.Services.AddSingleton<ISeedMergeService, SeedMergeService>();
        builder.Services.AddSingleton<ISchemaService, SchemaService>();
        builder.Services.AddSingleton<IDatabaseManagementService, DatabaseManagementService>();
        builder.Services.AddSingleton<IStandardConfigService, StandardConfigService>();
        builder.Services.AddSingleton<IDictionaryService, DictionaryService>();
        builder.Services.AddSingleton<IRegionService, RegionService>();
#if WINDOWS
        builder.Services.AddSingleton<INetworkAccessService, NetworkAccessService>();
#elif ANDROID
        builder.Services.AddSingleton<INetworkAccessService, AndroidNetworkAccessService>();
#endif
        builder.Services.AddSingleton<IHolidayManageService, HolidayManageService>();
        builder.Services.AddSingleton<IDutyService, DutyService>();
        builder.Services.AddSingleton<AddressResolver>();
        builder.Services.AddSingleton<TemplateFieldBuilder>();

        #endregion

        #region 领域服务注册 - 报表

        builder.Services.AddSingleton<IMonthlyReportService, MonthlyReportService>();
        builder.Services.AddSingleton<IPrintService, PrintService>();
        builder.Services.AddSingleton<IStatisticsService, StatisticsService>();

        #endregion

        #region 导入服务注册

        builder.Services.AddSingleton<RuralSubsistenceImportService>();
        builder.Services.AddSingleton<UrbanSubsistenceImportService>();
        builder.Services.AddSingleton<LowIncomeEdgeImportService>();
        builder.Services.AddSingleton<RigidExpenditureImportService>();
        builder.Services.AddSingleton<DestituteImportService>();

        builder.Services.AddSingleton<ICombinedImportService>(sp => sp.GetRequiredService<RuralSubsistenceImportService>());
        builder.Services.AddSingleton<ICombinedImportService>(sp => sp.GetRequiredService<UrbanSubsistenceImportService>());
        builder.Services.AddSingleton<ICombinedImportService>(sp => sp.GetRequiredService<LowIncomeEdgeImportService>());
        builder.Services.AddSingleton<ICombinedImportService>(sp => sp.GetRequiredService<RigidExpenditureImportService>());
        builder.Services.AddSingleton<ICombinedImportService>(sp => sp.GetRequiredService<DestituteImportService>());

        builder.Services.AddSingleton<ElderlySubsidyImportService>();
        builder.Services.AddSingleton<AgriculturalSubsidyImportService>();
        builder.Services.AddSingleton<PlantingSubsidyImportService>();
        builder.Services.AddSingleton<LandContractImportService>();
        builder.Services.AddSingleton<SoybeanSubsidyImportService>();
        builder.Services.AddSingleton<RotationSubsidyImportService>();

        builder.Services.AddSingleton<IImportService>(sp => sp.GetRequiredService<ElderlySubsidyImportService>());
        builder.Services.AddSingleton<IImportService>(sp => sp.GetRequiredService<AgriculturalSubsidyImportService>());
        builder.Services.AddSingleton<IImportService>(sp => sp.GetRequiredService<PlantingSubsidyImportService>());
        builder.Services.AddSingleton<IImportService>(sp => sp.GetRequiredService<LandContractImportService>());
        builder.Services.AddSingleton<IImportService>(sp => sp.GetRequiredService<SoybeanSubsidyImportService>());
        builder.Services.AddSingleton<IImportService>(sp => sp.GetRequiredService<RotationSubsidyImportService>());

        builder.Services.AddSingleton<IImportServiceManager, ImportServiceManager>();

        #endregion

        #region 基础服务（P1：文档处理引擎 + 时间线）

        builder.Services.AddSingleton<Services.Utilities.IHolidayService, Services.Utilities.HolidayService>();
        builder.Services.AddSingleton<Services.Utilities.IBusinessTimelineService, Services.Utilities.BusinessTimelineService>();

        #endregion

        #region 业务服务（P2：模板/文档/变更/一事一议/入户调查/归档）

        builder.Services.AddSingleton<Services.Domain.ArchiveManagement.ITemplateService, Services.Domain.ArchiveManagement.TemplateService>();
        builder.Services.AddSingleton<Services.Domain.ArchiveManagement.IArchiveService, Services.Domain.ArchiveManagement.ArchiveService>();
        builder.Services.AddSingleton<Services.Domain.ArchiveManagement.IProofUnitTemplateService, Services.Domain.ArchiveManagement.ProofUnitTemplateService>();
        builder.Services.AddSingleton<Services.Domain.ArchiveManagement.IPublicityOutputService, Services.Domain.ArchiveManagement.PublicityOutputService>();
        builder.Services.AddSingleton<Services.Domain.ChangeManagement.IChangeService, Services.Domain.ChangeManagement.ChangeService>();
        builder.Services.AddSingleton<Services.Domain.ChangeManagement.IImportedArchiveService, Services.Domain.ChangeManagement.ImportedArchiveService>();
        builder.Services.AddSingleton<Services.Domain.ChangeManagement.IDynamicManagementRecordService, Services.Domain.ChangeManagement.DynamicManagementRecordService>();
        builder.Services.AddSingleton<Services.Domain.SpecialApproval.ISpecialApprovalService, Services.Domain.SpecialApproval.SpecialApprovalService>();
        builder.Services.AddSingleton<Services.Domain.SpecialApproval.ISpecialApprovalFormService, Services.Domain.SpecialApproval.SpecialApprovalFormService>();

        #endregion

        #region 模板引擎注册

        builder.Services.AddSingleton<ITemplateEngineFactory, TemplateEngineFactory>();

        #endregion

        #region 打印服务注册

        builder.Services.AddSingleton<IPrintRecordService, PrintRecordService>();
        builder.Services.AddSingleton<IPrintExecuteService, PrintExecuteService>();
        // 推送打印队列（手机端入队，PC 端打印代理执行）
        builder.Services.AddSingleton<IPrintQueueService, PrintQueueService>();
        builder.Services.AddSingleton<IPrintJobFactory, PrintJobFactory>();
        builder.Services.AddSingleton<IPrintAgentService, PrintAgentService>();

        #endregion

        #region 彩票服务注册

        builder.Services.AddSingleton<ILotteryDataService, LotteryDataService>();
        builder.Services.AddSingleton<ILotteryPredictService, LotteryPredictService>();
        builder.Services.AddSingleton<IUserPurchaseService, UserPurchaseService>();

        #endregion

        #region ViewModel 注册

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<MainViewModel>();

        builder.Services.AddTransient<ApplicationFormViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.SocialAssistance.ApplicationWorkflowViewModel>();

        builder.Services.AddTransient<SpouseInfoViewModel>();
        builder.Services.AddTransient<FamilyMembersViewModel>();
        builder.Services.AddTransient<ClassificationViewModel>();
        builder.Services.AddTransient<CapabilityAssessmentViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.SocialAssistance.SpecialApprovalFormViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ArchiveManagement.ArchiveOutputViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ArchiveManagement.ArchiveProductionViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ArchiveManagement.TemplateManagementViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ArchiveManagement.ArchiveQueryViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ArchiveManagement.ProofUnitTemplateManageViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ArchiveManagement.MonthlyPublicityViewModel>();

        builder.Services.AddTransient<NewCosmos.ViewModels.ChangeManagement.ChangeManagementViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ChangeManagement.ChangeViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ChangeManagement.MemberChangeReasonPopupViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ChangeManagement.GracePeriodConfirmViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ChangeManagement.HouseholdDeathChangeViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.ChangeManagement.HeadChangeViewModel>();

        builder.Services.AddTransient<QuickAssetVerificationViewModel>();
        builder.Services.AddTransient<MonthlyAssetAuditViewModel>();
        builder.Services.AddTransient<MonthlyReportViewModel>();

        builder.Services.AddTransient<DatabaseManagementViewModel>();
        builder.Services.AddTransient<DictionaryManagementViewModel>();
        builder.Services.AddTransient<RegionManagementViewModel>();
        builder.Services.AddTransient<StandardConfigManagementViewModel>();
        builder.Services.AddTransient<DataImportViewModel>();

        builder.Services.AddTransient<MonthlyReportMainViewModel>();

        builder.Services.AddTransient<UserManagementViewModel>();
        builder.Services.AddTransient<OrganizationTreeViewModel>();
        builder.Services.AddTransient<OrganizationImportViewModel>();
        builder.Services.AddTransient<UserEditorViewModel>();
        builder.Services.AddTransient<RoleEditorViewModel>();
        builder.Services.AddTransient<OrganizationEditorViewModel>();

        builder.Services.AddTransient<ConfigWizardViewModel>();
        builder.Services.AddTransient<NetworkAccessViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.Shared.LoadingProgressDialogViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.SocialAssistance.CollegeStudentManagementViewModel>();

        builder.Services.AddTransient<ElderlyApplicationListViewModel>();
        builder.Services.AddTransient<ElderlyApplicationFormViewModel>();
        builder.Services.AddTransient<ElderlyStopListViewModel>();
        builder.Services.AddTransient<ElderlyStopViewModel>();
        builder.Services.AddTransient<GracePeriodExpiringListViewModel>();
        builder.Services.AddTransient<ElderlyReportViewModel>();
        builder.Services.AddTransient<ElderlyPendingListViewModel>();
        builder.Services.AddTransient<ElderlyReviewListViewModel>();
        builder.Services.AddTransient<ElderlyReviewViewModel>();

        builder.Services.AddTransient<TempReliefListViewModel>();
        builder.Services.AddTransient<TempReliefFormViewModel>();
        builder.Services.AddTransient<NewCosmos.ViewModels.SocialAssistance.NearRelativeEntryViewModel>();

        // ── 统一补打中心（五域 Provider + 三栏按人补打/跨户批量） ──
        // 注意：必须注册接口 IReprintDomainProvider——UnifiedReprintViewModel 注入的是 IEnumerable<IReprintDomainProvider>，
        // 仅注册具体类会导致注入空序列（名单恒空且无任何报错）。
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.Providers.SocialAssistanceReprintProvider>();
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.Providers.TempReliefReprintProvider>();
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.Providers.ElderlyReprintProvider>();
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.Providers.AssetVerificationReprintProvider>();
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.Providers.DynamicRecordReprintProvider>();
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.IReprintDomainProvider>(sp => sp.GetRequiredService<NewCosmos.ViewModels.Reprint.Providers.SocialAssistanceReprintProvider>());
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.IReprintDomainProvider>(sp => sp.GetRequiredService<NewCosmos.ViewModels.Reprint.Providers.TempReliefReprintProvider>());
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.IReprintDomainProvider>(sp => sp.GetRequiredService<NewCosmos.ViewModels.Reprint.Providers.ElderlyReprintProvider>());
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.IReprintDomainProvider>(sp => sp.GetRequiredService<NewCosmos.ViewModels.Reprint.Providers.AssetVerificationReprintProvider>());
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.IReprintDomainProvider>(sp => sp.GetRequiredService<NewCosmos.ViewModels.Reprint.Providers.DynamicRecordReprintProvider>());
        builder.Services.AddTransient<NewCosmos.ViewModels.Reprint.UnifiedReprintViewModel>();

        builder.Services.AddTransient<RecoverySearchViewModel>();
        builder.Services.AddTransient<RecoveryFormViewModel>();
        builder.Services.AddTransient<RecoveryManualViewModel>();

        // 彩票模块 ViewModel
        builder.Services.AddTransient<LotteryHomeViewModel>();
        builder.Services.AddTransient<LotteryPredictionViewModel>();
        builder.Services.AddTransient<LotteryHistoryViewModel>();
        builder.Services.AddTransient<LotteryResultViewModel>();

        // 值班管理模块 ViewModel
        builder.Services.AddTransient<DutyScheduleViewModel>();
        builder.Services.AddTransient<DutyMemberViewModel>();
        builder.Services.AddTransient<HolidayViewModel>();
        builder.Services.AddTransient<DutyAdjustViewModel>();

        #endregion

        #region Page 注册

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<MainPage>();

        // 手机端专用页面（Phase 2: UI 分离）
        builder.Services.AddTransient<Pages.Mobile.MobileMainPage>();
        builder.Services.AddTransient<ViewModels.Mobile.MobileMainViewModel>();
        builder.Services.AddTransient<Pages.Mobile.MobileAssetVerificationHomePage>();
        builder.Services.AddTransient<Pages.Mobile.MobileQuickAssetVerificationPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileMonthlyAssetAuditPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileIdCardScanPage>();
        // P1：低收入人口救助帮扶手机端
        builder.Services.AddTransient<Pages.Mobile.MobileApplicationWorkflowPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileApplicationFormPage>();
        // P3：高龄津贴手机端
        builder.Services.AddTransient<Pages.Mobile.MobileElderlyBenefitsHomePage>();
        builder.Services.AddTransient<Pages.Mobile.MobileElderlyApplicationListPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileElderlyApplicationFormPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileElderlyStopListPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileElderlyStopPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileElderlyReviewListPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileElderlyReviewPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileElderlyPendingListPage>();
        // P2：保障对象动态管理手机端
        builder.Services.AddTransient<Pages.Mobile.MobileChangePage>();
        builder.Services.AddTransient<Pages.Mobile.MobileHeadChangePage>();
        builder.Services.AddTransient<Pages.Mobile.MobileHouseholdDeathChangePage>();
        builder.Services.AddTransient<Pages.Mobile.MobileGracePeriodConfirmPage>();
        // P4：临时救助手机端
        builder.Services.AddTransient<ViewModels.TempRelief.MobileTempReliefFormViewModel>();
        builder.Services.AddTransient<Pages.Mobile.MobileTempReliefListPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileTempReliefFormPage>();
        // P5：只读查询/报表手机端
        builder.Services.AddTransient<Pages.Mobile.MobileGracePeriodExpiringListPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileArchiveQueryPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileDutySchedulePage>();
        // P6：推送打印任务查看
        builder.Services.AddTransient<ViewModels.Mobile.MobilePrintJobsViewModel>();
        builder.Services.AddTransient<Pages.Mobile.MobilePrintJobsPage>();
        // P7：后补追缴手机端
        builder.Services.AddTransient<Pages.Mobile.MobileRecoverySearchPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileRecoveryFormPage>();
        builder.Services.AddTransient<Pages.Mobile.MobileRecoveryManualPage>();

        // 业务域模块首页（首页布局重构）
        builder.Services.AddTransient<Pages.SocialAssistance.SocialAssistanceHomePage>();
        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyBenefitsHomePage>();
        builder.Services.AddTransient<Pages.AssetVerification.AssetVerificationHomePage>();
        builder.Services.AddTransient<ViewModels.SocialAssistance.SocialAssistanceHomeViewModel>();
        builder.Services.AddTransient<ViewModels.ElderlyBenefits.ElderlyBenefitsHomeViewModel>();
        builder.Services.AddTransient<ViewModels.AssetVerification.AssetVerificationHomeViewModel>();

        builder.Services.AddTransient<Pages.SocialAssistance.ApplicationFormPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.ApplicationWorkflowPage>();
        builder.Services.AddTransient<Pages.Reprint.UnifiedReprintPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.SpouseInfoPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.FamilyMembersPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.ClassificationPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.CapabilityAssessmentPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.SpecialApprovalFormPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.CollegeStudentManagementPage>();
        builder.Services.AddTransient<Pages.AssetVerification.QuickAssetVerificationPage>();
        builder.Services.AddTransient<Pages.AssetVerification.MonthlyAssetAuditPage>();
        builder.Services.AddTransient<Pages.AssetVerification.MonthlyReportPage>();
        builder.Services.AddTransient<Pages.Reporting.MonthlyReportPage>();
        builder.Services.AddTransient<Pages.UserManagement.UserManagementPage>();
        builder.Services.AddTransient<OrganizationPage>();
        builder.Services.AddTransient<OrganizationImportPage>();
        builder.Services.AddTransient<DatabaseManagementPage>();
        builder.Services.AddTransient<DataImportPage>();
          builder.Services.AddTransient<Pages.DatabaseManagement.DictionaryManagementPage>();
        builder.Services.AddTransient<Pages.DatabaseManagement.StandardConfigManagementPage>();
        builder.Services.AddTransient<Pages.DatabaseManagement.RegionManagementPage>();
        builder.Services.AddTransient<Pages.ArchiveManagement.ArchiveOutputPage>();
        builder.Services.AddTransient<Pages.ArchiveManagement.ArchiveProductionPage>();
        builder.Services.AddTransient<Pages.ArchiveManagement.TemplateManagementPage>();
        builder.Services.AddTransient<Pages.ArchiveManagement.ArchiveQueryPage>();
        builder.Services.AddTransient<Pages.ArchiveManagement.ProofUnitTemplateManagePage>();
        builder.Services.AddTransient<Pages.ArchiveManagement.MonthlyPublicityPage>();

        builder.Services.AddTransient<Pages.ChangeManagement.ChangePage>();
        builder.Services.AddTransient<Pages.ChangeManagement.HouseholdDeathChangePage>();
        builder.Services.AddTransient<Pages.ChangeManagement.HeadChangePage>();
        builder.Services.AddTransient<Pages.ChangeManagement.MemberChangeReasonPopup>();
        builder.Services.AddTransient<Pages.ChangeManagement.GracePeriodConfirmPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.AddMemberPopup>();
        builder.Services.AddTransient<Pages.SocialAssistance.SelectMembersPopup>();
        
        builder.Services.AddTransient<ConfigWizardPage>();
        builder.Services.AddTransient<Pages.Config.NetworkAccessPage>();

        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyApplicationListPage>();
        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyApplicationFormPage>();
        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyStopListPage>();
        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyStopPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.GracePeriodExpiringListPage>();
        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyReportPage>();
        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyPendingListPage>();
        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyReviewListPage>();
        builder.Services.AddTransient<Pages.ElderlyBenefits.ElderlyReviewPage>();

        builder.Services.AddTransient<TempReliefListPage>();
        builder.Services.AddTransient<TempReliefFormPage>();
        builder.Services.AddTransient<Pages.SocialAssistance.NearRelativeEntryPage>();

        builder.Services.AddTransient<RecoverySearchPage>();
        builder.Services.AddTransient<RecoveryFormPage>();
        builder.Services.AddTransient<RecoveryManualPage>();

        // 彩票模块 Page
        builder.Services.AddTransient<LotteryHomePage>();
        builder.Services.AddTransient<LotteryPredictionPage>();
        builder.Services.AddTransient<LotteryHistoryPage>();
        builder.Services.AddTransient<LotteryResultPage>();

        // 值班管理模块 Page
        builder.Services.AddTransient<DutySchedulePage>();
        builder.Services.AddTransient<Pages.DutyManagement.DutyAdjustPage>();
        builder.Services.AddTransient<DutyMemberPage>();
        builder.Services.AddTransient<Pages.DutyManagement.HolidayPage>();

        #endregion

        return builder.Build();
    }
}
