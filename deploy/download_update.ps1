# ============================================================================
# download_update.ps1 - 从更新服务器下载最新安装包（并验签 + 校验 SHA256）
#
# 流程：拉取 update.json → 校验渠道 → 用编译进 App 的公钥对清单 RSA 验签
#       → 解析包地址 → 下载（重试）→ 校验 SHA256 → 落盘
#
# 用法（双击 deploy\download_update.bat 亦可）：
#   .\deploy\download_update.ps1 -Platform windows
#   .\deploy\download_update.ps1 -Platform android
#   .\deploy\download_update.ps1 -Platform windows -OutputDir D:\updates -SkipSignature
#
# 说明：清单地址默认取 deploy\deploy.local.ps1 的 $LocalVerifyUrl（stable），
#       Android 用其 /stable/→/android/ 推导；也可用 -ManifestUrl 直接指定。
#       验签用 deploy\UpdateSigningTool（公钥取自 Constants\UpdateSignatureConstants.cs，公开）。
# ============================================================================
[CmdletBinding()]
param(
    [ValidateSet('windows', 'android')][string]$Platform = 'windows',
    [string]$ManifestUrl = "",
    [string]$OutputDir = "",
    [switch]$SkipSignature,
    [switch]$SkipDownload
)

$ErrorActionPreference = "Stop"
$ScriptsDir = $PSScriptRoot
$Repo = Split-Path -Parent $ScriptsDir

function Write-Step([string]$t) { Write-Host ""; Write-Host "== $t ==" -ForegroundColor Cyan }

# 本地部署配置（gitignored）
$deployLocal = Join-Path $ScriptsDir "deploy.local.ps1"
if (Test-Path -LiteralPath $deployLocal) { . $deployLocal }
if (-not $ManifestUrl) { $ManifestUrl = $LocalVerifyUrl }
if (-not $ManifestUrl) { throw "未配置清单地址：请设置 deploy\deploy.local.ps1 的 `$LocalVerifyUrl，或用 -ManifestUrl 指定" }
if ($Platform -eq 'android') { $ManifestUrl = $ManifestUrl -replace '/stable/', '/android/' }
if (-not $OutputDir) { $OutputDir = Join-Path $Repo "downloads" }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "平台    : $Platform"
Write-Host "清单地址: $ManifestUrl"
Write-Host "输出目录: $OutputDir"

# ── 1. 拉清单 ──
Write-Step "拉取更新清单"
$resp = Invoke-WebRequest -Uri $ManifestUrl -UseBasicParsing -TimeoutSec 30
$manifestText = [Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray())
$j = $manifestText | ConvertFrom-Json

if ($j.channel -ne 'stable' -and $j.channel -ne 'Stable') { throw "渠道不匹配: $($j.channel)" }
Write-Host "最新版本: $($j.latestVersion)  最低支持: $($j.minSupportedVersion)  强制: $($j.force)"

# ── 2. 验签（RSA-SHA256 over canonical）──
if (-not $SkipSignature) {
    Write-Step "清单验签"
    $constPath = Join-Path $Repo "Constants\UpdateSignatureConstants.cs"
    $pub = ([regex]::Match([IO.File]::ReadAllText($constPath), '-----BEGIN PUBLIC KEY-----[\s\S]*?-----END PUBLIC KEY-----')).Value
    if (-not $pub) { throw "未能从 $constPath 提取公钥" }

    $forceStr = if ($j.force) { 'true' } else { 'false' }
    $canonical = "$($j.schema)|$($j.channel)|$($j.latestVersion)|$($j.minSupportedVersion)|$forceStr|$($j.publishedAt)|$($j.package.url)|$($j.package.size)|$($j.package.sha256)"
    if ([int]$j.schema -ge 2 -and $j.patch) { $canonical += "|$($j.patch.sha256)|$($j.patch.baseVersion)" }

    $tmp = Join-Path $env:TEMP "nc_verify_$PID"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $pubFile = Join-Path $tmp "pub.pem"; $canonFile = Join-Path $tmp "canonical.txt"; $sigFile = Join-Path $tmp "sig.b64"
    [IO.File]::WriteAllText($pubFile, $pub, (New-Object System.Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText($canonFile, $canonical, (New-Object System.Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText($sigFile, $j.signature, (New-Object System.Text.UTF8Encoding($false)))

    $signTool = Join-Path $ScriptsDir "UpdateSigningTool\bin\Release\net10.0\UpdateSigningTool.dll"
    if (-not (Test-Path -LiteralPath $signTool)) {
        & dotnet build (Join-Path $ScriptsDir "UpdateSigningTool\UpdateSigningTool.csproj") -c Release -v q -nologo
        if ($LASTEXITCODE -ne 0) { throw "UpdateSigningTool 构建失败" }
    }
    $out = & dotnet $signTool verify --pub $pubFile --canonical $canonFile --sig $sigFile
    if ($LASTEXITCODE -ne 0 -or ($out -notcontains 'VERIFY_OK')) { throw "清单验签失败: $out" }
    Write-Host "验签通过 (VERIFY_OK)" -ForegroundColor Green
}

# ── 3. 解析包地址 ──
$pkgUrl = if ([Uri]::IsWellFormedUriString($j.package.url, [UriKind]::Absolute)) { $j.package.url }
          else { ([Uri]::new([Uri]$ManifestUrl, $j.package.url)).ToString() }
$target = Join-Path $OutputDir $j.package.url
Write-Host "安装包  : $pkgUrl"
Write-Host "SHA256  : $($j.package.sha256)"

# ── 4. 下载（重试 + 校验）──
Write-Step "下载安装包"
if ($SkipDownload) {
    Write-Host "（-SkipDownload）已完成清单拉取与验签，跳过实际下载。" -ForegroundColor Yellow
    Write-Host "包地址: $pkgUrl"
    return
}
$need = $true
if (Test-Path -LiteralPath $target) {
    $have = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($have -eq $j.package.sha256) { Write-Host "已存在且校验通过，跳过下载: $target" -ForegroundColor Green; $need = $false }
}
if ($need) {
    $ok = $false
    for ($i = 1; $i -le 3; $i++) {
        try {
            Write-Host "开始下载（第 $i 次）..."
            Invoke-WebRequest -Uri $pkgUrl -OutFile $target -UseBasicParsing -TimeoutSec 1800
            $ok = $true; break
        } catch { Write-Host "下载失败: $($_.Exception.Message)" -ForegroundColor Yellow; Start-Sleep -Seconds $i }
    }
    if (-not $ok) { throw "下载失败（已重试 3 次）" }
}
$final = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
if ($final -ne $j.package.sha256) { throw "SHA256 校验失败！期望 $($j.package.sha256)，实际 $final" }

Write-Host ""
Write-Host "下载完成：$target" -ForegroundColor Green
Write-Host "版本 $($j.latestVersion)  大小 $([Math]::Round((Get-Item $target).Length/1MB,1)) MB  SHA256 校验通过"
