# NewCosmos 开发规范（AGENTS.md）

> 更新日期：2026-10-07。本文件只保留"改这个代码库必须知道的事实与规则"。
> 单一事实来源：本文不抄代码——凡涉及可执行逻辑，以指向的源文件为准；业务资料在 `docs/`。
> 本文**不写死数量与全量文件枚举**（必然过时），易变清单一律给核对命令；失效引用一律直指权威代码。

---

## §0 30 秒上手

**构建 / 运行 / 验证**

```bash
dotnet build NewCosmos.csproj -f net10.0-windows10.0.19041.0     # Windows
# 运行：bin\Debug\net10.0-windows10.0.19041.0\win-x64\NewCosmos.exe（.NET 10 起 RID 目录名是 win-x64）
# Android：-f net10.0-android36.0（发布由 deploy\publish_all 走 -SkipAndroid 控制）
```

- **没有测试项目**。验证 = 编译零错误 + 启动冒烟（登录→列表→表单保存→导入→打印预览）。
- 发版入口：`deploy\publish_all.bat`（先用 `-DryRun` 体检），见 §2。
- 本地可再生占用（`bin\ obj\ publish\`，≈9 GB）可整删重建，维护见 §2。
- 改数据库 / 改金额 / 改日志，动手前分别看 §8、§9、§10。

**动手前三件事**：① 查 §5 禁止表；② 走 §13 工作流程（先出计划→备份→不盲替换）；③ 分层与 DI 见 §6。

| 要找什么 | 章节 | 要找什么 | 章节 |
|---|---|---|---|
| 技术栈 / XAML 管线 / 导航 | §1 | SQL 与事务写法 | §8 |
| 构建、发版、凭据、git 备份 | §2 | 金额月·年口径 | §9 |
| 代码该放哪个目录 | §3 | 日志与脱敏 | §10 |
| 命名规范 | §4 | 配置文件 | §11 |
| 绝对禁止做的事 | §5 | 安全 | §12 |
| 分层 / DI / ViewModel 基类 | §6 | 工作流程纪律 | §13 |
| Result 错误处理 | §7 | 业务规则权威代码在哪 | §14 |

## §1 项目概览

- **命名空间**：`NewCosmos.*`（`RootNamespace=NewCosmos`）
- **应用显示名**：帝皇权杖δ-me13（民政社会救助管理系统）；**双目标**：`net10.0-windows10.0.19041.0`（WinUI3 桌面，`WindowsPackageType=None` 未打包）+ `net10.0-android36.0`（数据录入端，部署见 `docs\20260920_Android数据录入版部署教程.md`）
- **技术栈**：.NET 10 MAUI + CommunityToolkit.Mvvm + PostgreSQL（Npgsql，自研 `IDatabaseService`，无 EF/Dapper）+ Serilog + EPPlus 8/ExcelDataReader（Excel）+ Office/WPS COM（打印导出）
- **架构**：Pages(XAML) → ViewModels(MVVM) → Services(Domain/System/Core) → IDatabaseService → PostgreSQL
- **导航**：`Navigation\NavigationService.cs` 封装 `NavigationPage` + `PushAsync`，导航键在 `Navigation\NavigationKeys.cs`。**仓库已无 AppShell / `Shell.Current`（历史死代码，2026-10 已删除），禁止引入 Shell。**
- **XAML 管线**：
  - `MauiXamlInflator=SourceGen`（编译期源生成）。
  - GlobalXmlns：根命名空间 `http://schemas.microsoft.com/dotnet/maui/global`，注册表在 `GlobalXmlns.cs`；隐式命名空间属预览特性（csproj 已开 `EnablePreviewFeatures` 并抑制 CA2252）。
  - 共享前缀全局可用：`components:` `converters:` `controls:` `constants:` `cat:` `results:` `pages:` `assetsvc:` `chg:` `domain:` `sys:`（`GlobalXmlns.cs` 尾部 `XmlnsPrefix` 列表为准）。
  - 唯一类型直接无前缀引用（如 `x:DataType="LoginViewModel"`）；`ent:`/`uent:` 因重名（Application、Role）须在各文件内保留经典 `clr-namespace` 声明；`App.xaml` 是例外文件，保留经典 MAUI 命名空间。
  - **值转换器不在 App.xaml 注册**，靠 `converters:` 前缀在页面直接引用。

## §2 构建、运行、发版与凭据

**前置条件**：.NET 10 SDK + `maui-windows` 工作负载；`config\database.ini` 必须存在。

**发版（在线更新）**——入口统一走发布向导：

```powershell
deploy\publish_all.bat                       # 交互向导（版本 / 增量补丁 / 非强制升级 / 推 GitHub / 回环校验）
deploy\publish_all.ps1 -DryRun                # 体检：只打印计划与命令，不构建/不上传/不推
deploy\publish_all.ps1 -NonInteractive -Version 1.1.yyyyMMdd [-SkipWindows|-SkipAndroid|-SkipGit|-SkipDownloadVerify]
deploy\publish_release.ps1 -Version 1.1.yyyyMMdd [-MinSupported 1.1.xxx] [-Force] [-NotesFile notes.md]   # 单平台底层
```

- 流程：版本三处同步 → Release 自包含发布 → Inno 安装包（`installer\NewCosmosSetup.iss`，产物 `publish\NewCosmosSetup_*.exe`）→ 清单签名 → 上传服务器 → 回环下载校验。公开库权威文档：`deploy\README.md`（另有本机 `docs\20260918_在线更新系统.md`，因含内网地址被 `.gitignore` 排除，不在仓库内）。
- 配套工具：`deploy\PatchTool`（增量补丁）、`deploy\UpdateSigningTool`（更新签名）、`deploy\download_update.ps1` / `download_update.bat`（回环校验）。
- 本机服务器参数写 `deploy\deploy.local.ps1`（`*.local.ps1` 已忽略，样例 `deploy.sample.ps1`）。
- **失败排查**：窗口保持不退（`pause`），报错同时转录到 `deploy\publish_all.last.log`（`*.log` 已忽略）。
- ⚠️ 维护坑：调用另一个 .ps1 传**命名参数必须用哈希表 splat** `& $pr @{Version=$v; Channel=$c}`；`@数组` 是**位置**参数展开，会把 `-Version` 当作值传进去（曾致发布闪退）。

**数据库与 Schema**

- `config\database.ini`：开发机放项目 `config\` 目录（**不会**复制进构建输出，csproj 已显式排除）；部署机在 exe 旁 `config\`。ini 中明文填写，首次启动自动 DPAPI 加密为 `enc:` 前缀（机器级，不可跨机复制）。
- Schema 权威定义：`Resources\Schema\{域}\*.yaml`；`SchemaService` 幂等建表/补索引/补外键；手写迁移 `docs\migrations\*.sql`。

**git 与备份**

- 已启用 git（2026-09 开源初始化）。忽略规则分四类（完整列表以 `.gitignore` 为准）：
  **凭据**：`config\database.ini|network.ini|update.ini`（`*.example` 例外）、`AGENTS.local.md`、`*.local.ps1`；
  **本地大体积/私有资产**：`publish/`、`python-embed/`、`公文字体/`、`keystore/`、`Resources\PostgreSQL|ZeroTier/`、`backup/`；
  **构建产物与运行残留**：`bin/`、`obj/`、`deploy/**/bin,obj/`、`*.log`、`输出/`、`Logs/`；
  **内网文档**：`docs\20260918_*.md`（两份，见 §2 末）。
  核对任意路径：`git check-ignore -v <路径>`；盘点被忽略项：`git status --ignored=matching`。
- 改文件前按相对路径复制到 `backup\<yyyyMMdd_HHmmss>_<用途>\`（便于回滚与对比）。
- **本地磁盘维护**：`bin\ obj\ publish\ deploy\**\bin,obj\ *.log` 全属可再生（一次 build/发版即回），磁盘紧张时可整删（2026-10-07 实测释放 8.8 GB，删后 build 零错误）；`python-embed\`（≈1.8 GB，彩票运行时依赖）与 `公文字体\`（打印依赖）**勿删**。

**数据库连接信息**：公开仓库不含任何内部主机/账号/密码；本地开发填 `config\database.ini`（按 `database.ini.example`）；含凭据的本机细节写在 `AGENTS.local.md`（已忽略，不入库）。

## §3 目录结构（职责视图）

> 目录内容会增长，**以磁盘为准**；核对命令：`ls Constants\*.cs`、`ls Components\*.xaml`、`ls Pages -Directory`。

```
NewCosmos/
├── App.xaml(.cs)、MauiProgram.cs（DI 注册总入口）、GlobalXmlns.cs（xmlns 注册表）、
│   Directory.Build.props（NoWarn 白名单：警告不作错误处理，新警告按"真问题修 / 误报入白名单"）
├── Components/     共享 XAML 组件（LoadingOverlay 遮罩、SnackBarView 轻提示、ProgressPopupView 进度、
│                   PaginationBarView 分页、PageHeaderView 页头、FeatureCard、StatCapsule、BackButton、
│                   NavigationViewItem、SchemaValidationResultPopupView、MobileStepperBar）
├── Constants/      常量单一来源，改业务逻辑前先查这里，禁止散落魔法值
│                   （如 ErrorCodes、UserFriendlyMessages、PermissionCodes、FieldKeys、
│                     ClassificationConstants、IncomeTypeConstants、UIConstants…）
├── Controls/       自定义控件 PdfPreviewView（WebView2 + PdfJs）
├── Converters/     值转换器（经 `converters:` 前缀在 XAML 引用，不在 App.xaml 注册）
├── Helpers/        校验与拼装助手：IdCardValidator、TableNameValidator（动态表名白名单唯一维护点）、
│                   PinyinConverter、DictDisplayHelper、ImportedDataMapper、PagedQueryHelper、
│                   LotteryPrizeResolver、AddressResolver…
├── Models/         Entities / Options / Results（Result 模式，见 §7）/ Schema / Exceptions…
├── Navigation/     NavigationService（PushAsync 封装）、NavigationKeys、WindowTitleService、LoadingProgressRunner
├── Pages/ 与 ViewModels/   两目录**按域同构分目录**（Auth、SocialAssistance、AssetVerification、
│                   ArchiveManagement、ChangeManagement、ElderlyBenefits、TempRelief、Recovery、
│                   MonthlyReport、DutyManagement、Lottery、Mobile、Reporting、Reprint、
│                   DatabaseManagement、UserManagement、Config、Shared…）；基类见 §6
├── Platforms/      平台实现（Windows 文件夹选择、打印机等）
├── Resources/      Schema/（YAML 表定义权威）、Seed/、Styles/、PdfJs/、Backgrounds/
├── Services/
│   ├── Core/       BaseService、LoggerService、ConfigService、DialogService、DictCacheService、
│   │               DataMasker（日志脱敏）、InitializationService、SchemaSyncService、
│   │               UpdateService / AppUpdateCoordinator、DatabaseBackupService、FileService、SessionStore…
│   ├── Database/   IDatabaseService、PostgreSqlDatabaseService、ITransactionScope、SchemaService
│   ├── Domain/     业务域（与 Pages 同名分目录）：SocialAssistance、AssetVerification、
│   │               ArchiveManagement、ChangeManagement、ElderlyBenefits、TempRelief、Recovery、
│   │               NearRelative、Printing、Reporting、SpecialApproval、UserManagement
│   ├── Import/     BaseImportService / BaseCombinedImportService + 各具体导入服务 + Excel 工厂
│   │               （EpplusSheetReader / ExcelDataReaderSheetReader / SheetReaderFactory / ImportServiceManager）
│   ├── Lottery/    彩票：LotteryDataService、LotteryPredictService（运行时调用 Scripts\Lottery 下 Python）
│   ├── StateMachine/ ApplicationStateMachine（申请状态机唯一权威，见 §14）
│   ├── System/     RegionService、DictionaryService、StandardConfigService、DatabaseManagementService
│   ├── Templates/  ExcelEngine、WordEngine、TemplateEngineFactory
│   ├── UserManagement/  NewPermissionService、RoleService、DataPermissionManager
│   ├── Platform/  平台相关服务
│   └── Utilities/  OfficeProviderDetector、HolidayService、BusinessTimelineService
├── ViewModels/     与 Pages 同构；Base\ViewModelBase.cs 是三层基类（见 §6）
├── config/         app.ini、database.ini(+.example)、performance.ini、network.ini、update.ini
│                   + document_output.yaml、print_settings.yaml、timeline_config.yaml；读取一律走 IConfigService（§11）
├── deploy/         发布与在线更新：publish_all(.bat/.ps1)、publish_release、download_update、
│                   PatchTool、UpdateSigningTool、README.md（本机参数 *.local.ps1 不入库）
├── installer/      Inno Setup：NewCosmosSetup.iss、Languages\、appicon.ico
├── Scripts/        Lottery\（彩票 Python：lstm_entry.py 统一入口 + 内嵌原版 KittenCN/predict_Lottery_ticket「基于tensorflow lstm模型的彩票预测」GPL-3.0，运行时数据/模型在 %APPDATA%\NewCosmos\LotteryLSTM）、import_templates.ps1|.bat、
│                   templates_manifest.json；脚本内密码一律读环境变量 NEWCOSMOS_DB_PASSWORD
├── python-embed/   嵌入式 Python（彩票训练/预测用，不入库）
├── Templates_NEW/   模板资源（入库 7 文件）；公文字体/、keystore/ 为本机资产，**不入库**（§2）
├── publish/        发布产物（不入库）
├── docs/           业务文档 + migrations/（权威事实来源索引见 §14）
└── AGENTS.md（本文件）、AGENTS.local.md（本机凭据，不入库）、opencode.json（instructions 指回本文件）、
    .opencode/（opencode 本地依赖缓存，不入库）
```

## §4 命名规范

| 对象 | 规则 | 示例 |
|---|---|---|
| 命名空间 | `NewCosmos.{目录层级}` | `NewCosmos.Services.Domain.SocialAssistance` |
| 服务 | `{名词}Service` + `I{名词}Service` | `ApplicationService` / `IApplicationService` |
| ViewModel / Page | `{名词}ViewModel` / `{名词}Page` | `ApplicationListViewModel` / `ApplicationListPage` |
| 私有字段 | `_camelCase` | `_dbService` |
| 异步方法 | `{动词}{名词}Async` | `GetPagedAsync` |
| 数据库表 | 前缀分域：`nc_biz_`（业务）`nc_sys_`（系统）`nc_config_`（配置）`nc_dict_`（字典）`nc_perm_`（权限）`nc_regions_`（地区） | `nc_biz_applications` |
| 数据库列 | snake_case（映射层自动转 PascalCase 属性） | `applicant_id_card` → `ApplicantIdCard` |
| 文档 | `docs/YYYYMMDD_功能名称.md` | `docs\20260924_业务节点文书直出规范.md` |

## §5 禁止事项速查

| # | 禁止 | 正确做法 | 详见 |
|---|---|---|---|
| 1 | SQL 字符串插值 / 拼接值 | `$1..$n` 位置参数；动态表名过 `TableNameValidator` | §8 |
| 2 | `SELECT *`（宽表）/ 无 LIMIT 列表查询 / `EXTRACT()` 包列 | 明确列 + LIMIT + 范围比较 | §8 |
| 3 | 循环内逐行 SQL（N+1） | `= ANY($1)` 批查 / 多行 VALUES 批写 | §8 |
| 4 | 吞异常返回空集合 | `Result.Failure` 显式失败 | §7 |
| 5 | 明文密码 / 凭据入文件或产物 | DPAPI `enc:` / 环境变量 | §2 §12 |
| 6 | 硬编码路径、金额、阈值 | config / `nc_config_*` 表 / Constants | §11 |
| 7 | `new` ViewModel/Service；新服务漏注册 DI | DI 解析 + `MauiProgram.cs` 注册 | §6 |
| 8 | CanExecute/getter/热路径写日志或 IO | 移出热路径，Debug 级 | §6 §10 |
| 9 | 未脱敏 PII 入日志 | `DataMasker` | §10 |
| 10 | 给 DB 服务的事务方法加 async | 保持同步帧写 AsyncLocal | §8 |
| 11 | Singleton 服务存放每操作可变状态 | 局部变量 / 不可变快照替换 | §6 |
| 12 | 无条件 `DROP TABLE`/`TRUNCATE` 不带确认与备份 | 二次确认 + 先备份 | §13 |
| 13 | 引入 `Shell` / `AppShell` / 使用 `Shell.Current` | `Navigation\NavigationService` + `PushAsync` | §1 |
| 14 | `& $ps1 @数组` 传命名参数（数组=位置参数） | 哈希表 splat `& $ps1 @{Version=$v}` | §2 |
| 15 | 手改 `publish\` 产物或在其中留测试文件 | 发版产物由 `deploy\publish_all` 全量生成 | §2 |

## §6 架构铁律

1. **分层单向依赖**：Page → ViewModel → Service → IDatabaseService。ViewModel 不写 SQL，Service 不引用 UI 类型。
2. **DI 生命周期**：Service 一律 `AddSingleton`，ViewModel/Page 一律 `AddTransient`（注册在 `MauiProgram.cs`）。禁止 `new` ViewModel/Service——从 DI 解析。**新增服务必须同时注册进 MauiProgram.cs**（漏注册 = 运行时 `GetRequiredService` 崩溃，历史上真实发生过 3 处）。
3. **Singleton 服务禁止持有每次操作的可变实例状态**（并发操作互相污染）；确需缓存用 `ConcurrentDictionary` 或不可变快照整体替换。
4. **ViewModel 基类**（`ViewModels\Base\ViewModelBase.cs`）：`ViewModelBase`（IsBusy/ErrorMessage/ExecuteAsync 包装）、`PagedSearchViewModelBase`（分页搜索）、`FormViewModelBase`（多步表单）。
5. **CanExecute 与属性 getter 里禁止日志/IO/DB**——它们被 XAML 反复求值。

## §7 错误处理：Result 模式

- Service 层返回 `Result` / `Result<T>`（`Models\Results\`），**不用异常做业务流控制**。
- 失败：`Result.Failure<T>(ErrorCodes.XXX, "消息")`；错误码在 `Constants\ErrorCodes.cs`，用户可读消息映射在 `Constants\UserFriendlyMessages.cs`。
- 异常转换：`Result.FromException<T>(ex)`。
- **加载失败必须显式失败，绝不允许"吞异常返回空集合"**——空集会被当作"无数据"保存回去，静默清空真实数据（EconomicDetailService 曾因此有数据丢失风险）。
- 范例：`Services\Domain\SocialAssistance\ApplicationService.cs` 及对应 ViewModel。
- 常见 PostgreSQL 错误码：`23505` 唯一冲突 / `23503` 外键 / `40001` 序列化失败 / `40P01` 死锁 / `57014` 查询取消。

## §8 数据库访问与事务

接口 `Services\Database\IDatabaseService.cs`（**注意 ct 在第二位，参数在最后**）：

```csharp
Task<Result<T>>        QuerySingleAsync<T>(string sql, CancellationToken ct = default, params object[] parameters);
Task<Result<List<T>>>  QueryAsync<T>(...同上);
Task<Result<int>>      ExecuteNonQueryAsync(...同上);
Task<Result<long>>     ExecuteScalarAsync(...同上);        // 历史 API：无行返回 Success(0)，勿用于 RETURNING id
Task<Result<T?>>       ExecuteScalarAsync<T>(...同上);      // 新代码用这个：无行/NULL → Success(default)
Task<ITransactionScope> BeginTransactionScopeAsync(CancellationToken ct = default);   // 唯一事务入口
Task BeginTransactionAsync(ct); Task CommitTransactionAsync(ct); Task RollbackTransactionAsync(ct);   // 已迁移废弃：0 调用方，新代码禁止使用
bool HasTransaction;   // 当前异步流是否有活动事务
```

- **SQL 只用 `$1,$2...` 位置参数**，值绝不插值进 SQL 字符串。表名如需动态必须过白名单（`Helpers\TableNameValidator.cs` 单点维护）。
- **事务是环境事务（AsyncLocal，按异步流隔离）**：Begin 之后同一异步流的查询自动入事务；并发操作互不影响。**全库统一写法（2026-10 已完成 219+ 处存量迁移）**：

```csharp
await using var tx = await _db.BeginTransactionScopeAsync(ct);
// ...一系列查询自动挂到事务上...
await tx.CommitAsync(ct);      // 未 Commit 则 Dispose 自动回滚
```

- **嵌套语义**：外层已有活动事务时，`BeginTransactionScopeAsync` 加入外层事务（不再抛"已有活动事务"），嵌套作用域的 Commit/Rollback/Dispose 均为 no-op，事务所有权归最外层作用域；嵌套参与者的失败经返回值上抛，由外层决定回滚（等价旧 `HasTransaction` 惯用法）。
- ⚠️ `PostgreSqlDatabaseService` 中 Begin/Commit/Rollback/BeginScope **必须保持非 async 方法**（AsyncLocal 写入只在同步帧对调用方可见）——文件内有注释，勿"顺手"加 async。
- 查询规范：明确列名代替 `SELECT *`（`nc_biz_applications` 有 380+ 列）；列表查询必须 LIMIT；批量条件用 `= ANY($1)` 传数组而非展开 IN 列表；时间过滤用范围比较（`col >= $1 AND col < $2`），**禁止 `EXTRACT(YEAR FROM col)=$1`**（索引失效）；批量写入用多行 VALUES。

## §9 金额口径铁律（财政会计基准）

1. **年值是权威基准**：家庭收入 = Σ(月项×12) + 赡养年值 + 土地年值 + 补贴年值 − 刚性支出×12，**先汇总、最后一次性舍入到分**（`IncomeCalculationService.CalculateAnnualFamilyIncome`）。
2. **月值只是分解显示**：月均 = 年值÷12（`MonthlyFromAnnual`/`PerCapitaMonthly`），**永远禁止从已舍入的月值×12 反推年值**（会产生 round-trip 误差，如 53.33×12≠640.03）。
3. **明细不提前舍入**：赡养费（`CalculateAlimonyAnnual`，逐笔年费舍入后求和）、补贴（`Subsidy.CalculateAmount`）计算过程中保留全精度，仅最终汇总舍入。
4. **公式单点实现**：收入计算只允许走 `IncomeCalculationService`，**禁止 ViewModel/Service 各自重抄公式**（曾双实现导致明细与汇总分裂）。
5. **主表存储语义**（`nc_biz_applications`）：`work/business/property/transfer/other/rigid_expenditure/total_family_income/per_capita_income` 为**月值**；`alimony_income/land_income_total/subsidy_total/total_annual_income/per_capita_annual_income` 为**年值**。新增金额字段必须标注月/年口径。
6. **判定与落库同口径**：分类判定用 `TotalAnnualIncome/12/人数` 精确口径（`ClassificationService`），落库月人均 = 年÷人数÷12 一次舍入。
7. **保障金不自动重算**：收入口径修正（如月值 53.33→53.34）可能越过 Ceiling 取整边界，保障金列属审批结果，需人工核定后走变更流程，禁止批量自动改。

## §10 日志

- API：`BaseService` 的 `LogInfo/LogWarn/LogError/LogException`；分类日志走 `ILoggerService` 的 `LogBusiness(...)`（BIZ）、`LogSecurity(...)`（SEC）、`StartPerfTimer/StopPerfTimer`（PERF）。
- 落盘（Serilog，异步 sink，按类别分流）：`app_.log` 通用 / `biz_.log` 业务 / `sec_.log` 安全 / `err_.log` 全部错误 / `perf_.log` 性能。最低级别 Information（Debug 需临时改 `LoggerService.cs` 一行）。
- **脱敏强制**：姓名/身份证/电话/银行账号入日志前过 `Services\Core\DataMasker.cs`（`MaskName/MaskIdCard/...`）。
- 热路径（每键、每行、CanExecute、每次绑定求值）禁止 Info 级日志；循环内日志用 Debug 或聚合后记一条。
- **必记的业务审计点**以 `Services\Core\ILoggerService.cs` 的 `LogBusiness`/`LogSecurity` 调用约定为准（原 `docs\audit-logging.md` 已不存在，勿引用）。

## §11 配置

- 文件：`config\app.ini`（版本/窗口标题）、`database.ini`（+`database.ini.example`，见 §2）、`performance.ini`（重试/超时/慢操作阈值）、`network.ini`、`update.ini`（在线更新）+ `document_output.yaml`、`print_settings.yaml`、`timeline_config.yaml`。
- **文档输出根**：`document_output.yaml` → `output.base_directory`（占位 `{Documents}`/`{AppData}`，默认落用户「文档」）；用户在「系统设置」（主页顶栏设置按钮 → `SettingsPage`）所选目录存 **AppData 用户级覆盖** `document_output.user.json`，优先级 覆盖 > yaml > 历史相对`输出`。改根即时生效并由 `OutputRootMigrationService` 后台把旧根复制过去 + 改写留痕表路径（权威细则见 `docs\20261007_文档动作与输出路径统一规范.md` §2）。
- 读取一律走 `IConfigService`（`Services\Core\ConfigService.cs`，进程内缓存）。**禁止硬编码**：路径、连接串、金额标准、阈值——分别归 config、`nc_config_*` 表、Constants。
- 注意：INI 解析按 key 全文件匹配、忽略 section；跨 section 重名 key 会告警并被后者覆盖。

## §12 安全

- 密码存储：BCrypt（`UserService` 的 HashPassword/VerifyPassword），禁止 MD5/SHA/明文比较。
- 凭据：任何文件/脚本禁止出现明文数据库密码；`Scripts\`、`deploy\` 下统一读环境变量 `NEWCOSMOS_DB_PASSWORD`；服务器/签名等本机参数放 `deploy\deploy.local.ps1`、`AGENTS.local.md`（均忽略不入库）。
- 权限检查：操作前 `INewPermissionService.HasPermissionAsync` / 批量 `CheckPermissionsAsync`；权限码在 `Constants\PermissionCodes.cs`。
- PII（公民数据文件）不入代码目录；`publish/`、`输出\`、`Logs\` 不入安装包；日志脱敏见 §10。

## §13 工作流程纪律

1. **先出计划，用户确认后再动代码**；说明改哪些文件、为什么。
2. 动手前按 §2 的 backup 惯例备份将改动的文件。
3. 禁止盲目全局替换（replaceAll）——逐处确认上下文后修改。
4. 修复问题治本优先：先找根因，不加"绕过式补丁"；同类问题一次修全（grep 确认无同型残留）。
5. 每批改动后 `dotnet build` 零错误；涉及数据库行为的改动补充说明冒烟验证路径。
6. 完成后总结：改了什么、为什么、怎么验证。
7. 修改本文件时，先用磁盘/`git ls-files` 核实所写事实，禁止照抄旧清单。

## §14 业务规则权威来源

> 本文不复述细则，只指向唯一权威；改这些域前先读对应源文件。部分历史文档已不存在，一律以代码为准。

| 主题 | 唯一权威代码 | 备注 |
|---|---|---|
| 分类认定 / 收入计算 / 渐退期 / 字段校验 | `Services\Domain\SocialAssistance\ClassificationService.cs`、`IncomeCalculationService.cs`、`Constants\ClassificationConstants.cs`、`GracePeriodConstants.cs` | 口径铁律见 §9（原 `docs\business-rules.md` 已不存在） |
| 申请状态机（Draft→Submitted→Approved→…） | `Services\StateMachine\ApplicationStateMachine.cs` | 唯一权威，勿另抄流转表 |
| 审计 / 业务日志必记点 | `Services\Core\ILoggerService.cs`（`LogBusiness`/`LogSecurity` 调用约定） | 原 `docs\audit-logging.md` 已不存在 |
| 模板字段 FieldKey 命名 | `Constants\FieldKeys.cs` | 原 `docs\template-fieldkey.md`、`docs\compose\...` 均已不存在 |
| 身份证校验 | `Helpers\IdCardValidator.cs` | |
| 彩票奖级与金额 | SSQ/DLT 奖级判定唯一权威 `Services\Lottery\UserPurchaseService.cs`（含大乐透 26014 期起 9→7 奖级改版），奖金解析 `Helpers\LotteryPrizeResolver.cs`；算法与 LSTM 预测 `Scripts\Lottery\lstm_entry.py`（适配层：数据导出/窗口/负样本降权/JSON）+ `Scripts\Lottery\predict_Lottery_ticket\`（原版 LSTM，commit 6cf60bb7，GPL-3.0；其 `src\bootstrap.py` 承担 TF2.21/Keras3 兼容） | 训练/预测数据源 = `nc_lottery_draws`（由 `fetch_history.py` 同步），算法侧不联网抓取；`nc_lottery_predictions`、`nc_lottery_statistics` 已无代码读写 |
| 申请主表与业务表结构 | `Resources\Schema\{域}\*.yaml` | 手写迁移 `docs\migrations\*.sql` |
| 文档四件套与输出路径 | `Constants\DocumentActionText.cs`、`Helpers\OutputPathHelper.cs`、`config\document_output.yaml`、`Services\Core\ConfigService.cs`（用户级覆盖读写）、`Pages\Config\SettingsPage`（改根入口）、`docs\20261007_文档动作与输出路径统一规范.md` | 预览临时/保存落根铁律；六功能点对照与输出根优先级见该文档 |
| 在线更新与增量补丁 | `deploy\README.md`（公开库权威）、`deploy\PatchTool`、`deploy\UpdateSigningTool` | 入口见 §2；本机 `docs\20260918_在线更新系统.md` 含内网信息，未入库 |
