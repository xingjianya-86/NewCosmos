# ============================================================================
# publish_all.ps1 - 一键双端发布（一条龙）
#   Windows 安装包 + Android(AOT) APK → 签名 → 上传 → 回环下载校验 → GitHub
#
# 用法：
#   .\deploy\publish_all.ps1                      # 版本取 csproj；默认强制升级、无补丁
#   .\deploy\publish_all.ps1 -Version 1.1.20261010
#   .\deploy\publish_all.ps1 -Version 1.1.20261010 -WithPatch      # 生成上一版→本版增量补丁
#   .\deploy\publish_all.ps1 -Version 1.1.20261010 -SoftUpdate     # 非强制升级（minSupported=上一版）
#   .\deploy\publish_all.ps1 -MinSupported 1.1.20261004            # 显式指定最低支持版本
#
# 开关：
#   -WithPatch          生成增量补丁（Windows exe + Android apk，需能取到上一版包），清单 schema=2
#   -SoftUpdate         非强制升级：minSupported 自动取“上一版”（低于它才强制）
#   -MinSupported <ver> 显式最低支持版本（优先级高于 -SoftUpdate）
#   -PrevVersion <ver>  指定上一版（补丁基线/SoftUpdate 用；缺省从 DB 或本机 release 目录推断）
#   -Force              清单 force=true（无论版本都强制更新）
#   -SkipWindows / -SkipAndroid / -SkipGit / -SkipDownloadVerify
#   -SkipTemplateCheck   跳过模板字段体检
#   -SkipTests           跳过发布前 dotnet test 门槛（默认执行，~1-2 分钟）
#   -SkipStandardsCheck  跳过 check_standards 规范检查（默认执行，红线类命中即中断）
#   -Notes <text>        本次更新说明（升级弹窗与「更新日志」页显示；缺省自动抽取 CHANGELOG.md 当前版本段）
#   -NotesFile <path>    从 UTF-8 文件读取更新说明（多行文本）
#
# 依赖：.NET 10 SDK + maui；Inno Setup 6；JDK17 + Android SDK；keystore\keystore.props。
# 内网地址来自 deploy\deploy.local.ps1（gitignored）；上一版查询/入库需环境变量 NEWCOSMOS_DB_PASSWORD。
# ============================================================================
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$MinSupported = "",
    [switch]$SoftUpdate,
    [switch]$WithPatch,
    [string]$PrevVersion = "",
    [switch]$Force,
    [string]$Channel = "stable",
    [string]$Notes = "",
    [string]$NotesFile = "",
    [switch]$SkipWindows,
    [switch]$SkipAndroid,
    [switch]$SkipGit,
    [switch]$SkipDownloadVerify,
    [switch]$SkipTemplateCheck,
    [switch]$SkipTests,
    [switch]$SkipStandardsCheck,
    [switch]$NonInteractive,
    [switch]$DryRun,
    [string]$JavaSdkDirectory = "C:\AndroidJdk\jdk-17.0.2",
    [string]$AndroidSdkDirectory = "$env:LOCALAPPDATA\Android\Sdk",
    [string]$AsciiTempDir = "C:\bt",
    [string]$SigningKey = "$env:USERPROFILE\.newcosmos\update_signing_key.pem",
    [string]$SshKey = "$env:USERPROFILE\.ssh\id_ed25519_newcosmos"
)

$ErrorActionPreference = "Stop"
$ScriptsDir = $PSScriptRoot
$Repo = Split-Path -Parent $ScriptsDir
Set-Location $Repo

function Write-Step([string]$t) { Write-Host ""; Write-Host "== $t ==" -ForegroundColor Cyan }

# ── CHANGELOG.md 抽取当前版本段（升级日志唯一素材来源）──
# 返回 @{ Text; Source }：exact=当前版本段 / unreleased=「未发布」段回退 / missing=两段都没有或文件不可读。
# 显式 UTF-8 读取（PS5.1 Get-Content 默认 ANSI，中文会乱码）。
function Get-ChangelogSection([string]$Ver) {
    $cl = Join-Path $Repo "CHANGELOG.md"
    if (-not (Test-Path -LiteralPath $cl)) { return @{ Text = ""; Source = "missing" } }
    try { $text = [IO.File]::ReadAllText($cl, [Text.Encoding]::UTF8) } catch { return @{ Text = ""; Source = "missing" } }

    $m = [regex]::Match($text, "(?ms)" + [regex]::Escape("## [$Ver]") + "[^\r\n]*\r?\n.*?(?=^## \[|\z)")
    if ($m.Success) { return @{ Text = $m.Value.Trim(); Source = "exact" } }

    $m = [regex]::Match($text, "(?ms)" + [regex]::Escape("## [未发布]") + "[^\r\n]*\r?\n.*?(?=^## \[|\z)")
    if ($m.Success) { return @{ Text = $m.Value.Trim(); Source = "unreleased" } }

    return @{ Text = ""; Source = "missing" }
}

# ── 发布环境体检（DryRun 与正式发布共用；只报告不构建）──
# 2026-11 新增：DryRun 原只打印计划，缺依赖（Inno/JDK/SDK/SSH/签名键/deploy.local）要到真跑
# 才 throw——DryRun 最有价值的动作恰是提前暴露 80% 失败原因。
function Test-PublishEnvironment {
    $issues = @()
    function _chk([string]$name, [bool]$ok, [string]$hint) {
        if ($ok) { Write-Host "  [OK] $name" -ForegroundColor DarkGray }
        else { $issues += "$name —— $hint"; Write-Host "  [缺失] $name（$hint）" -ForegroundColor Yellow }
    }
    if (-not $SkipWindows) {
        # 与 publish_release.ps1 的 ISCC 探测保持一致（4 路候选）：Inno Setup 可按用户装到
        # %LOCALAPPDATA%\Programs，只查 Program Files (x86) 会误报缺失（2026-11-02 实测）。
        $isccCandidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Inno Setup 6\ISCC.exe"
        )
        $isccOk = @($isccCandidates | Where-Object { Test-Path -LiteralPath $_ }).Count -gt 0
        _chk "Inno Setup 6 (ISCC.exe)" $isccOk "安装 Inno Setup 6 或改用 -SkipWindows（已搜索 Program Files(x86)/Program Files/LOCALAPPDATA）"
    }
    if (-not $SkipAndroid) {
        _chk "JDK17 ($JavaSdkDirectory)" (Test-Path -LiteralPath $JavaSdkDirectory) "安装 JDK 或改用 -SkipAndroid"
        _chk "Android SDK ($AndroidSdkDirectory)" (Test-Path -LiteralPath $AndroidSdkDirectory) "安装 Android SDK 或改用 -SkipAndroid"
        _chk "keystore\keystore.props" (Test-Path -LiteralPath (Join-Path $Repo "keystore\keystore.props")) "配置 APK 签名或改用 -SkipAndroid"
    }
    if (-not $SkipGit) {
        _chk "SSH 私钥 ($SshKey)" (Test-Path -LiteralPath $SshKey) "配置 git 推送密钥或改用 -SkipGit"
    }
    _chk "更新签名私钥 ($SigningKey)" (Test-Path -LiteralPath $SigningKey) "UpdateSigningTool 生成或改用 -SkipDownloadVerify"
    _chk "deploy\deploy.local.ps1（服务器参数）" (Test-Path -LiteralPath $deployLocal) "复制 deploy.sample.ps1 填内网参数"
    if ([string]::IsNullOrEmpty($env:NEWCOSMOS_DB_PASSWORD)) {
        Write-Host "  [提示] NEWCOSMOS_DB_PASSWORD 未设置：补丁基线/上一版查询走本地目录回退，发布历史不入库" -ForegroundColor DarkGray
    }
    return $issues
}

# ── 本地部署配置（gitignored）──
$deployLocal = Join-Path $ScriptsDir "deploy.local.ps1"
if (Test-Path -LiteralPath $deployLocal) { . $deployLocal }
if (-not $ServerHost)      { $ServerHost = $LocalServerHost }
if (-not $ServerRoot)      { $ServerRoot = $LocalServerRoot }
if (-not $VerifyUrlStable) { $VerifyUrlStable = $LocalVerifyUrl }
if (-not $ServerUser)      { $ServerUser = "root" }
$dbHost = if ($LocalDbHost) { $LocalDbHost } else { $ServerHost }
$dbName = if ($LocalDbName) { $LocalDbName } else { "new_cosmos" }
$dbUser = if ($LocalDbUser) { $LocalDbUser } else { "new_cosmos" }
$psql   = if ($LocalPsqlPath) { $LocalPsqlPath } else { "C:\pgsql\bin\psql.exe" }

# ── 版本号 ──
$csprojPath = Join-Path $Repo "NewCosmos.csproj"
if ($Version -eq "") {
    $m = [regex]::Match([IO.File]::ReadAllText($csprojPath), '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>')
    if (-not $m.Success) { throw "无法从 csproj 读取版本号，请用 -Version 指定" }
    $Version = $m.Groups[1].Value.Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d{8}$') { throw "版本号格式应为 主.次.yyyyMMdd，如 1.1.20261010（当前: $Version）" }

# ── 交互向导（运行后按 Y/F 选择；已显式传入的参数不再询问；自动化用 -NonInteractive 关闭）──
# DryRun 跳过向导（只体检与打印计划，不构建；原实现 DryRun 仍被 5 个 Read-Host 卡住）。
if (-not $NonInteractive -and -not $DryRun) {
    Write-Host ""
    Write-Host "===== 发布向导（回车=默认；Y=是，F/N=否）=====" -ForegroundColor Cyan
    if (-not $PSBoundParameters.ContainsKey('Version')) {
        $v = Read-Host "版本号 [默认 $Version]"
        if (-not [string]::IsNullOrWhiteSpace($v)) { $Version = $v.Trim() }
    }
    # 升级日志素材：默认抽取 CHANGELOG.md 当前版本段（缺段回退「未发布」段），可手动输入或留空
    if (-not $PSBoundParameters.ContainsKey('Notes') -and -not $PSBoundParameters.ContainsKey('NotesFile')) {
        $clSec = Get-ChangelogSection $Version
        if ($clSec.Text) {
            $srcLabel = if ($clSec.Source -eq 'exact') { "CHANGELOG.md [$Version] 段" } else { "CHANGELOG.md [未发布] 段（建议发版前把标题改成版本号）" }
            Write-Host ""
            Write-Host "本次更新说明（升级弹窗显示 · 来源：$srcLabel）：" -ForegroundColor Cyan
            Write-Host $clSec.Text -ForegroundColor DarkGray
            $a = Read-Host "采用? Y=采用 / N=手动输入 / E=留空 [回车=Y]"
            if ($a -match '^(n|N)$') { $Notes = Read-Host "更新说明（单行；多行请用 -NotesFile）" }
            elseif ($a -match '^(e|E)$') { $Notes = "" }
            else { $Notes = $clSec.Text }
        } else {
            Write-Host "警告：CHANGELOG.md 缺少 [$Version] 与 [未发布] 段落，升级弹窗将没有更新说明（建议发版前补写）" -ForegroundColor Yellow
            $a = Read-Host "仍要手动输入说明? Y=输入 / N=留空 [回车=N]"
            if ($a -match '^(y|Y)$') { $Notes = Read-Host "更新说明（单行）" }
        }
    }
    if (-not $PSBoundParameters.ContainsKey('WithPatch')) {
        $a = Read-Host "生成增量补丁（上一版→本版）? Y/F [回车=F]"
        if ($a -match '^(y|Y)$') { $WithPatch = $true }
    }
    if (-not $PSBoundParameters.ContainsKey('SoftUpdate')) {
        $a = Read-Host "非强制升级（最低支持=上一版）? Y/F [回车=F]"
        if ($a -match '^(y|Y)$') { $SoftUpdate = $true }
    }
    if (-not $PSBoundParameters.ContainsKey('SkipGit')) {
        $a = Read-Host "发布后推送 GitHub? Y/F [回车=Y]"
        if ($a -match '^(f|F|n|N)$') { $SkipGit = $true }
    }
    if (-not $PSBoundParameters.ContainsKey('SkipDownloadVerify')) {
        $a = Read-Host "回环下载校验（会下载两端安装包）? Y/F [回车=Y]"
        if ($a -match '^(f|F|n|N)$') { $SkipDownloadVerify = $true }
    }
}

# ── 上一版（补丁基线 / SoftUpdate）解析 ──
function Resolve-PrevVersion([string]$Current) {
    if ($PrevVersion) { return $PrevVersion }
    if ($env:NEWCOSMOS_DB_PASSWORD -and (Test-Path -LiteralPath $psql)) {
        $sqlFile = Join-Path $env:TEMP "prev_ver_$Current.sql"
        [IO.File]::WriteAllText($sqlFile, "SELECT version FROM nc_sys_app_versions WHERE channel='$Channel' AND version<>'$Current' ORDER BY id DESC LIMIT 1;", (New-Object System.Text.UTF8Encoding($false)))
        $o = $env:PGPASSWORD; $e = $env:PGCLIENTENCODING
        $env:PGPASSWORD = $env:NEWCOSMOS_DB_PASSWORD; $env:PGCLIENTENCODING = "UTF8"
        try { $r = & $psql -h $dbHost -p 5432 -U $dbUser -d $dbName -t -A -w -f $sqlFile 2>$null } finally {
            if ($null -eq $o) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue } else { $env:PGPASSWORD = $o }
            if ($null -eq $e) { Remove-Item Env:\PGCLIENTENCODING -ErrorAction SilentlyContinue } else { $env:PGCLIENTENCODING = $e }
        }
        if ($r) { return $r.Trim() }
    }
    $rel = Join-Path $Repo "publish\release"
    if (Test-Path -LiteralPath $rel) {
        $cands = Get-ChildItem $rel -Directory | Where-Object { $_.Name -ne $Current } |
            Sort-Object { try { [version]$_.Name } catch { [version]"0.0" } } -Descending
        foreach ($d in $cands) {
            if ((Test-Path (Join-Path $d.FullName "NewCosmosSetup_$($d.Name).exe")) -or (Test-Path (Join-Path $d.FullName "NewCosmosSetup_$($d.Name).apk"))) { return $d.Name }
        }
    }
    return ""
}

$prevResolved = ""
if ($SoftUpdate -or $WithPatch) { $prevResolved = Resolve-PrevVersion $Version }

if (-not $MinSupported) {
    if ($SoftUpdate -and $prevResolved) { $MinSupported = $prevResolved }
    else { $MinSupported = $Version }
}
if ($SoftUpdate -and -not $prevResolved) { Write-Host "警告：未能解析上一版，-SoftUpdate 退化为强制（minSupported=版本号）" -ForegroundColor Yellow }
Write-Host "发布版本: $Version（最低支持 $MinSupported，渠道 $Channel，强制=$([bool]$Force)，增量补丁=$([bool]$WithPatch)）"

# ── 升级日志素材落地：-NotesFile/-Notes → notes 文本 → 临时 UTF-8 文件 ──
# 供 Windows 分支传 publish_release.ps1 -NotesFile、Android 分支写清单 notes、changelog.json 合并共用。
if ($NotesFile -ne "" -and (Test-Path -LiteralPath $NotesFile)) {
    # 显式按 UTF-8 读取（PS5.1 Get-Content 默认 ANSI，中文会乱码）
    $Notes = [IO.File]::ReadAllText($NotesFile, [Text.Encoding]::UTF8).Trim()
}
$notesText = ""
if ($Notes) { $notesText = $Notes.Trim() }
$notesPath = ""
if ($notesText -ne "") {
    New-Item -ItemType Directory -Force -Path (Join-Path $Repo "publish") | Out-Null
    $notesPath = Join-Path $Repo "publish\notes_$Version.md"
    [IO.File]::WriteAllText($notesPath, $notesText, (New-Object System.Text.UTF8Encoding($false)))
}

if ($DryRun) {
    Write-Host ""
    Write-Host "===== DRY RUN（不构建/不上传/不推 GitHub）=====" -ForegroundColor Yellow
    # 环境体检：提前暴露缺依赖（Inno/JDK/SDK/SSH/签名键/deploy.local），不再等真跑才 throw
    Write-Step "发布环境体检"
    $envIssues = Test-PublishEnvironment
    $winFlags = @()
    if (-not $WithPatch) { $winFlags += '-SkipPatch' }
    if ($Force) { $winFlags += '-Force' }
    Write-Host ("  Windows : publish_release.ps1 -Version {0} -MinSupported {1} -Channel {2} {3}" -f $Version, $MinSupported, $Channel, ($winFlags -join ' '))
    Write-Host ("  Android : dotnet publish -f net10.0-android36.0 -c Release -r android-arm64 -p:RunAOTCompilation=true" + $(if ($WithPatch) { "（并生成增量补丁）" } else { "" }))
    Write-Host ("  上一版  : {0}" -f $(if ($prevResolved) { $prevResolved } else { "（未解析到）" }))
    Write-Host ("  回环校验: {0}" -f $(if ($SkipDownloadVerify) { "跳过" } else { "执行（下载两端安装包）" }))
    Write-Host ("  模板体检: {0}" -f $(if ($SkipTemplateCheck) { "跳过" } else { "执行（check_template_fields -FailOn NonAscii,MultiSource -MaxUnknown 0，命中即中断）" }))
    Write-Host ("  测试门槛: {0}" -f $(if ($SkipTests) { "跳过" } else { "执行（dotnet test，全过才发版）" }))
    Write-Host ("  规范检查: {0}" -f $(if ($SkipStandardsCheck) { "跳过" } else { "执行（check_standards 红线类命中即中断）" }))
    Write-Host ("  GitHub  : {0}" -f $(if ($SkipGit) { "跳过" } else { "提交并推送" }))
    Write-Host ("  产物目录: {0}" -f (Join-Path $Repo "publish\release\$Version"))
    Write-Host ("  版本同步: {0}（csproj/app.ini/iss；DryRun 不写入）" -f $Version)
    if ($notesText -ne "") {
        Write-Host ("  更新说明: 已就绪（{0} 字）—— Windows 走 -NotesFile，Android 写清单 notes" -f $notesText.Length) -ForegroundColor Cyan
    } else {
        Write-Host "  更新说明: 空（升级弹窗将无更新说明；建议在 CHANGELOG.md 写当前版本段）" -ForegroundColor Yellow
    }
    if ($envIssues.Count -gt 0) {
        Write-Host ""
        Write-Host "体检发现 $($envIssues.Count) 个缺失依赖（正式发版将中断）：" -ForegroundColor Red
        $envIssues | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host ""
    Write-Host "体检通过：无缺失依赖。" -ForegroundColor Green
    return
}

# ── 0-. 发布环境体检（缺依赖早失败，不动版本文件）──
Write-Step "发布环境体检"
$envIssues = Test-PublishEnvironment
if ($envIssues.Count -gt 0) {
    $envIssues | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "发布环境体检失败（$($envIssues.Count) 项缺失依赖），见上方清单"
}

# ── 0-.2 规范检查（check_standards：AGENTS §5/§7/§8/§10/§12 防回潮，命中红线类即中断）──
# 退出码语义：脚本现恒 0（报告式），故按报告行数判红线（SEC/UI1/SQL1/SQL2/SQL4）。
if (-not $SkipStandardsCheck) {
    Write-Step "规范检查（check_standards.ps1）"
    $std = Join-Path $ScriptsDir "check_standards.ps1"
    if (Test-Path -LiteralPath $std) {
        & $std -Category ALL | Out-Host
        $report = Join-Path $ScriptsDir "standards_report.txt"
        if (Test-Path -LiteralPath $report) {
            $redlines = @(Get-Content -LiteralPath $report -Encoding UTF8 | Where-Object { $_ -match '^\[(SEC|UI1|SQL1|SQL2|SQL4)\]' })
            if ($redlines.Count -gt 0) {
                $redlines | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
                throw "规范检查命中红线 $($redlines.Count) 处（SEC/UI1/SQL1/SQL2/SQL4），勿强行发版；见 deploy\standards_report.txt"
            }
            Write-Host "  红线 0 处" -ForegroundColor Green
        }
    }
    else { Write-Host "  未找到 check_standards.ps1，跳过" -ForegroundColor Yellow }
}
else { Write-Host "跳过规范检查（-SkipStandardsCheck）" -ForegroundColor Yellow }

# ── 0-.3 测试门槛（AGENTS §0：测试全过是发版前提，原仅靠人工记忆 → 机器门槛）──
if (-not $SkipTests) {
    Write-Step "测试门槛（dotnet test）"
    & dotnet test (Join-Path $Repo "NewCosmos.Tests\NewCosmos.Tests.csproj") --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "测试未通过（退出码 $LASTEXITCODE）：勿强行发版" }
}
else { Write-Host "跳过测试门槛（-SkipTests）" -ForegroundColor Yellow }

# ── 0-. 模板字段体检（批6 长效机制，2026-10-16）──
# 在版本号同步之前执行（早失败，不动 csproj/app.ini/iss 三处版本文件）。命中即 throw 中断发版。
# $TemplateUnknownBaseline = 既存孤儿行数（docs\20261016_模板字段体检报告.md 的 Unknown），
# 只防新增不清存量；孤儿清理后同步下调该基线直至 0。
# NonInteractive 且无 NEWCOSMOS_DB_PASSWORD 时脚本无法交互取密 → 显式警告跳过，避免卡死无人值守发版。
$TemplateUnknownBaseline = 0
if (-not $SkipTemplateCheck) {
    if ([string]::IsNullOrEmpty($env:NEWCOSMOS_DB_PASSWORD) -and $NonInteractive) {
        Write-Host "警告：NonInteractive 且未设置 NEWCOSMOS_DB_PASSWORD，跳过模板字段体检" -ForegroundColor Yellow
    }
    else {
        Write-Step "模板字段体检（check_template_fields.ps1）"
        $chk = Join-Path $Repo "Scripts\check_template_fields.ps1"
        & $chk -FailOn NonAscii,MultiSource -MaxUnknown $TemplateUnknownBaseline
        if ($LASTEXITCODE -ne 0) { throw "模板字段体检未通过（退出码 $LASTEXITCODE）：见上方 findings，勿强行发版" }
    }
}
else { Write-Host "跳过模板字段体检（-SkipTemplateCheck）" -ForegroundColor Yellow }

# ── 0. 版本号三处同步 ──
# 必须在平台分支之前执行：原先同步只在 publish_release.ps1（Windows 分支）里做，
# -SkipWindows 时不会同步，Android 清单沿用 csproj 旧版本号 → 手机端判定"已是最新"而不更新。
Write-Step "同步版本号（csproj / app.ini / iss）"
$numeric = ($Version -split '\.')[2]
$csproj = [IO.File]::ReadAllText($csprojPath)
$csproj = [regex]::Replace($csproj, '<ApplicationDisplayVersion>[^<]*</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$Version</ApplicationDisplayVersion>")
$csproj = [regex]::Replace($csproj, '<ApplicationVersion>[^<]*</ApplicationVersion>', "<ApplicationVersion>$numeric</ApplicationVersion>")
[IO.File]::WriteAllText($csprojPath, $csproj, (New-Object System.Text.UTF8Encoding($false)))

$appIniPath = Join-Path $Repo "config\app.ini"
$appIni = [IO.File]::ReadAllText($appIniPath)
$appIni = [regex]::Replace($appIni, '(?m)^Version=.*$', "Version=$Version")
[IO.File]::WriteAllText($appIniPath, $appIni, (New-Object System.Text.UTF8Encoding($false)))

$issPath = Join-Path $Repo "installer\NewCosmosSetup.iss"
$iss = [IO.File]::ReadAllText($issPath)
$iss = [regex]::Replace($iss, '#define MyAppVersion "[^"]*"', "#define MyAppVersion ""$Version""")
$yearV = [int]$numeric.Substring(0, 4)
$mdV   = [int]$numeric.Substring(4, 4)
# 主次版本从 $Version 派生（勿硬编码：历史 1.1 硬编码在版本升到 1.2 后导致文件属性版本主次不一致）
$majorMinor = ($Version -split '\.')[0..1] -join '.'
$iss = [regex]::Replace($iss, 'VersionInfoVersion=[0-9.]+', "VersionInfoVersion=$majorMinor.$yearV.$mdV")
[IO.File]::WriteAllText($issPath, $iss, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "已同步：$Version / $numeric / VersionInfo=$majorMinor.$yearV.$mdV"

$releaseDir = Join-Path $Repo "publish\release\$Version"
$apkName    = "NewCosmosSetup_$Version.apk"
$apkPath    = Join-Path $Repo "publish\$apkName"
$androidDir = Join-Path $releaseDir "android"
$signTool   = Join-Path $ScriptsDir "UpdateSigningTool\bin\Release\net10.0\UpdateSigningTool.dll"
$patchTool  = Join-Path $ScriptsDir "PatchTool\bin\Release\net10.0\PatchTool.dll"
$remote     = "$ServerUser@$ServerHost"

function Ensure-Tool([string]$dll, [string]$proj) {
    if (-not (Test-Path -LiteralPath $dll)) {
        & dotnet build (Join-Path $ScriptsDir $proj) -c Release -v q -nologo
        if ($LASTEXITCODE -ne 0) { throw "$proj 构建失败" }
    }
}

# ── 1. Windows ──
if (-not $SkipWindows) {
    Write-Step "Windows：publish_release.ps1"
    $pr = Join-Path $ScriptsDir "publish_release.ps1"
    $prArgs = @{ Version = $Version; MinSupported = $MinSupported; Channel = $Channel }
    if (-not $WithPatch) { $prArgs['SkipPatch'] = $true }
    if ($Force) { $prArgs['Force'] = $true }
    # 升级日志：把本次更新说明传给底层（写入 Windows 清单 notes）
    if ($notesPath -ne "") { $prArgs['NotesFile'] = $notesPath }
    & $pr @prArgs
    if ($LASTEXITCODE -ne 0) { throw "Windows 发布失败（退出码 $LASTEXITCODE）" }
}
else { Write-Host "跳过 Windows" -ForegroundColor Yellow }

# ── 2. Android（AOT）──
if (-not $SkipAndroid) {
    if (-not $ServerHost) { throw "未配置 ServerHost：请设置 deploy\deploy.local.ps1 的 `$LocalServerHost" }
    if (-not (Test-Path -LiteralPath $JavaSdkDirectory)) { throw "未找到 JDK: $JavaSdkDirectory" }
    if (-not (Test-Path -LiteralPath $AndroidSdkDirectory)) { throw "未找到 Android SDK: $AndroidSdkDirectory" }

    Write-Step "Android：dotnet publish（Release / android-arm64 / AOT）"
    $oldJava = $env:JAVA_HOME; $oldAndroid = $env:ANDROID_HOME; $oldTmp = $env:TMP; $oldTemp = $env:TEMP
    $env:JAVA_HOME = $JavaSdkDirectory
    $env:ANDROID_HOME = $AndroidSdkDirectory
    New-Item -ItemType Directory -Force -Path $AsciiTempDir | Out-Null
    $env:TMP = $AsciiTempDir; $env:TEMP = $AsciiTempDir
    try {
        & dotnet publish "NewCosmos.csproj" -f net10.0-android36.0 -c Release -r android-arm64 `
            -p:JavaSdkDirectory=$JavaSdkDirectory -p:RunAOTCompilation=true
        if ($LASTEXITCODE -ne 0) { throw "Android dotnet publish 失败" }
    }
    finally {
        if ($null -eq $oldJava) { Remove-Item Env:\JAVA_HOME -ErrorAction SilentlyContinue } else { $env:JAVA_HOME = $oldJava }
        if ($null -eq $oldAndroid) { Remove-Item Env:\ANDROID_HOME -ErrorAction SilentlyContinue } else { $env:ANDROID_HOME = $oldAndroid }
        if ($null -eq $oldTmp) { Remove-Item Env:\TMP -ErrorAction SilentlyContinue } else { $env:TMP = $oldTmp }
        if ($null -eq $oldTemp) { Remove-Item Env:\TEMP -ErrorAction SilentlyContinue } else { $env:TEMP = $oldTemp }
    }

    $apkPubDir = Join-Path $Repo "bin\Release\net10.0-android36.0\android-arm64\publish"
    $builtApk = Get-ChildItem -LiteralPath $apkPubDir -Filter "*-Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $builtApk) { $builtApk = Get-ChildItem -LiteralPath $apkPubDir -Filter "*.apk" -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if (-not $builtApk) { throw "未找到 Android APK（$apkPubDir）" }
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    Copy-Item -LiteralPath $builtApk.FullName -Destination $apkPath -Force

    $size = (Get-Item -LiteralPath $apkPath).Length
    $sha  = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()

    # Android 增量补丁（可选）
    $apkPatch = $null
    if ($WithPatch) {
        Write-Step "Android：生成增量补丁"
        if (-not $prevResolved) { Write-Host "  未解析到上一版，跳过 Android 补丁" -ForegroundColor Yellow }
        else {
            $baseApk = Join-Path $Repo "publish\release\$prevResolved\NewCosmosSetup_$prevResolved.apk"
            if (-not (Test-Path -LiteralPath $baseApk) -and (Test-Path -LiteralPath $SshKey)) {
                Write-Host "  下载上一版 APK 作为基线: $prevResolved"
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $baseApk) | Out-Null
                & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new "${remote}:$ServerRoot/releases/$prevResolved/NewCosmosSetup_$prevResolved.apk" $baseApk 2>$null
            }
            if (Test-Path -LiteralPath $baseApk) {
                Ensure-Tool $patchTool "PatchTool\PatchTool.csproj"
                $patchName = "NewCosmosPatch_${prevResolved}_${Version}.bin"
                $patchPath = Join-Path $releaseDir $patchName
                $sw = [Diagnostics.Stopwatch]::StartNew()
                & dotnet $patchTool create --old $baseApk --new $apkPath --out $patchPath
                $sw.Stop()
                if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $patchPath)) {
                    $apkPatch = [ordered]@{ url = $patchName; size = (Get-Item $patchPath).Length; sha256 = (Get-FileHash $patchPath -Algorithm SHA256).Hash.ToLowerInvariant(); baseVersion = $prevResolved }
                    Write-Host "  Android 补丁: $patchName ($([Math]::Round($apkPatch.size/1MB,1)) MB, from $prevResolved, $([Math]::Round($sw.Elapsed.TotalSeconds,0))s)" -ForegroundColor Green
                } else { Write-Host "  Android 补丁生成失败，清单不含 patch" -ForegroundColor Yellow }
            } else { Write-Host "  无法获取上一版 APK，跳过 Android 补丁" -ForegroundColor Yellow }
        }
    }

    Write-Step "Android：签名清单"
    $publishedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    $forceStr = if ($Force) { "true" } else { "false" }
    $schema = if ($apkPatch) { 2 } else { 1 }
    $canonical = "$schema|$Channel|$Version|$MinSupported|$forceStr|$publishedAt|$apkName|$size|$sha"
    if ($apkPatch) { $canonical += "|$($apkPatch.sha256)|$($apkPatch.baseVersion)" }
    New-Item -ItemType Directory -Force -Path $androidDir | Out-Null
    $canonFile = Join-Path $androidDir "canonical.txt"
    [IO.File]::WriteAllText($canonFile, $canonical, (New-Object System.Text.UTF8Encoding($false)))
    if (-not (Test-Path -LiteralPath $SigningKey)) { throw "未找到签名私钥: $SigningKey" }
    Ensure-Tool $signTool "UpdateSigningTool\UpdateSigningTool.csproj"
    $sigFile = Join-Path $androidDir "update.sig"
    & dotnet $signTool sign --key $SigningKey --canonical $canonFile --out $sigFile
    if ($LASTEXITCODE -ne 0) { throw "Android 清单签名失败" }
    $signature = (Get-Content -LiteralPath $sigFile -Raw -Encoding UTF8).Trim()
    $manifest = [ordered]@{
        schema = $schema; channel = $Channel; latestVersion = $Version; minSupportedVersion = $MinSupported
        force = [bool]$Force; publishedAt = $publishedAt; notes = $notesText
        package = [ordered]@{ url = $apkName; size = $size; sha256 = $sha }
        signature = $signature
    }
    if ($apkPatch) { $manifest.patch = [ordered]@{ url = $apkPatch.url; size = $apkPatch.size; sha256 = $apkPatch.sha256; baseVersion = $apkPatch.baseVersion } }
    $manifestPath = Join-Path $androidDir "update.json"
    [IO.File]::WriteAllText($manifestPath, (ConvertTo-Json $manifest -Depth 5), (New-Object System.Text.UTF8Encoding($false)))

    Write-Step "Android：上传到 $ServerHost$ServerRoot/android"
    if (-not (Test-Path -LiteralPath $SshKey)) { throw "未找到 SSH 密钥: $SshKey" }
    & ssh -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $remote "mkdir -p $ServerRoot/android $ServerRoot/releases/$Version"
    if ($LASTEXITCODE -ne 0) { throw "SSH 连接失败" }
    $up = @($apkPath, $manifestPath)
    if ($apkPatch) { $up += (Join-Path $releaseDir $apkPatch.url) }
    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new @up "${remote}:$ServerRoot/android/"
    if ($LASTEXITCODE -ne 0) { throw "上传 Android 失败" }
    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $apkPath "${remote}:$ServerRoot/releases/$Version/" 2>$null
    if ($apkPatch) { & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new (Join-Path $releaseDir $apkPatch.url) "${remote}:$ServerRoot/releases/$Version/" 2>$null }

    Write-Step "Android：线上校验"
    $verifyUrl = if ($VerifyUrlStable) { $VerifyUrlStable -replace '/stable/', '/android/' } else { "" }
    if ($verifyUrl) {
        Start-Sleep -Seconds 2
        try {
            $j = (Invoke-WebRequest -Uri $verifyUrl -UseBasicParsing -TimeoutSec 25).Content | ConvertFrom-Json
            if ($j.package.sha256 -eq $sha) { Write-Host "Android 线上清单 OK: schema=$($j.schema) sha=$($j.package.sha256)" -ForegroundColor Green }
            else { Write-Host "Android 线上 sha 不一致: $($j.package.sha256)" -ForegroundColor Yellow }
        } catch { Write-Host "Android 线上校验失败（稍后手动确认）: $($_.Exception.Message)" -ForegroundColor Yellow }
    }
    Write-Host "Android APK: $apkPath ($([Math]::Round($size/1MB,1)) MB)" -ForegroundColor Green
}
else { Write-Host "跳过 Android" -ForegroundColor Yellow }

# ── 2.5 升级日志全量（changelog.json）：与服务端历史合并后上传 ──
# 应用内「更新日志」页拉取全量历史；仅作展示，不参与安装决策（安装仍走签名清单 update.json）。
if ((-not $SkipWindows) -or (-not $SkipAndroid)) {
    Write-Step "升级日志：合并并上传 changelog.json"
    try {
        $clRemote = "$ServerUser@$ServerHost"
        $clPublishedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")

        $entries = @()
        try {
            # 清单在 stable/（Windows）与 android/ 两处各存一份全量历史，客户端按各自清单目录读取
            $oldJson = (& ssh -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $clRemote "cat $ServerRoot/stable/changelog.json 2>/dev/null" 2>$null) -join "`n"
            if ($oldJson -and $oldJson.Trim()) {
                $oldObj = $oldJson | ConvertFrom-Json
                if ($oldObj.entries) { $entries = @($oldObj.entries) }
            }
        } catch {
            Write-Host "  服务端 changelog.json 拉取/解析失败（按全新重建，仅保留本次）: $($_.Exception.Message)" -ForegroundColor Yellow
        }

        # 同版本覆盖：重发/补发不产生重复条目
        $entries = @($entries | Where-Object { $_.version -ne $Version })
        $newList = @([ordered]@{
            version     = $Version
            channel     = $Channel
            publishedAt = $clPublishedAt
            force       = [bool]$Force
            notes       = $notesText
        }) + $entries

        $clDoc = [ordered]@{ schema = 1; updatedAt = $clPublishedAt; entries = $newList }
        $clPath = Join-Path $Repo "publish\changelog.json"
        [IO.File]::WriteAllText($clPath, (ConvertTo-Json $clDoc -Depth 6), (New-Object System.Text.UTF8Encoding($false)))

        & ssh -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $clRemote "mkdir -p $ServerRoot/stable $ServerRoot/android" | Out-Null
        foreach ($dest in @("stable", "android")) {
            & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $clPath "${clRemote}:$ServerRoot/$dest/changelog.json"
            if ($LASTEXITCODE -ne 0) { throw "changelog.json 上传 $dest/ 失败（退出码 $LASTEXITCODE）" }
        }
        Write-Host "  changelog.json 已上传（stable/ + android/ 各一份）：共 $($newList.Count) 个版本，本版说明 $($notesText.Length) 字" -ForegroundColor Green
    } catch {
        Write-Host "  升级日志上传失败（不阻断发版，下次发版会重新合并）: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# ── 3. 回环校验：从服务器下载更新包并验签/校验 SHA256 ──
if ((-not $SkipDownloadVerify) -and (-not $SkipWindows -or -not $SkipAndroid)) {
    Write-Step "回环校验：从服务器下载最新包（验签 + SHA256）"
    $dl = Join-Path $ScriptsDir "download_update.ps1"
    if (-not $SkipWindows) { & $dl -Platform windows; if ($LASTEXITCODE -ne 0) { throw "Windows 下载回环校验失败" } }
    if (-not $SkipAndroid) { & $dl -Platform android; if ($LASTEXITCODE -ne 0) { throw "Android 下载回环校验失败" } }
}
else { Write-Host "跳过回环下载校验" -ForegroundColor Yellow }

# ── 4. GitHub ──
if (-not $SkipGit) {
    Write-Step "GitHub：提交版本号并推送"
    & git add NewCosmos.csproj config/app.ini installer/NewCosmosSetup.iss CHANGELOG.md
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Write-Host "版本号无变化，跳过提交" -ForegroundColor Yellow }
    else {
        & git commit -m "发布 $Version"
        if ($LASTEXITCODE -ne 0) { throw "git commit 失败" }
        & git push origin main
        if ($LASTEXITCODE -ne 0) { throw "git push 失败" }
    }
}

Write-Step "发布完成：$Version"
Write-Host "产物目录: $releaseDir"
