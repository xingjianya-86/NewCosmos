# =============================================================================
# 帝皇权杖δ-me13 · 启动动画音频嵌入包生成器
# -----------------------------------------------------------------------------
# 把 Resources\Splash\audio\*.mp3 转成 base64 data URI，生成
# Resources\Splash\core\anim-audio-assets.js（随仓库入库的生成文件）。
#
# 目的（2026-11-15）：启动动画与序列登录**不发起任何 mp3 请求**——
# 虚拟主机（Windows）、APK assets（Android）、本地测试（file://）一律零流量，
# 动画完全自包含（嵌入式）。anim-audio.js 优先读该清单，缺失才回落 audio/*.mp3。
#
# 用法：改了 mp3 后执行
#   powershell -File Scripts\prepare_splash_audio.ps1
# =============================================================================
[CmdletBinding()]
param(
    [string]$AudioDir,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

# 注：PS5.1 的 param() 默认值表达式里 $PSScriptRoot 可能为空，统一在脚本体内解析
if (-not $AudioDir) { $AudioDir = Join-Path $PSScriptRoot '..\Resources\Splash\audio' }
if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot '..\Resources\Splash\core\anim-audio-assets.js' }

$AudioDir = [System.IO.Path]::GetFullPath($AudioDir)
$OutFile = [System.IO.Path]::GetFullPath($OutFile)

if (-not (Test-Path $AudioDir)) {
    throw "音频目录不存在: $AudioDir"
}

$files = @(Get-ChildItem $AudioDir -Filter *.mp3 | Sort-Object Name)
if ($files.Count -eq 0) {
    throw "目录里没有 mp3 文件: $AudioDir"
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('/* ============================================================================')
[void]$sb.AppendLine(' * 帝皇权杖δ-me13 · 音频资产包（生成文件，请勿手改）')
[void]$sb.AppendLine(' * ----------------------------------------------------------------------------')
[void]$sb.AppendLine(' * 由 Scripts\prepare_splash_audio.ps1 生成：audio\*.mp3 → base64 data URI。')
[void]$sb.AppendLine(' * 启动动画与序列登录不再对 mp3 发起任何请求（零虚拟主机/APK/测试流量），')
[void]$sb.AppendLine(' * 动画完全自包含；anim-audio.js 优先读本清单，缺失才回落 audio/*.mp3。')
[void]$sb.AppendLine(' * 音频素材本身的许可与出处见同目录 THIRD-PARTY-NOTICES.md。')
[void]$sb.AppendLine(' * ========================================================================== */')
[void]$sb.AppendLine('window.__NC_AUDIO_ASSETS__ = {')

$total = 0
for ($i = 0; $i -lt $files.Count; $i++) {
    $f = $files[$i]
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $total += $bytes.Length
    $key = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    $b64 = [Convert]::ToBase64String($bytes)
    $comma = if ($i -lt $files.Count - 1) { ',' } else { '' }
    [void]$sb.AppendLine("  '$key': 'data:audio/mpeg;base64,$b64'$comma")
}

[void]$sb.AppendLine('};')

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), $utf8NoBom)

$srcKb = [Math]::Round($total / 1KB, 1)
$outKb = [Math]::Round((Get-Item $OutFile).Length / 1KB, 1)
Write-Host "已生成 $OutFile"
Write-Host ("  音频 {0} 个 / {1} KB → 资产包 {2} KB" -f $files.Count, $srcKb, $outKb)
