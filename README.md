# NewCosmos（帝皇权杖δ-me13）

民政社会救助管理系统 —— 面向乡镇/街道民政业务的一体化 Windows 桌面应用，覆盖低收入人口认定、特困供养、高龄津贴、临时救助、资产核查、变更管理、档案制作与统计月报。

## 功能概览

- **低收入人口认定**：五步申请表单、收入核算（年值权威口径）、三层分类判定（低保 / 最低生活保障边缘家庭 / 特困人员 / 刚性支出困难家庭 / 单人保）、渐退期管理。
- **动态管理**：经济状况复核、家庭信息修正、家庭成员变更、户主变更、死亡变更，停旧建新与变更链追溯。
- **资产核查**：银行存款、有价证券、车辆、农机等经济核对档案。
- **档案制作与打印**：Word/Excel/PDF 模板引擎（Office/WPS COM）、PDF 预览、批量补打。
- **统计报表**：月报、分类施保增减、停保/新增汇总、低收入证明等。
- **系统能力**：RBAC + 数据权限、字典与标准配置、节假日/时间线、Schema 幂等建表、Seed 合并、在线更新（可选）。

## 技术栈

| 层 | 技术 |
|---|---|
| UI | .NET 10 MAUI（WinUI 3，未打包 `WindowsPackageType=None`） |
| MVVM | CommunityToolkit.Mvvm 8.x |
| 数据库 | PostgreSQL（Npgsql，自研 `IDatabaseService`，手写 SQL，无 EF/Dapper） |
| Schema | `Resources\Schema/**/*.yaml`（`SchemaService` 幂等建表/补索引/补外键） |
| 日志 | Serilog（按类别分流 app/biz/sec/err/perf） |
| 文档 | EPPlus 8 / ExcelDataReader / Office・WPS COM |

## 环境要求

- Windows 10 1809+ / Windows 11
- .NET 10 SDK + `maui-windows` 工作负载：
  ```powershell
  dotnet workload install maui-windows
  ```
- PostgreSQL 14+（本机或远程）

## 构建与运行

```powershell
# 1. 准备数据库配置（凭据不入库）
Copy-Item config\database.ini.example config\database.ini
# 编辑 config\database.ini 填写 Host / Port / DatabaseName / Username / Password

# 2. 构建
dotnet build NewCosmos.csproj -f net10.0-windows10.0.19041.0

# 3. 运行
.\bin\Debug\net10.0-windows10.0.19041.0\win-x64\NewCosmos.exe
```

- 首次启动自动执行建表、补索引与种子合并（`SchemaService` / `SeedMergeService`）。
- `config\database.ini` 中的明文密码在首次启动后自动加密为 `enc:` 前缀密文（DPAPI 机器级），密文不可跨机复制。
- `config\network.ini`、`config\update.ini` 为可选文件；缺失时使用内置默认配置（网络接入/在线更新关闭或按需配置）。

## 目录结构（节选）

```
NewCosmos/
├── App.xaml(.cs)、MauiProgram.cs         应用入口与 DI 注册
├── Components/ Controls/ Converters/    共享 UI 组件、控件与转换器
├── Constants/                           业务常量（错误码、字段键、权限码…）
├── Helpers/                             校验/映射/路径等辅助类
├── Models/                              Entities / Options / Results / Schema / Requests
├── Pages/ ViewModels/                   按业务域划分的页面与视图模型（MVVM）
├── Services/
│   ├── Core/        日志、配置、字典缓存、对话框、加载进度
│   ├── Database/    IDatabaseService / PostgreSQL 实现 / 事务 / SchemaService
│   ├── Domain/      社会救助各业务域服务（认定、变更、档案、报表、打印…）
│   ├── Import/      各类台账导入服务
│   ├── System/      地区、字典、标准配置
│   └── Templates/   Excel/Word 模板引擎
├── Resources/       Schema（YAML）、Seed、样式、Icd10、PdfJs 等
├── config/          运行配置（database.ini 不入库）
├── installer/       Inno Setup 安装包脚本
└── docs/            业务与迁移文档
```

## 未随仓库分发的组件

以下内容因体积或授权原因未纳入仓库，按需自行准备：

| 目录 | 说明 |
|---|---|
| `Resources\PostgreSQL\` | 备份/恢复功能所需 `pg_dump` 等客户端二进制（从 PostgreSQL 官方获取） |
| `Resources\ZeroTier\` | 网络接入功能所需 ZeroTier One 安装包（可选） |
| `公文字体\` | 公文排版字体（商用授权限制，请自备合法授权字体） |
| `python-embed\`、`Scripts\` | 本地实验/一次性运维脚本，不参与应用构建 |

## 参与开发

`AGENTS.md` 汇总了架构铁律、命名规范、金额口径、事务与日志约定，供贡献者与 AI 辅助开发参考。

## 开源许可

[GPL-3.0](LICENSE)。
