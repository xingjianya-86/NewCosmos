# deploy/ — 发布与部署工具（帝皇权杖δ-me13）

本目录集中存放**发版 / 部署 / 更新**相关脚本；**模板导入**脚本仍在 `Scripts/`。
所有脚本以本文件所在目录为基准，仓库根自动推导（`$PSScriptRoot` 的上级），移动到别处需同步调整。

> 内网地址（更新服务器 / 数据库）不入库，来自 `deploy\deploy.local.ps1`（已被 `.gitignore` 忽略）。
> 复制 `deploy.sample.ps1` 为 `deploy.local.ps1` 并填值即可。

---

## 一、脚本一览

| 脚本 | 作用 | 入口 |
|---|---|---|
| **publish_all.ps1 / .bat** | **一条龙发布**：Windows 安装包 + Android(AOT) APK → 签名 → 上传服务器 → **回环下载校验** → GitHub 提交推送 | 双击 `publish_all.bat` |
| publish_release.ps1 | **仅 Windows** 发布：版本同步 → `dotnet publish` → Inno 安装包 → 增量补丁 → 清单签名 → 上传 → 入库 → 校验（被 `publish_all` 调用，亦可单独用） | `.\publish_release.ps1 -Version x` |
| download_update.ps1 / .bat | 从更新服务器**下载最新包**：拉清单 → 验签 → 下载 → 校验 SHA256 | `download_update.bat -Platform android` |
| `PatchTool/` | 增量补丁工具（BsDiff 差分/应用），被发布脚本调用 | — |
| `UpdateSigningTool/` | 清单签名工具：`keygen` / `sign` / `verify`（RSA-2048-SHA256） | — |
| `deploy.sample.ps1` | 内网地址配置样例（复制为 `deploy.local.ps1`） | — |
| `deploy.local.ps1` | **本地真实配置**（gitignored）：更新服务器地址、DB、清单校验地址、psql 路径 | — |

> 模板导入（`import_templates.ps1/.bat`、`templates_manifest.json`）仍在 `Scripts/`。

---

## 二、一条龙：`publish_all`

双击 `publish_all.bat`（或 `.	\publish_all.ps1`）后，会先进入**交互向导**（见下表），再依次执行：

```
1) Windows : 同步版本号(3处) → dotnet publish → Inno 安装包 → 生成补丁(可选)
             → 签名 stable\update.json(schema=1/2) → 上传 stable\ + releases\<版本>\
             → 发布历史入库 → 线上校验
2) Android : dotnet publish(Release/android-arm64/AOT) → 生成补丁(可选)
             → 签名 android\update.json → 上传 /android/ → 线上校验
3) 回环下载 : 从服务器拉两端清单验签 → 下载两端安装包 → 校验 SHA256
4) GitHub  : git add 三处版本文件 → commit "发布 <版本>" → push origin main
```

### 交互向导（运行后按 Y/F 选择）
| 询问 | 默认 | 说明 |
|---|---|---|
| 版本号 | csproj 当前值 | 直接回车用默认；输入新号即同步三处版本号 |
| 生成增量补丁 | F（否） | Y=生成「上一版→本版」增量补丁（Windows exe + Android apk），清单 schema=2 |
| 非强制升级 | F（否） | Y=最低支持版本取"上一版"（低于它才强制）；默认=强制到本版 |
| 发布后推送 GitHub | Y（是） | F=不提交推送 |
| 回环下载校验 | Y（是） | F=跳过大文件下载（仅保留清单/签名校验） |

> 已显式传入的参数不会再询问；自动化/无人值守用 `-NonInteractive`。

### 常用参数
| 参数 | 说明 |
|---|---|
| `-Version 1.1.20261010` | 指定版本号（否则取 csproj） |
| `-WithPatch` / `-SoftUpdate` | 增量补丁 / 非强制升级（等价于向导选 Y） |
| `-MinSupported <ver>` | 显式最低支持版本（优先级最高） |
| `-PrevVersion <ver>` | 指定上一版（补丁基线 / SoftUpdate 用） |
| `-Force` | 清单 `force=true`（无论版本都强制更新） |
| `-SkipWindows` / `-SkipAndroid` / `-SkipGit` / `-SkipDownloadVerify` | 跳过对应环节 |
| `-NonInteractive` | 关闭交互向导 |

**版本号格式**：`1.1.yyyyMMdd`（如 `1.1.20261010`）。脚本自动同步：
`NewCosmos.csproj`(ApplicationDisplayVersion/ApplicationVersion) / `config\app.ini`(Version) / `installer\NewCosmosSetup.iss`(MyAppVersion/VersionInfoVersion)。

---

## 三、典型场景

| 场景 | 操作 |
|---|---|
| 日常发新版（如 10 号） | `publish_all.bat` → 版本号填 `1.1.20261010` → 其余回车 |
| 带增量补丁发布 | 向导"生成增量补丁"选 `Y`（需能取到上一版包） |
| 只提示不强更 | 向导"非强制升级"选 `Y`（或在命令行 `-SoftUpdate`） |
| 只想下载最新包 | `download_update.bat -Platform android`（或 `windows`） |

---

## 四、依赖与前置

- **构建**：.NET 10 SDK + `maui-windows`/`maui-android`；Inno Setup 6；JDK 17（`C:\AndroidJdk\jdk-17.0.2`）+ Android SDK（`%LOCALAPPDATA%\Android\Sdk`）。
- **签名**：私钥 `%USERPROFILE%\.newcosmos\update_signing_key.pem`（`UpdateSigningTool keygen` 生成；公钥已编译进 App 的 `Constants\UpdateSignatureConstants.cs`）。
- **上传**：SSH 部署密钥 `%USERPROFILE%\.ssh\id_ed25519_newcosmos`；服务器地址来自 `deploy.local.ps1`。
- **入库/查上一版**：环境变量 `NEWCOSMOS_DB_PASSWORD`（未设则跳过发布历史入库；上一版改从本机 `publish\release\` 推断）。
- **Android 发布签名**：`keystore\keystore.props`（gitignored）。

### 已知环境要求
- **Android AOT 需 ASCII 临时目录**：Windows 用户名含中文时 `%TEMP%` 非 ASCII，会让 `mono-aot-cross` 读不到 `temp.rsp` 报 `The specified response file can not be read`。脚本在 Android 发布期间强制 `TMP/TEMP=<AsciiTempDir>`（默认 `C:\bt`）。
- **Android AOT 依赖 SkiaSharp**：`NewCosmos.csproj` 已对 Android 显式引用 `SkiaSharp`，否则 AOT 编译 `NPOI.OOXML` 报 `Could not load ... 'SkiaSharp'`。

---

## 五、产物位置（均在 `publish\`，gitignored）

- Windows 安装包：`publish\NewCosmosSetup_<版本>.exe`
- Android APK（AOT）：`publish\NewCosmosSetup_<版本>.apk`
- 发布目录：`publish\release\<版本>\`（含 `update.json`/`update.sig`/`canonical.txt`；Android 在 `android\` 子目录）
- 增量补丁：`publish\release\<版本>\NewCosmosPatch_<上一版>_<版本>.bin`
- 下载缓存：`downloads\<版本>\`（`download_update` 或回环校验时）
