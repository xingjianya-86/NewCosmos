# ============================================================================
# publish_all.ps1 - 一键双端发布：Windows 安装包 + Android(AOT) APK → 上传服务器
#
# 组合流程：
#   1) Windows：调用 Scripts\publish_release.ps1（版本同步→publish→Inno→签名→上传→入库→校验；本次不含增量补丁）
#   2) Android：dotnet publish（Release/android-arm64/开启 AOT）→ 签名 schema=1 清单 → 上传 /android/
#   3) 可选：GitHub 提交并推送版本号三处
#
# 用法（双击 Scripts\publish_all.bat 亦可）：
#   .\Scripts\publish_all.ps1                      # 版本取 csproj（ApplicationDisplayVersion）
#   .\Scripts\publish_all.ps1 -Version 1.1.20261008
#   .\Scripts\publish_all.ps1 -SkipGit
#
# 依赖：.NET 10 SDK + maui-windows/maui-android；Inno Setup 6；JDK17 + Android SDK；keystore\keystore.props。
# 内网地址/服务器来自 Scripts\deploy.local.ps1（gitignored）；发布历史入库需环境变量 NEWCOSMOS_DB_PASSWORD。
# 说明：Android AOT 需 ASCII 临时目录（Windows 用户名含中文时 %TEMP% 非 ASCII 会使 mono-aot-cross 读不到 temp.rsp）。
# ============================================================================
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$MinSupported = "",
    [string]$Channel = "stable",
    [switch]$SkipWindows,
    [switch]$SkipAndroid,
    [switch]$SkipGit,
    [switch]$SkipDownloadVerify,
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

# ── 版本号 ──
$csprojPath = Join-Path $Repo "NewCosmos.csproj"
if ($Version -eq "") {
    $m = [regex]::Match([IO.File]::ReadAllText($csprojPath), '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>')
    if (-not $m.Success) { throw "无法从 csproj 读取版本号，请用 -Version 指定" }
    $Version = $m.Groups[1].Value.Trim()
}
if ($MinSupported -eq "") { $MinSupported = $Version }
Write-Host "发布版本: $Version（最低支持 $MinSupported，渠道 $Channel）"

$releaseDir = Join-Path $Repo "publish\release\$Version"
$apkName    = "NewCosmosSetup_$Version.apk"
$apkPath    = Join-Path $Repo "publish\$apkName"
$androidDir = Join-Path $releaseDir "android"

# ── 1. Windows ──
if (-not $SkipWindows) {
    Write-Step "Windows：publish_release.ps1（含上传与入库）"
    $pr = Join-Path $ScriptsDir "publish_release.ps1"
    & $pr -Version $Version -MinSupported $MinSupported -Channel $Channel -SkipPatch
    if ($LASTEXITCODE -ne 0) { throw "Windows 发布失败（退出码 $LASTEXITCODE）" }
}
else { Write-Host "跳过 Windows" -ForegroundColor Yellow }

# ── 2. Android（AOT）──
if (-not $SkipAndroid) {
    if (-not $ServerHost) { throw "未配置 ServerHost：请设置 Scripts\deploy.local.ps1 的 `$LocalServerHost" }
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

    Write-Step "Android：签名 schema=1 清单"
    $size = (Get-Item -LiteralPath $apkPath).Length
    $sha  = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $publishedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    $canonical = "1|$Channel|$Version|$MinSupported|false|$publishedAt|$apkName|$size|$sha"
    New-Item -ItemType Directory -Force -Path $androidDir | Out-Null
    $canonFile = Join-Path $androidDir "canonical.txt"
    [IO.File]::WriteAllText($canonFile, $canonical, (New-Object System.Text.UTF8Encoding($false)))
    if (-not (Test-Path -LiteralPath $SigningKey)) { throw "未找到签名私钥: $SigningKey" }
    $signTool = Join-Path $ScriptsDir "UpdateSigningTool\bin\Release\net10.0\UpdateSigningTool.dll"
    if (-not (Test-Path -LiteralPath $signTool)) {
        & dotnet build (Join-Path $ScriptsDir "UpdateSigningTool\UpdateSigningTool.csproj") -c Release -v q -nologo
        if ($LASTEXITCODE -ne 0) { throw "UpdateSigningTool 构建失败" }
    }
    $sigFile = Join-Path $androidDir "update.sig"
    & dotnet $signTool sign --key $SigningKey --canonical $canonFile --out $sigFile
    if ($LASTEXITCODE -ne 0) { throw "Android 清单签名失败" }
    $signature = (Get-Content -LiteralPath $sigFile -Raw).Trim()
    $manifest = [ordered]@{
        schema = 1; channel = $Channel; latestVersion = $Version; minSupportedVersion = $MinSupported
        force = $false; publishedAt = $publishedAt; notes = ""
        package = [ordered]@{ url = $apkName; size = $size; sha256 = $sha }
        signature = $signature
    }
    $manifestPath = Join-Path $androidDir "update.json"
    [IO.File]::WriteAllText($manifestPath, (ConvertTo-Json $manifest -Depth 5), (New-Object System.Text.UTF8Encoding($false)))

    Write-Step "Android：上传到 $ServerHost$ServerRoot/android"
    if (-not (Test-Path -LiteralPath $SshKey)) { throw "未找到 SSH 密钥: $SshKey" }
    $remote = "$ServerUser@$ServerHost"
    & ssh -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $remote "mkdir -p $ServerRoot/android $ServerRoot/releases/$Version"
    if ($LASTEXITCODE -ne 0) { throw "SSH 连接失败" }
    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $apkPath $manifestPath "${remote}:$ServerRoot/android/"
    if ($LASTEXITCODE -ne 0) { throw "上传 Android 失败" }
    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $apkPath "${remote}:$ServerRoot/releases/$Version/" 2>$null

    Write-Step "Android：线上校验"
    $verifyUrl = if ($VerifyUrlStable) { $VerifyUrlStable -replace '/stable/', '/android/' } else { "" }
    if ($verifyUrl) {
        Start-Sleep -Seconds 2
        try {
            $j = (Invoke-WebRequest -Uri $verifyUrl -UseBasicParsing -TimeoutSec 25).Content | ConvertFrom-Json
            if ($j.package.sha256 -eq $sha) { Write-Host "Android 线上清单 OK: sha=$($j.package.sha256)" -ForegroundColor Green }
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
    if (-not $SkipWindows) {
        & $dl -Platform windows
        if ($LASTEXITCODE -ne 0) { throw "Windows 下载回环校验失败" }
    }
    if (-not $SkipAndroid) {
        & $dl -Platform android
        if ($LASTEXITCODE -ne 0) { throw "Android 下载回环校验失败" }
    }
}
else { Write-Host "跳过回环下载校验" -ForegroundColor Yellow }

# ── 4. GitHub ──
if (-not $SkipGit) {
    Write-Step "GitHub：提交版本号并推送"
    & git add NewCosmos.csproj config/app.ini installer/NewCosmosSetup.iss
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "版本号无变化，跳过提交" -ForegroundColor Yellow
    } else {
        & git commit -m "发布 $Version"
        if ($LASTEXITCODE -ne 0) { throw "git commit 失败" }
        & git push origin main
        if ($LASTEXITCODE -ne 0) { throw "git push 失败" }
    }
}

Write-Step "发布完成：$Version"
Write-Host "产物目录: $releaseDir"
