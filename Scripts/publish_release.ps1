# ============================================================================
# 在线更新发布脚本：版本号同步 → dotnet publish → Inno 安装包 → 清单签名 → 上传
#
# 用法（示例）：
#   .\Scripts\publish_release.ps1 -Version 1.1.20260918 -Notes "修复XXX"
#   .\Scripts\publish_release.ps1 -Version 1.1.20260918 -MinSupported 1.1.20260910 -Force
#
# 前置：
#   1) 签名私钥已生成：  .\Scripts\generate_update_signing_key.ps1
#   2) 部署 SSH 密钥已加入服务器（一次性）：%USERPROFILE%\.ssh\id_ed25519_newcosmos
#
# 说明：脚本会同步三处版本号（NewCosmos.csproj / config\app.ini / installer\NewCosmosSetup.iss），
#       并生成带 RSA-SHA256 签名的 update.json 后上传到宝塔站点更新目录。
#
# 依赖（均在 Scripts\ 下，勿删）：
#   Scripts\PatchTool           生成增量补丁（BsDiff）
#   Scripts\UpdateSigningTool   清单 RSA-SHA256 签名
# ============================================================================
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$MinSupported = "",
    [switch]$Force,
    [string]$Notes = "",
    [string]$NotesFile = "",
    [string]$Channel = "stable",
    [string]$ServerHost = "",
    [string]$ServerUser = "root",
    [string]$ServerRoot = "",
    [string]$SshKey = "$env:USERPROFILE\.ssh\id_ed25519_newcosmos",
    [string]$SigningKey = "$env:USERPROFILE\.newcosmos\update_signing_key.pem",
    [string]$IsccPath = "",
    [string]$VerifyUrl = "",
    [string]$DbHost = "",
    [string]$DbName = "new_cosmos",
    [string]$DbUser = "new_cosmos",
    [string]$PsqlPath = "C:\pgsql\bin\psql.exe",
    [switch]$SkipVersionSync,
    [switch]$SkipUpload,
    [switch]$SkipPatch
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# ── 本地部署配置（gitignored）：Scripts\deploy.local.ps1 提供内网地址 ──
$deployLocal = Join-Path $PSScriptRoot "deploy.local.ps1"
if (Test-Path -LiteralPath $deployLocal) { . $deployLocal }
if ($ServerHost -eq "" -and (Get-Variable LocalServerHost -ErrorAction SilentlyContinue)) { $ServerHost = $LocalServerHost }
if ($ServerRoot -eq "" -and (Get-Variable LocalServerRoot -ErrorAction SilentlyContinue)) { $ServerRoot = $LocalServerRoot }
if ($VerifyUrl  -eq "" -and (Get-Variable LocalVerifyUrl  -ErrorAction SilentlyContinue)) { $VerifyUrl  = $LocalVerifyUrl }
if ($DbHost     -eq "" -and (Get-Variable LocalDbHost     -ErrorAction SilentlyContinue)) { $DbHost     = $LocalDbHost }
if ($ServerHost -eq "") { throw "未配置 ServerHost：请在 Scripts\deploy.local.ps1 设置 `$LocalServerHost，或用 -ServerHost 传入" }
if ($ServerRoot -eq "") { throw "未配置 ServerRoot：请在 Scripts\deploy.local.ps1 设置 `$LocalServerRoot，或用 -ServerRoot 传入" }
if ($DbHost -eq "") { $DbHost = $ServerHost }

if ($MinSupported -eq "") { $MinSupported = $Version }
if ($Version -notmatch '^\d+\.\d+\.\d{8}$') { throw "版本号格式应为 主.次.yyyyMMdd，如 1.1.20260918（当前: $Version）" }
$numeric = ($Version -split '\.')[2]

function Write-Step([string]$text) { Write-Host ""; Write-Host "== $text ==" -ForegroundColor Cyan }

Write-Step "发布版本 $Version（最低支持 $MinSupported，强制=$([bool]$Force)）"

# ── 1. 版本号三处同步 ──
if (-not $SkipVersionSync) {
    Write-Step "同步版本号（csproj / app.ini / iss）"

    $csprojPath = Join-Path $repo "NewCosmos.csproj"
    $csproj = [IO.File]::ReadAllText($csprojPath)
    $csproj = [regex]::Replace($csproj, '<ApplicationDisplayVersion>[^<]*</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$Version</ApplicationDisplayVersion>")
    $csproj = [regex]::Replace($csproj, '<ApplicationVersion>[^<]*</ApplicationVersion>', "<ApplicationVersion>$numeric</ApplicationVersion>")
    [IO.File]::WriteAllText($csprojPath, $csproj, (New-Object System.Text.UTF8Encoding($false)))

    $appIniPath = Join-Path $repo "config\app.ini"
    $appIni = [IO.File]::ReadAllText($appIniPath)
    $appIni = [regex]::Replace($appIni, '(?m)^Version=.*$', "Version=$Version")
    [IO.File]::WriteAllText($appIniPath, $appIni, (New-Object System.Text.UTF8Encoding($false)))

    $issPath = Join-Path $repo "installer\NewCosmosSetup.iss"
    $iss = [IO.File]::ReadAllText($issPath)
    $iss = [regex]::Replace($iss, '#define MyAppVersion "[^"]*"', "#define MyAppVersion ""$Version""")
    $year = [int]$numeric.Substring(0, 4)
    $md = [int]$numeric.Substring(4, 4)
    $iss = [regex]::Replace($iss, 'VersionInfoVersion=[0-9.]+', "VersionInfoVersion=1.1.$year.$md")
    [IO.File]::WriteAllText($issPath, $iss, (New-Object System.Text.UTF8Encoding($false)))

    Write-Host "已同步：$Version / $numeric / VersionInfo=1.1.$year.$md"
}

# ── 2. 发布与打包 ──
Write-Step "dotnet publish（Release / win-x64 / 自包含）"
$publishDir = Join-Path $repo "publish\win-x64"
# 注意：必须用 -p:TargetFrameworks 限定为单目标，否则全局 -r win-x64 会作用到 Android TFM
# （Android 目标会去解析 Microsoft.NETCore.App.Runtime.Mono.win-x64，不存在 → NU1102 还原失败）
& dotnet publish "NewCosmos.csproj" -c Release -f net10.0-windows10.0.19041.0 -r win-x64 -p:TargetFrameworks=net10.0-windows10.0.19041.0 -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

if ($IsccPath -eq "") {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { $IsccPath = $candidate; break }
    }
}
if ($IsccPath -eq "" -or -not (Test-Path -LiteralPath $IsccPath)) { throw "未找到 ISCC.exe，请用 -IsccPath 指定 Inno Setup 编译器路径" }

Write-Step "编译安装包（Inno Setup）"
& $IsccPath (Join-Path $repo "installer\NewCosmosSetup.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败" }

$setupName = "NewCosmosSetup_$Version.exe"
$setupPath = Join-Path $repo "publish\$setupName"
if (-not (Test-Path -LiteralPath $setupPath)) { throw "未找到安装包: $setupPath" }

$releaseDir = Join-Path $repo "publish\release\$Version"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Copy-Item -LiteralPath $setupPath -Destination $releaseDir -Force

# ── 2.5 增量 patch 生成 ──
$patchInfo = $null
if (-not $SkipPatch) {
    Write-Step "生成增量 patch（从上一 stable 版本）"

    # 查上一个 stable 版本
    $prevVersion = $null
    if ($env:NEWCOSMOS_DB_PASSWORD -and (Test-Path -LiteralPath $PsqlPath)) {
        $env:PGPASSWORD = $env:NEWCOSMOS_DB_PASSWORD
        $env:PGCLIENTENCODING = "UTF8"
        $prevSqlFile = Join-Path $env:TEMP "prev_version_$Version.sql"
        [IO.File]::WriteAllText($prevSqlFile, "SELECT version FROM nc_sys_app_versions WHERE channel='stable' AND version<>'$Version' ORDER BY id DESC LIMIT 1;", (New-Object System.Text.UTF8Encoding($false)))
        $prevResult = & $PsqlPath -h $DbHost -p 5432 -U $DbUser -d $DbName -t -A -w -f $prevSqlFile 2>$null
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
        Remove-Item Env:\PGCLIENTENCODING -ErrorAction SilentlyContinue
        if ($prevResult) { $prevVersion = $prevResult.Trim() }
    }

    if ($prevVersion) {
        $prevInstaller = Join-Path $repo "publish\release\$prevVersion\NewCosmosSetup_$prevVersion.exe"
        if (-not (Test-Path -LiteralPath $prevInstaller)) {
            Write-Host "  下载上一版本安装包作为 base: $prevVersion"
            if (Test-Path -LiteralPath $SshKey) {
                $remote = "$ServerUser@$ServerHost"
                & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new "${remote}:$ServerRoot/releases/$prevVersion/NewCosmosSetup_$prevVersion.exe" $prevInstaller 2>$null
            }
        }
        if (Test-Path -LiteralPath $prevInstaller) {
            $patchName = "NewCosmosPatch_${prevVersion}_${Version}.bin"
            $patchPath = Join-Path $releaseDir $patchName
            $patchToolProj = Join-Path $repo "Scripts\PatchTool\PatchTool.csproj"
            & dotnet run --project $patchToolProj -- create --old $prevInstaller --new $setupPath --out $patchPath
            if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $patchPath)) {
                $patchSize = (Get-Item -LiteralPath $patchPath).Length
                $patchSha = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()
                $patchInfo = [ordered]@{ url = $patchName; size = $patchSize; sha256 = $patchSha; baseVersion = $prevVersion }
                Write-Host "  patch: $patchName ($([Math]::Round($patchSize / 1MB, 1)) MB)" -ForegroundColor Green
            } else {
                Write-Host "  patch 生成失败，本次发布不含增量 patch" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  无法获取 $prevVersion 安装包，跳过 patch 生成" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  无历史 stable 版本，跳过 patch 生成" -ForegroundColor Yellow
    }
}

# ── 3. 清单与签名 ──
Write-Step "生成并签名 update.json"
$size = (Get-Item -LiteralPath $setupPath).Length
$sha = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
$publishedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")

$notesText = $Notes
if ($NotesFile -ne "" -and (Test-Path -LiteralPath $NotesFile)) {
    # 显式按 UTF-8 读取（PS5.1 Get-Content 默认 ANSI，中文会乱码）
    $notesText = [IO.File]::ReadAllText($NotesFile, [Text.Encoding]::UTF8).Trim()
}

$forceStr = "false"
if ($Force) { $forceStr = "true" }

# schema=2 当有 patch；schema=1 无 patch
$schema = if ($patchInfo) { 2 } else { 1 }
$patchSha = if ($patchInfo) { $patchInfo.sha256 } else { "" }
$patchBase = if ($patchInfo) { $patchInfo.baseVersion } else { "" }
# 与客户端 BuildCanonicalString 保持一致：schema=1 为 9 字段（无 patch 字段）；schema=2 才追加 patchSha|patchBase
$canonical = "$schema|$Channel|$Version|$MinSupported|$forceStr|$publishedAt|$setupName|$size|$sha"
if ($patchInfo) { $canonical += "|$($patchInfo.sha256)|$($patchInfo.baseVersion)" }
$canonicalFile = Join-Path $releaseDir "canonical.txt"
[IO.File]::WriteAllText($canonicalFile, $canonical, (New-Object System.Text.UTF8Encoding($false)))

if (-not (Test-Path -LiteralPath $SigningKey)) { throw "未找到签名私钥: $SigningKey（先运行 Scripts\generate_update_signing_key.ps1）" }

$toolProj = Join-Path $repo "Scripts\UpdateSigningTool\UpdateSigningTool.csproj"
& dotnet build $toolProj -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "签名工具编译失败" }
$toolDll = Join-Path $repo "Scripts\UpdateSigningTool\bin\Release\net10.0\UpdateSigningTool.dll"

$sigFile = Join-Path $releaseDir "update.sig"
& dotnet $toolDll sign --key $SigningKey --canonical $canonicalFile --out $sigFile | Out-Null
if ($LASTEXITCODE -ne 0) { throw "清单签名失败" }
$signature = (Get-Content -LiteralPath $sigFile -Raw).Trim()

$manifest = [ordered]@{
    schema              = $schema
    channel             = $Channel
    latestVersion       = $Version
    minSupportedVersion = $MinSupported
    force               = [bool]$Force
    publishedAt         = $publishedAt
    notes               = $notesText
    package             = [ordered]@{
        url    = $setupName
        size   = $size
        sha256 = $sha
    }
    signature           = $signature
}
if ($patchInfo) {
    $manifest.patch = [ordered]@{
        url         = $patchInfo.url
        size        = $patchInfo.size
        sha256      = $patchInfo.sha256
        baseVersion = $patchInfo.baseVersion
    }
}
$manifestJson = ConvertTo-Json $manifest -Depth 5
$manifestPath = Join-Path $releaseDir "update.json"
[IO.File]::WriteAllText($manifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "安装包: $setupName ($([Math]::Round($size / 1MB, 1)) MB)"
Write-Host "SHA256: $sha"
if ($patchInfo) { Write-Host "Patch:   $($patchInfo.url) ($([Math]::Round($patchInfo.size / 1MB, 1)) MB, from $($patchInfo.baseVersion))" }

# ── 4. 上传服务器 ──
if (-not $SkipUpload) {
    Write-Step "上传到 $ServerHost$ServerRoot"
    if (-not (Test-Path -LiteralPath $SshKey)) { throw "未找到 SSH 密钥: $SshKey（请配置 SSH 部署密钥）" }

    $remote = "$ServerUser@$ServerHost"
    & ssh -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $remote "mkdir -p $ServerRoot/stable $ServerRoot/releases/$Version"
    if ($LASTEXITCODE -ne 0) { throw "SSH 连接失败（请确认部署密钥已加入服务器）" }

    $uploadFiles = @($setupPath, $manifestPath)
    if ($patchInfo) { $uploadFiles += Join-Path $releaseDir $patchInfo.url }
    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new @uploadFiles "${remote}:$ServerRoot/stable/"
    if ($LASTEXITCODE -ne 0) { throw "上传 stable 失败" }

    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $setupPath "${remote}:$ServerRoot/releases/$Version/"
    if ($LASTEXITCODE -ne 0) { throw "上传 releases 归档失败" }

    if ($patchInfo) {
        $patchLocal = Join-Path $releaseDir $patchInfo.url
        & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $patchLocal "${remote}:$ServerRoot/releases/$Version/"
        if ($LASTEXITCODE -ne 0) { throw "上传 patch 归档失败" }
    }

    Write-Host "上传完成: $ServerRoot/stable/{update.json, $setupName" + $(if ($patchInfo) { ", $($patchInfo.url)" } else { "" }) + "}"
}

# ── 5. 发布历史入库（可选；需 NEWCOSMOS_DB_PASSWORD 环境变量） ──
if ($env:NEWCOSMOS_DB_PASSWORD -and (Test-Path -LiteralPath $PsqlPath)) {
    Write-Step "记录发布历史 nc_sys_app_versions"
    $escapedNotes = $notesText.Replace("'", "''")
    $sql = "INSERT INTO nc_sys_app_versions (version, build_number, channel, notes) VALUES ('$Version', '$numeric', '$Channel', '$escapedNotes');"
    # psql 通过 -c 传中文在 Windows 控制台会按 GBK 编码失败：写 UTF-8 文件 + PGCLIENTENCODING 执行
    $sqlFile = Join-Path $env:TEMP "newcosmos_release_$Version.sql"
    [IO.File]::WriteAllText($sqlFile, $sql, (New-Object System.Text.UTF8Encoding($false)))
    $env:PGPASSWORD = $env:NEWCOSMOS_DB_PASSWORD
    $env:PGCLIENTENCODING = "UTF8"
    & $PsqlPath -h $DbHost -p 5432 -U $DbUser -d $DbName -w -f $sqlFile
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:\PGCLIENTENCODING -ErrorAction SilentlyContinue
}
else {
    Write-Host "跳过发布历史入库（未设置 NEWCOSMOS_DB_PASSWORD）" -ForegroundColor Yellow
}

# ── 6. 线上校验 ──
if (-not $SkipUpload -and $VerifyUrl -ne "") {
    Write-Step "线上校验 $VerifyUrl"
    Start-Sleep -Seconds 2
    try {
        $resp = Invoke-WebRequest -Uri $VerifyUrl -TimeoutSec 20 -UseBasicParsing
        $remote = $resp.Content | ConvertFrom-Json
        if ($remote.latestVersion -eq $Version) { Write-Host "线上清单版本: $($remote.latestVersion) OK" -ForegroundColor Green }
        else { Write-Host "线上清单版本不一致: $($remote.latestVersion)" -ForegroundColor Yellow }
    }
    catch { Write-Host "线上校验失败（稍后手动确认）: $($_.Exception.Message)" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "发布完成: $Version" -ForegroundColor Green
Write-Host "产物目录: $releaseDir"
