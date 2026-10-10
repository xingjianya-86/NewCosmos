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
    [switch]$SkipWindows,
    [switch]$SkipAndroid,
    [switch]$SkipGit,
    [switch]$SkipDownloadVerify,
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
if (-not $NonInteractive) {
    Write-Host ""
    Write-Host "===== 发布向导（回车=默认；Y=是，F/N=否）=====" -ForegroundColor Cyan
    if (-not $PSBoundParameters.ContainsKey('Version')) {
        $v = Read-Host "版本号 [默认 $Version]"
        if (-not [string]::IsNullOrWhiteSpace($v)) { $Version = $v.Trim() }
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

if ($DryRun) {
    Write-Host ""
    Write-Host "===== DRY RUN（不构建/不上传/不推 GitHub）=====" -ForegroundColor Yellow
    $winFlags = @()
    if (-not $WithPatch) { $winFlags += '-SkipPatch' }
    if ($Force) { $winFlags += '-Force' }
    Write-Host ("  Windows : publish_release.ps1 -Version {0} -MinSupported {1} -Channel {2} {3}" -f $Version, $MinSupported, $Channel, ($winFlags -join ' '))
    Write-Host ("  Android : dotnet publish -f net10.0-android36.0 -c Release -r android-arm64 -p:RunAOTCompilation=true" + $(if ($WithPatch) { "（并生成增量补丁）" } else { "" }))
    Write-Host ("  上一版  : {0}" -f $(if ($prevResolved) { $prevResolved } else { "（未解析到）" }))
    Write-Host ("  回环校验: {0}" -f $(if ($SkipDownloadVerify) { "跳过" } else { "执行（下载两端安装包）" }))
    Write-Host ("  GitHub  : {0}" -f $(if ($SkipGit) { "跳过" } else { "提交并推送" }))
    Write-Host ("  产物目录: {0}" -f (Join-Path $Repo "publish\release\$Version"))
    Write-Host ("  版本同步: {0}（csproj/app.ini/iss；DryRun 不写入）" -f $Version)
    return
}

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
$iss = [regex]::Replace($iss, 'VersionInfoVersion=[0-9.]+', "VersionInfoVersion=1.1.$yearV.$mdV")
[IO.File]::WriteAllText($issPath, $iss, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "已同步：$Version / $numeric / VersionInfo=1.1.$yearV.$mdV"

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
    $signature = (Get-Content -LiteralPath $sigFile -Raw).Trim()
    $manifest = [ordered]@{
        schema = $schema; channel = $Channel; latestVersion = $Version; minSupportedVersion = $MinSupported
        force = [bool]$Force; publishedAt = $publishedAt; notes = ""
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
    & git add NewCosmos.csproj config/app.ini installer/NewCosmosSetup.iss
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
