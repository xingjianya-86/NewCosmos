# ============================================================================
# build_all.ps1 - 一键本地构建：双端安装包 + 增量补丁 + 可直接运行的 Debug 版
#
# 产物（均在 publish\ 下，不上传服务器）：
#   Windows 安装包 : publish\NewCosmosSetup_<v>.exe（Inno）
#   Android  APK   : publish\NewCosmosSetup_<v>.apk（Release，keystore 签名）
#   Android 清单   : publish\release\<v>\android\update.json（已签名）
#   增量补丁       : publish\release\<v>\NewCosmosPatch_<prev>_<v>.bin + Windows update.json(schema=2)
#   Debug 可运行版 : bin\Debug\net10.0-windows10.0.19041.0\win-x64\NewCosmos.exe（直接双击运行）
#
# 用法：
#   .\Scripts\build_all.ps1                       # 用 csproj 当前版本，全量构建
#   .\Scripts\build_all.ps1 -Version 1.1.20261005 # 指定版本（同步 csproj/app.ini/iss 三处）
#   .\Scripts\build_all.ps1 -SkipAndroid -SkipPatch
#   .\Scripts\build_all.ps1 -DryRun               # 只打印计划，不实际构建
#
# 依赖：.NET 10 SDK + maui-windows/maui-android；Inno Setup 6；JDK 17 + Android SDK（Android 端）。
#       Android 发布签名由 keystore\keystore.props 提供（已被 .gitignore 忽略）。
#
# 说明：Android 默认关闭 AOT（-p:RunAOTCompilation=false）。本机 mono-aot-cross 存在
#       "response file can not be read" 故障，需要时加 -RunAOT 重开。
# ============================================================================
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$PrevVersion = "",
    [string]$Channel = "stable",
    [switch]$SkipWindows,
    [switch]$SkipAndroid,
    [switch]$SkipPatch,
    [switch]$SkipDebug,
    [switch]$RunAOT,
    [string]$JavaSdkDirectory = "C:\AndroidJdk\jdk-17.0.2",
    [string]$AndroidSdkDirectory = "$env:LOCALAPPDATA\Android\Sdk",
    [string]$IsccPath = "",
    [string]$SigningKey = "$env:USERPROFILE\.newcosmos\update_signing_key.pem",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ScriptsDir = $PSScriptRoot
$Repo = Split-Path -Parent $ScriptsDir
Set-Location $Repo

function Write-Step([string]$t) { Write-Host ""; Write-Host "== $t ==" -ForegroundColor Cyan }

# ── 0. 版本号 ──
$csprojPath = Join-Path $Repo "NewCosmos.csproj"
if ($Version -eq "") {
    $m = [regex]::Match([IO.File]::ReadAllText($csprojPath), '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>')
    if (-not $m.Success) { throw "无法从 NewCosmos.csproj 读取 ApplicationDisplayVersion，请用 -Version 指定" }
    $Version = $m.Groups[1].Value.Trim()
    Write-Host "使用 csproj 当前版本: $Version"
}
else {
    if ($Version -notmatch '^\d+\.\d+\.\d{8}$') { throw "版本号格式应为 主.次.yyyyMMdd，如 1.1.20261005（当前: $Version）" }
    $numeric = ($Version -split '\.')[2]
    Write-Step "同步版本号三处（csproj / app.ini / iss）为 $Version"
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
    $year = [int]$numeric.Substring(0, 4); $md = [int]$numeric.Substring(4, 4)
    $iss = [regex]::Replace($iss, 'VersionInfoVersion=[0-9.]+', "VersionInfoVersion=1.1.$year.$md")
    [IO.File]::WriteAllText($issPath, $iss, (New-Object System.Text.UTF8Encoding($false)))
}

$releaseDir  = Join-Path $Repo "publish\release\$Version"
$setupName   = "NewCosmosSetup_$Version.exe"
$setupPath   = Join-Path $Repo "publish\$setupName"
$apkName     = "NewCosmosSetup_$Version.apk"
$apkPath     = Join-Path $Repo "publish\$apkName"
$androidDir  = Join-Path $releaseDir "android"
$patchTool   = Join-Path $ScriptsDir "PatchTool\bin\Release\net10.0\PatchTool.dll"
$signTool    = Join-Path $ScriptsDir "UpdateSigningTool\bin\Release\net10.0\UpdateSigningTool.dll"

Write-Step "构建计划（版本 $Version）"
Write-Host ("  Windows 安装包 : {0}{1}" -f $(if ($SkipWindows) { "(跳过) " } else { "" }), $setupPath)
Write-Host ("  Android  APK   : {0}{1}" -f $(if ($SkipAndroid) { "(跳过) " } else { "" }), $apkPath)
Write-Host ("  增量补丁       : {0}{1}" -f $(if ($SkipPatch) { "(跳过) " } else { "" }), (Join-Path $releaseDir "NewCosmosPatch_<prev>_$Version.bin"))
Write-Host ("  Debug 可运行版 : {0}" -f $(if ($SkipDebug) { "(跳过)" } else { "bin\Debug\net10.0-windows10.0.19041.0\win-x64\NewCosmos.exe" }))

if ($DryRun) { Write-Host ""; Write-Host "DRY RUN：未执行任何构建。" -ForegroundColor Yellow; exit 0 }

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

# ── 工具：缺则构建 ──
if ((-not $SkipPatch) -and -not (Test-Path -LiteralPath $patchTool)) {
    Write-Step "构建 PatchTool"
    & dotnet build (Join-Path $ScriptsDir "PatchTool\PatchTool.csproj") -c Release -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "PatchTool 构建失败" }
}
if ((-not $SkipPatch) -or (-not $SkipAndroid)) {
    if (-not (Test-Path -LiteralPath $signTool)) {
        Write-Step "构建 UpdateSigningTool"
        & dotnet build (Join-Path $ScriptsDir "UpdateSigningTool\UpdateSigningTool.csproj") -c Release -v q -nologo
        if ($LASTEXITCODE -ne 0) { throw "UpdateSigningTool 构建失败" }
    }
}

# ── 1. Windows 安装包 ──
if (-not $SkipWindows) {
    Write-Step "Windows：dotnet publish（Release / win-x64 / 自包含）"
    $winPublishDir = Join-Path $Repo "publish\win-x64"
    & dotnet publish "NewCosmos.csproj" -c Release -f net10.0-windows10.0.19041.0 -r win-x64 -p:TargetFrameworks=net10.0-windows10.0.19041.0 -o $winPublishDir
    if ($LASTEXITCODE -ne 0) { throw "Windows dotnet publish 失败" }

    if ($IsccPath -eq "") {
        foreach ($c in @("C:\Program Files (x86)\Inno Setup 6\ISCC.exe","C:\Program Files\Inno Setup 6\ISCC.exe","$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe","$env:LOCALAPPDATA\Inno Setup 6\ISCC.exe")) {
            if (Test-Path -LiteralPath $c) { $IsccPath = $c; break }
        }
    }
    if ($IsccPath -eq "" -or -not (Test-Path -LiteralPath $IsccPath)) { throw "未找到 ISCC.exe，请用 -IsccPath 指定" }

    Write-Step "Windows：编译 Inno 安装包"
    & $IsccPath (Join-Path $Repo "installer\NewCosmosSetup.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败" }
    if (-not (Test-Path -LiteralPath $setupPath)) { throw "未找到安装包: $setupPath" }
    Copy-Item -LiteralPath $setupPath -Destination $releaseDir -Force
    Write-Host "Windows 安装包: $setupPath" -ForegroundColor Green
}

# ── 2. Android APK + 清单 ──
if (-not $SkipAndroid) {
    Write-Step "Android：dotnet publish（Release / android-arm64）"
    if (-not (Test-Path -LiteralPath $JavaSdkDirectory)) { throw "未找到 JDK: $JavaSdkDirectory（用 -JavaSdkDirectory 指定）" }
    if (-not (Test-Path -LiteralPath $AndroidSdkDirectory)) { throw "未找到 Android SDK: $AndroidSdkDirectory（用 -AndroidSdkDirectory 指定）" }

    $oldJava = $env:JAVA_HOME; $oldAndroid = $env:ANDROID_HOME
    $env:JAVA_HOME = $JavaSdkDirectory
    $env:ANDROID_HOME = $AndroidSdkDirectory
    try {
        $aotArg = if ($RunAOT) { "true" } else { "false" }
        & dotnet publish "NewCosmos.csproj" -f net10.0-android36.0 -c Release -r android-arm64 `
            -p:JavaSdkDirectory=$JavaSdkDirectory -p:RunAOTCompilation=$aotArg
        if ($LASTEXITCODE -ne 0) { throw "Android dotnet publish 失败" }
    }
    finally {
        if ($null -eq $oldJava) { Remove-Item Env:\JAVA_HOME -ErrorAction SilentlyContinue } else { $env:JAVA_HOME = $oldJava }
        if ($null -eq $oldAndroid) { Remove-Item Env:\ANDROID_HOME -ErrorAction SilentlyContinue } else { $env:ANDROID_HOME = $oldAndroid }
    }

    $apkPubDir = Join-Path $Repo "bin\Release\net10.0-android36.0\android-arm64\publish"
    $builtApk = Get-ChildItem -LiteralPath $apkPubDir -Filter "*-Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $builtApk) { $builtApk = Get-ChildItem -LiteralPath $apkPubDir -Filter "*.apk" -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if (-not $builtApk) { throw "未找到 Android APK（$apkPubDir）" }
    Copy-Item -LiteralPath $builtApk.FullName -Destination $apkPath -Force

    Write-Step "Android：生成并签名 update.json（schema=1）"
    $apkSize = (Get-Item -LiteralPath $apkPath).Length
    $apkSha  = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $publishedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    $canonical = "1|$Channel|$Version|$Version|false|$publishedAt|$apkName|$apkSize|$apkSha"
    New-Item -ItemType Directory -Force -Path $androidDir | Out-Null
    $canonFile = Join-Path $androidDir "canonical.txt"
    [IO.File]::WriteAllText($canonFile, $canonical, (New-Object System.Text.UTF8Encoding($false)))
    if (-not (Test-Path -LiteralPath $SigningKey)) { throw "未找到签名私钥: $SigningKey" }
    $sigFile = Join-Path $androidDir "update.sig"
    & dotnet $signTool sign --key $SigningKey --canonical $canonFile --out $sigFile
    if ($LASTEXITCODE -ne 0) { throw "Android 清单签名失败" }
    $signature = (Get-Content -LiteralPath $sigFile -Raw).Trim()
    $androidManifest = [ordered]@{
        schema = 1; channel = $Channel; latestVersion = $Version; minSupportedVersion = $Version
        force = $false; publishedAt = $publishedAt; notes = ""
        package = [ordered]@{ url = $apkName; size = $apkSize; sha256 = $apkSha }
        signature = $signature
    }
    [IO.File]::WriteAllText((Join-Path $androidDir "update.json"), (ConvertTo-Json $androidManifest -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Android APK: $apkPath ($([Math]::Round($apkSize/1MB,1)) MB)" -ForegroundColor Green
    Write-Host "Android 清单: $androidDir\update.json"
}

# ── 3. 增量补丁（Windows） ──
if (-not $SkipPatch) {
    Write-Step "增量补丁：定位上一版本安装包"
    $prev = $PrevVersion
    if ($prev -eq "") {
        $cands = Get-ChildItem (Join-Path $Repo "publish\release") -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne $Version } |
            Sort-Object { try { [version]$_.Name } catch { [version]"0.0" } } -Descending
        foreach ($c in $cands) {
            $exe = Join-Path $c.FullName "NewCosmosSetup_$($c.Name).exe"
            if (Test-Path -LiteralPath $exe) { $prev = $c.Name; break }
        }
    }
    if (-not $prev) {
        Write-Host "未找到可用的上一版本安装包（publish\release\*），跳过补丁生成" -ForegroundColor Yellow
    }
    else {
        $prevExe = Join-Path $Repo "publish\release\$prev\NewCosmosSetup_$prev.exe"
        if (-not (Test-Path -LiteralPath $prevExe)) { throw "上一版本安装包不存在: $prevExe" }
        $patchName = "NewCosmosPatch_${prev}_${Version}.bin"
        $patchPath = Join-Path $releaseDir $patchName
        Write-Step "生成补丁 $prev -> $Version（BsDiff，可能较慢）"
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & dotnet $patchTool create --old $prevExe --new $setupPath --out $patchPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $patchPath)) { throw "补丁生成失败" }
        $sw.Stop()
        $patchSize = (Get-Item -LiteralPath $patchPath).Length
        $patchSha  = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()

        Write-Step "重签 Windows update.json（schema=2）"
        $exeSize = (Get-Item -LiteralPath $setupPath).Length
        $exeSha  = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $winManifestPath = Join-Path $releaseDir "update.json"
        if (Test-Path -LiteralPath $winManifestPath) {
            $old = Get-Content -LiteralPath $winManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $winPublishedAt = $old.publishedAt; $notes = $old.notes
        } else { $winPublishedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz"); $notes = "" }
        $canonical = "2|$Channel|$Version|$Version|false|$winPublishedAt|$setupName|$exeSize|$exeSha|$patchSha|$prev"
        $canonFile = Join-Path $releaseDir "canonical.txt"
        [IO.File]::WriteAllText($canonFile, $canonical, (New-Object System.Text.UTF8Encoding($false)))
        $sigFile = Join-Path $releaseDir "update.sig"
        & dotnet $signTool sign --key $SigningKey --canonical $canonFile --out $sigFile
        if ($LASTEXITCODE -ne 0) { throw "Windows 清单签名失败" }
        $signature = (Get-Content -LiteralPath $sigFile -Raw).Trim()
        $winManifest = [ordered]@{
            schema = 2; channel = $Channel; latestVersion = $Version; minSupportedVersion = $Version
            force = $false; publishedAt = $winPublishedAt; notes = $notes
            package = [ordered]@{ url = $setupName; size = $exeSize; sha256 = $exeSha }
            patch   = [ordered]@{ url = $patchName; size = $patchSize; sha256 = $patchSha; baseVersion = $prev }
            signature = $signature
        }
        [IO.File]::WriteAllText($winManifestPath, (ConvertTo-Json $winManifest -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "补丁: $patchPath ($([Math]::Round($patchSize/1MB,1)) MB, from $prev, $([Math]::Round($sw.Elapsed.TotalSeconds,0))s)" -ForegroundColor Green
        Write-Host "Windows 清单: $winManifestPath (schema=2)"
    }
}

# ── 4. Debug 可运行版 ──
if (-not $SkipDebug) {
    Write-Step "Debug：dotnet build（可直接运行）"
    & dotnet build "NewCosmos.csproj" -c Debug -f net10.0-windows10.0.19041.0
    if ($LASTEXITCODE -ne 0) { throw "Debug 构建失败" }
    $debugExe = Join-Path $Repo "bin\Debug\net10.0-windows10.0.19041.0\win-x64\NewCosmos.exe"
    if (Test-Path -LiteralPath $debugExe) { Write-Host "Debug 可执行: $debugExe" -ForegroundColor Green }
    else { Write-Host "Debug 构建完成，但未在预期路径找到 exe：$debugExe" -ForegroundColor Yellow }
}

# ── 汇总 ──
Write-Step "构建完成（版本 $Version）"
Write-Host "  产物目录: $releaseDir"
Get-ChildItem -LiteralPath $releaseDir -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ("    {0}  ({1:N1} MB)" -f $_.FullName.Substring($Repo.Length + 1), ($_.Length / 1MB))
}
