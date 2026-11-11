<#
  2026-11-11 四张声明/协查类模板占位符文本改造（二进制 file_data 本机修正脚本）
  配套文档：docs\migrations\20261111_template_survey_date_bindings.sql（config_json 换键/备份/回滚）
  变更说明见 docs\20261111_模板日期改入户调查日与赡养人家庭人口修复.md

  改动（每张 docx 的 word/document.xml 恰好 1 处）：
    <w:t>当前日期</w:t>  →  <w:t>入户调查日期</w:t>
    占位符在 docx 里被拆成三个 run（"{" / "当前日期" / "}"），故只替换中间那段，
    花括号两侧 run 原样保留，拼出的完整占位符即 {入户调查日期}。

  模板清单（与 config 换键同批，见配套 SQL）：
    224 部省协查函 / 243 档案_结婚证遗失声明 / 244 档案_离婚证遗失声明 / 245 档案_无房产声明
  ⚠️ 241 档案_变更告知书、248 模板_社会救助对象动态管理记录 也含 {当前日期}，**不在本脚本范围**。

  执行流程：取 file_data → 建备份表 + 存 BEFORE 快照 → 精确替换（每模板要求恰好命中 1 次，否则中止）
            → 回读校验 → SHA256 → 回写 file_data/file_hash/updated_at → 复核。
  幂等性：第二次执行时源串不再命中 → 该模板记为"已修正"并跳过；全部已修正则直接退出，不会重复改写。

  凭据（AGENTS §12）：数据库密码读环境变量 NEWCOSMOS_DB_PASSWORD；ini 仅取 Host/Port/DatabaseName/Username。
  用法：
    $env:NEWCOSMOS_DB_PASSWORD = '<密码>'
    powershell -File docs\migrations\20261111_declare_surveydate_text_fix.ps1 [-DryRun]
#>
[CmdletBinding()]
param(
    [switch]$DryRun   # 只做替换与校验、输出 SHA256，不建备份表、不回写数据库
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

# ── 1. 连接参数（密码只走环境变量） ──
$iniPath = Join-Path $root 'config\database.ini'
if (-not (Test-Path $iniPath)) { throw "未找到 $iniPath" }
$ini = @{}
Get-Content $iniPath -Encoding UTF8 | ForEach-Object {
    if ($_ -match '^\s*([^#;=\s][^=]*)=(.*)$') { $ini[$Matches[1].Trim()] = $Matches[2].Trim() }
}
$host_ = $ini['Host']; $port = $ini['Port']; $db = $ini['DatabaseName']; $user = $ini['Username']
$psql = if (Test-Path 'C:\pgsql\bin\psql.exe') { 'C:\pgsql\bin\psql.exe' } else { 'psql' }
if (-not $env:NEWCOSMOS_DB_PASSWORD) { throw "缺少环境变量 NEWCOSMOS_DB_PASSWORD（AGENTS §12：脚本禁止明文密码）" }
$env:PGPASSWORD = $env:NEWCOSMOS_DB_PASSWORD

function Invoke-Psql([string]$sql) {
    $tmp = Join-Path $env:TEMP ("ncfix_{0}.sql" -f ([guid]::NewGuid().ToString('N')))
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $eap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { $out = & $psql -X -w -h $host_ -p $port -U $user -d $db -t -A -F '|' -f $tmp 2>&1 } finally { $ErrorActionPreference = $eap }
        if ($LASTEXITCODE -ne 0) { throw ($out -join [char]10) }
        return @($out | Where-Object { $_ -notmatch '^\s*(NOTICE|WARNING):' })
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

$sha = { param([byte[]]$b) $s = [System.Security.Cryptography.SHA256]::Create(); try { ($s.ComputeHash($b) | ForEach-Object { $_.ToString('x2') }) -join '' } finally { $s.Dispose() } }

function HexToBytes([string]$hex) {
    $hex = $hex.Trim() -replace '\s', ''
    if (-not $hex -or $hex -notmatch '^[0-9a-fA-F]+$') { return $null }
    $b = [byte[]]::new($hex.Length / 2)
    for ($i = 0; $i -lt $b.Length; $i++) { $b[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16) }
    return $b
}

# 改前 SHA256 基线（2026-11-11 实测，用于执行前人工核对）
$expectedBefore = @{
    224 = 'da9ea7c6ed6d0df61fe492a1be15308d5dd2560c0113d7d0d55c4d4a4da62cef'
    243 = '24a1b883c99ced9e58c4e49962cde4304502dbdd86472e704f71eaf1a218a7ca'
    244 = 'f81baa4145f4e812358324e88fd773e90e251fd869e8a72b253787bf7d8e3120'
    245 = '4a12a727216d5c99c8c1b651b5b49dc39ff96b0bdf6596853b5334be8689a086'
}

$targets = @(
    @{ Id = 224; Name = '部省协查函' },
    @{ Id = 243; Name = '档案_结婚证遗失声明' },
    @{ Id = 244; Name = '档案_离婚证遗失声明' },
    @{ Id = 245; Name = '档案_无房产声明' }
)

$oldRun = '<w:t>当前日期</w:t>'
$newRun = '<w:t>入户调查日期</w:t>'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$pending = @()   # 待回写：@{ Id, Name, NewBytes, NewSha }

foreach ($t in $targets) {
    $id = $t.Id
    Write-Host ""
    Write-Host ("── 模板 {0} {1} ──" -f $id, $t.Name) -ForegroundColor Cyan

    $hex = (Invoke-Psql "SELECT encode(file_data,'hex') FROM nc_biz_templates WHERE id = $id;") -join ''
    $bytes = HexToBytes $hex
    if ($null -eq $bytes) { throw "模板 $id file_data 读取失败（空或非 hex）" }
    $beforeSha = & $sha $bytes
    $baseline = $expectedBefore[$id]
    if ($baseline -and $beforeSha -ne $baseline) {
        Write-Host ("  ⚠ 改前 SHA256 与基线不符：实际 {0}，基线 {1}" -f $beforeSha, $baseline) -ForegroundColor Yellow
        Write-Host "    （可能已被手工修改过；继续前请人工确认该模板仍含 {当前日期}）" -ForegroundColor Yellow
    } else {
        Write-Host ("  改前 SHA256 与基线一致（{0} 字节）" -f $bytes.Length)
    }

    $work = Join-Path $env:TEMP (("nc_tpl{0}_" -f $id) + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $docxPath = Join-Path $work 'tpl.docx'
    [System.IO.File]::WriteAllBytes($docxPath, $bytes)

    # 读 word/document.xml
    $zip = [System.IO.Compression.ZipFile]::OpenRead($docxPath)
    $entry = $zip.GetEntry('word/document.xml')
    if ($null -eq $entry) { $zip.Dispose(); Remove-Item $work -Recurse -Force; throw "模板 $id docx 内无 word/document.xml" }
    $sr = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
    $xml = $sr.ReadToEnd(); $sr.Close(); $zip.Dispose()

    $n = ([regex]::Matches($xml, [regex]::Escape($oldRun))).Count
    if ($n -eq 0) {
        Write-Host "  源串 <w:t>当前日期</w:t> 未命中（可能已修正）——跳过，不回写。" -ForegroundColor Yellow
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        continue
    }
    if ($n -ne 1) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue; throw "模板 $id 源串命中 $n 次（预期 1 次），中止" }
    $xml = $xml.Replace($oldRun, $newRun)
    Write-Host "  已替换 <w:t>当前日期</w:t> → <w:t>入户调查日期</w:t>" -ForegroundColor Green

    # 回写 docx（仅 word/document.xml，其余 zip 条目原样）
    $zip = [System.IO.Compression.ZipFile]::Open($docxPath, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $e = $zip.GetEntry('word/document.xml')
        $e.Delete()
        $ne = $zip.CreateEntry('word/document.xml', [System.IO.Compression.CompressionLevel]::Optimal)
        $sw = New-Object System.IO.StreamWriter($ne.Open(), (New-Object System.Text.UTF8Encoding($false)))
        $sw.Write($xml); $sw.Close()
    } finally { $zip.Dispose() }

    # 回读校验
    $zip = [System.IO.Compression.ZipFile]::OpenRead($docxPath)
    $entry = $zip.GetEntry('word/document.xml')
    $sr = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
    $xml2 = $sr.ReadToEnd(); $sr.Close(); $zip.Dispose()
    $checks = @(
        @{ Desc = '不再含 <w:t>当前日期</w:t>'; Ok = -not $xml2.Contains('<w:t>当前日期</w:t>') },
        @{ Desc = '已含 <w:t>入户调查日期</w:t>'; Ok = $xml2.Contains('<w:t>入户调查日期</w:t>') },
        @{ Desc = '花括号两侧 run 完整（{ 与 } 仍在）'; Ok = ($xml2.Contains('<w:t>{</w:t>') -and $xml2.Contains('<w:t>}</w:t>')) }
    )
    $fail = $false
    foreach ($c in $checks) { if ($c.Ok) { Write-Host ("    ✓ {0}" -f $c.Desc) -ForegroundColor Green } else { Write-Host ("    ✗ {0}" -f $c.Desc) -ForegroundColor Red; $fail = $true } }
    if ($fail) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue; throw "模板 $id 回读校验未通过，中止（未回写数据库）" }

    $newBytes = [System.IO.File]::ReadAllBytes($docxPath)
    $newSha = & $sha $newBytes
    Write-Host ("  新 SHA256(after) = {0}（{1} 字节）" -f $newSha, $newBytes.Length)
    $pending += [pscustomobject]@{ Id = $id; Name = $t.Name; NewBytes = $newBytes; NewSha = $newSha }
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($pending.Count -eq 0) {
    Write-Host ""
    Write-Host "四张模板均已处于目标状态（源串全部未命中），无需回写。" -ForegroundColor Yellow
    exit 0
}

# ── 备份 + 回写 ──
if ($DryRun) {
    Write-Host ""
    Write-Host ("-DryRun：跳过备份与回写（待回写 {0} 张）。" -f $pending.Count) -ForegroundColor Yellow
} else {
    $ids = ($pending | ForEach-Object { $_.Id }) -join ','
    Invoke-Psql @"
CREATE TABLE IF NOT EXISTS backup_20261111_declare_surveydate_template AS
SELECT id, name, file_type, file_data, file_hash, config_json, sort_order, updated_at, now() AS backed_up_at
FROM nc_biz_templates WHERE id IN ($ids);
"@ | Out-Null
    $bakCount = (Invoke-Psql "SELECT count(*) FROM backup_20261111_declare_surveydate_template;") -join ''
    if ([int]$bakCount -lt $pending.Count) { throw "备份表写入不足（$bakCount < $($pending.Count)）" }
    Write-Host ("备份表 backup_20261111_declare_surveydate_template 就绪（{0} 行）" -f $bakCount)

    foreach ($p in $pending) {
        $hexOut = ($p.NewBytes | ForEach-Object { $_.ToString('x2') }) -join ''
        $updSql = "UPDATE nc_biz_templates SET file_data = decode('" + $hexOut + "','hex'), file_hash = '" + $p.NewSha + "', updated_at = now() WHERE id = " + $p.Id + ";"
        Invoke-Psql $updSql | Out-Null
        $after = (Invoke-Psql "SELECT id || '|' || octet_length(file_data) || '|' || file_hash FROM nc_biz_templates WHERE id = $($p.Id);") -join ''
        Write-Host ("  回写模板 {0} {1}：{2}" -f $p.Id, $p.Name, $after) -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "完成。注意：应用有模板进程内缓存，重新生成文书前请重启应用。"
