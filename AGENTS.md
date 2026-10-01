# NewCosmos 开发规范（AGENTS.md）

> 更新日期：2026-07-29。本文件只保留"改这个代码库必须知道的事实与规则"；
> 业务规则、审计清单等参考资料在 `docs/`（见 §9）。
> 单一事实来源原则：本文不抄代码——凡涉及可执行逻辑，以指向的源文件为准。

---

## 一、项目概览

- **项目名 / 命名空间**：`NewCosmos`（`RootNamespace=NewCosmos`，所有命名空间为 `NewCosmos.*`）
- **应用显示名**：帝皇权杖δ-me13（民政社会救助管理系统，仅 Windows 桌面）
- **技术栈**：.NET 10 MAUI（`net10.0-windows10.0.19041.0`，WinUI3，未打包 `WindowsPackageType=None`）+ CommunityToolkit.Mvvm 8.4 + PostgreSQL（Npgsql 10，自研 `IDatabaseService`，无 EF/Dapper）+ Serilog 4 + EPPlus 8/ExcelDataReader（Excel）+ Office/WPS COM（打印导出）
- **XAML 管线**：`MauiXamlInflator=SourceGen`（编译期源生成）+ GlobalXmlns（根命名空间 `http://schemas.microsoft.com/dotnet/maui/global`，注册表在 `GlobalXmlns.cs`；隐式命名空间为预览特性，csproj 已开 `EnablePreviewFeatures` 并抑制 CA2252）。**约定**：ViewModel 等唯一类型直接无前缀引用（如 `x:DataType="LoginViewModel"`）；共享前缀（components/converters/controls/constants/cat/results/pages/assetsvc/chg/domain/sys）全局可用；`ent:`/`uent:` 因重名（Application、Role）须在各文件内保留经典 `clr-namespace` 声明；`App.xaml` 是例外文件，保留经典 MAUI 命名空间。
- **架构**：Pages(XAML) → ViewModels(MVVM) → Services(Domain/System/Core) → IDatabaseService → PostgreSQL
- **导航**：`NavigationPage` + `Navigation.PushAsync`。**AppShell 是死代码**——未注册进 DI，不要解析或导航到它。

## 二、构建与运行

```bash
dotnet build NewCosmos.csproj -f net10.0-windows10.0.19041.0
```

- 运行：构建后启动 `bin\Debug\net10.0-windows10.0.19041.0\win-x64\NewCosmos.exe`（.NET 10 起 RID 目录名为 `win-x64`，不再是 `win10-x64`）
- 发版（在线更新）：`Scripts\publish_release.ps1 -Version 1.1.yyyyMMdd [-MinSupported 1.1.xxx] [-Force] [-NotesFile notes.md]`——版本三处同步→Release 自包含发布→Inno 安装包→清单签名→上传服务器，详见 `docs\20260918_在线更新系统.md`
- **前置条件**：.NET 10 SDK + `maui-windows` 工作负载；`config\database.ini` 必须存在。开发机放在项目 `config\` 目录（**不会**被复制进构建输出——凭据不随产物分发，csproj 已显式排除）；部署机由配置向导生成或手工放置在 exe 旁的 `config\` 目录。
- 数据库密码：ini 中明文填写，应用首次启动自动加密为 `enc:` 前缀密文（DPAPI 机器级，不可跨机复制）。
- Schema：权威定义在 `Resources\Schema\{域}\*.yaml`；`SchemaService` 负责建表/补索引/补外键（幂等）；手写迁移在 `docs\migrations\*.sql`。
- **没有测试项目**。验证方式 = 编译零错误 + 启动应用跑冒烟（登录→列表→表单保存→导入→打印预览）。
- **已启用 git**（2026-09 开源初始化）。敏感文件（`config\database.ini`、`config\network.ini`、`config\update.ini`、`Scripts\`、`AGENTS.local.md` 等）已 gitignore；修改前仍按相对路径复制到 `backup\<yyyyMMdd_HHmmss>_<用途>\`（便于回滚与对比）。

## 三、目录结构（实况）

```
NewCosmos/
├── App.xaml(.cs)、AppShell.xaml(.cs)†、MauiProgram.cs      † AppShell 为死代码
├── GlobalXmlns.cs   GlobalXmlns 注册表（XmlnsDefinition/XmlnsPrefix，见 §1 XAML 管线）
├── Components/      6 个共享组件：LoadingOverlay、ModuleShellView、NavigationViewItem、
│                    ProgressPopupView、SchemaValidationResultPopupView、SnackBarView
├── Constants/       28 个常量类（见 §10）
├── Controls/        PdfPreviewView（WebView2 + PdfJs）
├── Converters/      26 个值转换器（注册在 App.xaml）
├── Helpers/         AddressResolver、ALinePeriodHelper、DictDisplayHelper、IdCardValidator、
│                    ImportedDataMapper、OutputPathHelper、PageDefaultValues、PinyinConverter、TableNameValidator
├── Models/          Entities / Options / Results / Schema / Exceptions / NavigationData …
├── Pages/           按域分目录：Auth、Main、SocialAssistance、AssetVerification、ArchiveManagement、
│                    ChangeManagement、DatabaseManagement、UserManagement、Reporting、Config、Shared
├── Platforms/Windows/   Windows 平台服务（文件夹选择、打印机）
├── Resources/       Schema/（YAML 表定义）、Seed/、Styles/、PdfJs/、Backgrounds/
├── Services/
│   ├── Core/        BaseService、LoggerService、ConfigService、DialogService、DictCacheService、
│   │                InitializationService、SeedMergeService、DataMasker、LoadingProgressService
│   ├── Database/    IDatabaseService、PostgreSqlDatabaseService、ITransactionScope、SchemaService
│   ├── Domain/      SocialAssistance / AssetVerification / ArchiveManagement / ChangeManagement /
│   │                Printing / Reporting / SpecialApproval / UserManagement
│   ├── Import/      BaseImportService、BaseCombinedImportService + 11 个导入服务
│   ├── StateMachine/ ApplicationStateMachine
│   ├── System/      RegionService、DictionaryService、StandardConfigService、DatabaseManagementService
│   ├── Templates/   ExcelEngine、WordEngine、TemplateEngineFactory
│   ├── UserManagement/  NewPermissionService、RoleService、DataPermissionManager
│   └── Utilities/   OfficeProviderDetector、HolidayService、BusinessTimelineService
├── ViewModels/      与 Pages 同构分目录；Base/ViewModelBase.cs 是三层基类
├── config/          app.ini、database.ini、performance.ini + 3 个 yaml（document_output/print_settings/timeline）
├── docs/            业务文档 + migrations/ + compose/
└── Scripts/         一次性运维脚本（不参与编译；密码一律读环境变量 NEWCOSMOS_DB_PASSWORD）
```

## 四、命名规范

| 对象 | 规则 | 示例 |
|---|---|---|
| 命名空间 | `NewCosmos.{目录层级}` | `NewCosmos.Services.Domain.SocialAssistance` |
| 服务 | `{名词}Service` + `I{名词}Service` | `ApplicationService` / `IApplicationService` |
| ViewModel / Page | `{名词}ViewModel` / `{名词}Page` | `ApplicationListViewModel` / `ApplicationListPage` |
| 私有字段 | `_camelCase` | `_dbService` |
| 异步方法 | `{动词}{名词}Async` | `GetPagedAsync` |
| 数据库表 | 前缀分域：`nc_biz_`（业务）`nc_sys_`（系统）`nc_config_`（配置）`nc_dict_`（字典）`nc_perm_`（权限）`nc_regions_`（地区） | `nc_biz_applications` |
| 数据库列 | snake_case（映射层自动转 PascalCase 属性） | `applicant_id_card` → `ApplicantIdCard` |
| 文档 | `docs/YYYYMMDD_功能名称.md` | |

## 五、架构铁律

1. **分层单向依赖**：Page → ViewModel → Service → IDatabaseService。ViewModel 不写 SQL，Service 不引用 UI 类型。
2. **DI 生命周期**：Service 一律 `AddSingleton`，ViewModel/Page 一律 `AddTransient`（注册在 `MauiProgram.cs`）。禁止 `new` ViewModel/Service——从 DI 解析。**新增服务必须同时注册进 MauiProgram.cs**（漏注册 = 运行时 `GetRequiredService` 崩溃，历史上真实发生过 3 处）。
3. **Singleton 服务禁止持有每次操作的可变实例状态**（并发操作会互相污染）；确需缓存用 `ConcurrentDictionary` 或不可变快照整体替换。
4. ViewModel 基类：`ViewModelBase`（IsBusy/ErrorMessage/ExecuteAsync 包装）、`PagedSearchViewModelBase`（分页搜索）、`FormViewModelBase`（多步表单），见 `ViewModels\Base\ViewModelBase.cs`。
5. **CanExecute 与属性 getter 里禁止日志/IO/DB**——它们被 XAML 反复求值。

## 六、错误处理：Result 模式

- Service 层返回 `Result` / `Result<T>`（`Models\Results\`），**不用异常做业务流控制**。
- 失败：`Result.Failure<T>(ErrorCodes.XXX, "消息")`；错误码在 `Constants\ErrorCodes.cs`，用户可读消息映射在 `Constants\UserFriendlyMessages.cs`。
- 异常转换：`Result.FromException<T>(ex)`。
- **加载失败必须显式失败，绝不允许"吞异常返回空集合"**——空集会被当作"无数据"保存回去，静默清空真实数据（EconomicDetailService 曾因此有数据丢失风险）。
- 范例：`Services\Domain\SocialAssistance\ApplicationService.cs` 及对应 ViewModel。
- 常见 PostgreSQL 错误码：`23505` 唯一冲突 / `23503` 外键 / `40001` 序列化失败 / `40P01` 死锁 / `57014` 查询取消。

## 七、数据库访问与事务

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

## 七·五、金额口径铁律（财政会计基准）

1. **年值是权威基准**：家庭收入 = Σ(月项×12) + 赡养年值 + 土地年值 + 补贴年值 − 刚性支出×12，**先汇总、最后一次性舍入到分**（`IncomeCalculationService.CalculateAnnualFamilyIncome`）。
2. **月值只是分解显示**：月均 = 年值÷12（`MonthlyFromAnnual`/`PerCapitaMonthly`），**永远禁止从已舍入的月值×12 反推年值**（会产生 round-trip 误差，如 53.33×12≠640.03）。
3. **明细不提前舍入**：赡养费（`CalculateAlimonyAnnual`，逐笔年费舍入后求和）、补贴（`Subsidy.CalculateAmount`）计算过程中保留全精度，仅最终汇总舍入。
4. **公式单点实现**：收入计算只允许走 `IncomeCalculationService`，**禁止 ViewModel/Service 各自重抄公式**（曾双实现导致明细与汇总分裂）。
5. **主表存储语义**（`nc_biz_applications`）：`work/business/property/transfer/other/rigid_expenditure/total_family_income/per_capita_income` 为**月值**；`alimony_income/land_income_total/subsidy_total/total_annual_income/per_capita_annual_income` 为**年值**。新增金额字段必须标注月/年口径。
6. **判定与落库同口径**：分类判定用 `TotalAnnualIncome/12/人数` 精确口径（`ClassificationService`），落库月人均 = 年÷人数÷12 一次舍入。
7. **保障金不自动重算**：收入口径修正（如月值 53.33→53.34）可能越过 Ceiling 取整边界，保障金列属审批结果，需人工核定后走变更流程，禁止批量自动改。

## 八、日志

- API：`BaseService` 的 `LogInfo/LogWarn/LogError/LogException`；分类日志走 `ILoggerService` 的 `LogBusiness(...)`（BIZ）、`LogSecurity(...)`（SEC）、`StartPerfTimer/StopPerfTimer`（PERF）。
- 落盘（Serilog，异步 sink，按类别分流）：`app_.log` 通用 / `biz_.log` 业务 / `sec_.log` 安全 / `err_.log` 全部错误 / `perf_.log` 性能。最低级别 Information（Debug 需临时改 `LoggerService.cs` 一行）。
- **脱敏强制**：姓名/身份证/电话/银行账号入日志前过 `Services\Core\DataMasker.cs`（`MaskName/MaskIdCard/...`）。
- 热路径（每键、每行、CanExecute、每次绑定求值）禁止 Info 级日志；循环内日志用 Debug 或聚合后记一条。
- 必记的业务审计点清单：`docs\audit-logging.md`。

## 九、业务规则（外部文档）

| 主题 | 文档 | 代码事实来源 |
|---|---|---|
| 分类认定 / 收入计算 / 渐退期 / 字段校验 | `docs\business-rules.md` | `Services\Domain\SocialAssistance\ClassificationService.cs`、`IncomeCalculationService.cs`、`Constants\ClassificationConstants.cs`、`GracePeriodConstants.cs` |
| 申请状态机（Draft→Submitted→Approved→…） | — | `Services\StateMachine\ApplicationStateMachine.cs`（唯一权威，勿另抄流转表） |
| 审计日志清单 | `docs\audit-logging.md` | `Services\Core\ILoggerService.cs` |
| 模板字段 FieldKey 命名 | `docs\template-fieldkey.md`、`docs\compose\specs\template-field-mapping-rules.md` | `Constants\FieldKeys.cs` |
| 身份证校验 | — | `Helpers\IdCardValidator.cs` |

## 十、常量与组件速查

高频常量类（全部在 `Constants\`，28 个，改业务逻辑前先查这里，禁止散落魔法值）：
`ErrorCodes`、`UserFriendlyMessages`、`FieldKeys`、`PermissionCodes`、`ClassificationConstants`、`GracePeriodConstants`、`DictionaryConstants`/`DictionaryTypeCodes`、`IncomeTypeConstants`、`RigidExpenditureConstants`、`LandStatusConstants`、`DisabilityConstants`、`AssistanceCategoryConstants`、`ImportTypeCodes`、`UIConstants`、`PickerConstants`、`DefaultValuesConstants`。

共享组件（`Components\`）：`LoadingOverlay`（加载遮罩）、`SnackBarView`（轻提示）、`ProgressPopupView`（进度弹窗）、`ModuleShellView`/`NavigationViewItem`（模块壳/导航项）、`SchemaValidationResultPopupView`。自定义控件：`Controls\PdfPreviewView`。

## 十一、配置

- `config\app.ini`（版本/窗口标题）、`database.ini`（连接与凭据，见 §2）、`performance.ini`（重试/超时/慢操作阈值）、`update.ini`（在线更新，见 `docs\20260918_在线更新系统.md`）+ `document_output.yaml`、`print_settings.yaml`、`timeline_config.yaml`。
- 读取一律走 `IConfigService`（`Services\Core\ConfigService.cs`，进程内缓存）。**禁止硬编码**：路径、连接串、金额标准、阈值——分别归 config、`nc_config_*` 表、Constants。
- 注意：INI 解析按 key 全文件匹配、忽略 section；跨 section 重名 key 会告警并被后者覆盖。

## 十二、安全

- 密码存储：BCrypt（`UserService` 的 HashPassword/VerifyPassword），禁止 MD5/SHA/明文比较。
- 凭据：任何文件/脚本禁止出现明文数据库密码；`Scripts\` 下统一读环境变量 `NEWCOSMOS_DB_PASSWORD`。
- 权限检查：操作前 `INewPermissionService.HasPermissionAsync` / 批量 `CheckPermissionsAsync`；权限码在 `Constants\PermissionCodes.cs`。
- PII（公民数据文件）不入代码目录；日志脱敏见 §8。

## 十三、工作流程纪律

1. **先出计划，用户确认后再动代码**；说明改哪些文件、为什么。
2. 动手前按 §2 的 backup 惯例备份将改动的文件。
3. 禁止盲目全局替换（replaceAll）——逐处确认上下文后修改。
4. 修复问题治本优先：先找根因，不加"绕过式补丁"；同类问题一次修全（grep 确认无同型残留）。
5. 每批改动后 `dotnet build` 零错误；涉及数据库行为的改动补充说明冒烟验证路径。
6. 完成后总结：改了什么、为什么、怎么验证。

## 十四、禁止事项速查

| # | 禁止 | 正确做法 | 详见 |
|---|---|---|---|
| 1 | SQL 字符串插值 / 拼接值 | `$1..$n` 位置参数；动态表名过白名单 | §7 |
| 2 | `SELECT *`（宽表）/ 无 LIMIT 列表查询 / `EXTRACT()` 包列 | 明确列 + LIMIT + 范围比较 | §7 |
| 3 | 循环内逐行 SQL（N+1） | `= ANY($1)` 批查 / 多行 VALUES 批写 | §7 |
| 4 | 吞异常返回空集合 | Result.Failure 显式失败 | §6 |
| 5 | 明文密码 / 凭据入文件或产物 | DPAPI enc: / 环境变量 | §2 §12 |
| 6 | 硬编码路径、金额、阈值 | config / nc_config_* / Constants | §11 |
| 7 | `new` ViewModel/Service；新服务漏注册 DI | DI 解析 + MauiProgram 注册 | §5 |
| 8 | CanExecute/getter/热路径写日志或 IO | 移出热路径，Debug 级 | §5 §8 |
| 9 | 未脱敏 PII 入日志 | DataMasker | §8 |
| 10 | 给 DB 服务的事务方法加 async | 保持同步帧写 AsyncLocal | §7 |
| 11 | Singleton 服务存放每操作可变状态 | 局部变量 / 快照替换 | §5 |
| 12 | 无条件 `DROP TABLE`/`TRUNCATE` 不带确认与备份 | 二次确认 + 先备份 | — |
| 13 | 使用 `Shell.Current` / 解析 AppShell | NavigationPage + PushAsync | §1 |

## 十五、数据库连接信息

- 公开仓库不包含任何内部主机/账号/密码；本地开发按 `config\database.ini.example` 在 `config\database.ini` 填写（已 gitignore，首次启动自动 DPAPI 加密为 `enc:`）。
- 本机开发连接信息（含凭据）见本地 `AGENTS.local.md`（已 gitignore，不入库）。
