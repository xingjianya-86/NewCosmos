<#
  2026-11-03 模板 241《档案_变更告知书》正文文案修正（二进制 file_data 本机修正脚本）
  配套文档：docs\migrations\20261103_changenotice_template_text.sql（备份/回滚/复核口径）
  变更说明见 docs\20261103_告知书称谓与双句号修正.md

  两处正文修正（word/document.xml，均为整段 run 文本，精确串替换）：
    1) 「申请家庭人口{户主家庭人口}人。」→「申请家庭人口{户主家庭人口}。」
       字段值 NOTICE_FAMILY_SIZE_TEXT 自带"人"字（"1人"），模板再带"人"会渲染成"1人人"。
    2) 「决定对您家庭{承办分类}的社会救助保障待遇{享受类型}{享受结果}。」
       →「决定对您家庭{承办分类}{享受类型}{享受结果}。」
       字面"的社会救助保障待遇"与 {享受结果} 重复，渲染成"…社会救助保障待遇停止享受社会救助保障待遇"。

  执行流程：取 file_data → 建备份表 + 存 BEFORE docx → 精确替换（各要求恰好命中 1 次，否则中止）
            → 回读校验 → SHA256 → 回写 file_data/file_hash/updated_at → 复核。
  幂等性：第二次执行时源串不再命中 → 脚本中止（"已修正"提示），不会重复改写。

  凭据（AGENTS §12）：数据库密码读环境变量 NEWCOSMOS_DB_PASSWORD；ini 仅取 Host/Port/DatabaseName/Username。
  用法：
    $env:NEWCOSMOS_DB_PASSWORD = '<密码>'
    powershell -File docs\migrations\20261103_changenotice_template_text_fix.ps1 [-DryRun]
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

# ── 2. 取出模板 241 二进制 ──
$hex = (Invoke-Psql "SELECT encode(file_data,'hex') FROM nc_biz_templates WHERE id = 241;") -join ''
$hex = $hex.Trim()
if (-not $hex -or $hex -notmatch '^[0-9a-fA-F]+$') { throw "模板 241 file_data 读取失败（空或非 hex）" }
$bytes = [byte[]]::new($hex.Length / 2)
for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16) }
Write-Host ("模板 241 file_data：{0} 字节，SHA256(before)={1}" -f $bytes.Length, (& $sha $bytes))

$work = Join-Path $env:TEMP ("nc_tpl241_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$docxPath = Join-Path $work 'tpl241.docx'
[System.IO.File]::WriteAllBytes($docxPath, $bytes)

# ── 3. 读 word/document.xml 并做两处精确替换 ──
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($docxPath)
$entry = $zip.GetEntry('word/document.xml')
if ($null -eq $entry) { $zip.Dispose(); throw "docx 内无 word/document.xml" }
$sr = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
$xml = $sr.ReadToEnd(); $sr.Close(); $zip.Dispose()

$edits = @(
    @{ Name = '去重复"人"字（1人人→1人）'
       Old  = '<w:t>人。根据《牡丹江市低收入人口认定实施细则》（牡民规〔</w:t>'
       New  = '<w:t>。根据《牡丹江市低收入人口认定实施细则》（牡民规〔</w:t>' },
    @{ Name = '去重复短语（社会救助保障待遇×2）'
       Old  = '<w:t>的社会救助保障待遇</w:t>'
       New  = '<w:t></w:t>' }
)
foreach ($e in $edits) {
    $n = ([regex]::Matches($xml, [regex]::Escape($e.Old))).Count
    if ($n -eq 0) {
        Write-Host ("[{0}] 源串未命中（可能已修正）——中止，未做任何回写。" -f $e.Name) -ForegroundColor Yellow
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        exit 0
    }
    if ($n -ne 1) { throw ("[{0}] 源串命中 {1} 次（预期 1 次），中止" -f $e.Name, $n) }
    $xml = $xml.Replace($e.Old, $e.New)
    Write-Host ("[{0}] 已替换" -f $e.Name) -ForegroundColor Green
}

# ── 4. 回写 docx（仅替换 word/document.xml，其余条目原样保留） ──
$zip = [System.IO.Compression.ZipFile]::Open($docxPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $zip.GetEntry('word/document.xml')
    $entry.Delete()
    $newEntry = $zip.CreateEntry('word/document.xml', [System.IO.Compression.CompressionLevel]::Optimal)
    $sw = New-Object System.IO.StreamWriter($newEntry.Open(), (New-Object System.Text.UTF8Encoding($false)))
    $sw.Write($xml); $sw.Close()
} finally { $zip.Dispose() }

# ── 5. 回读校验 ──
$zip = [System.IO.Compression.ZipFile]::OpenRead($docxPath)
$entry = $zip.GetEntry('word/document.xml')
$sr = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
$xml2 = $sr.ReadToEnd(); $sr.Close(); $zip.Dispose()
$checks = @(
    @{ Desc = '不再有"人。根据《…实施细则》"' ; Ok = -not $xml2.Contains('人。根据《牡丹江市低收入人口认定实施细则》') },
    @{ Desc = '占位符 {户主家庭人口} 完整保留'   ; Ok = $xml2.Contains('户主家庭人口') },
    @{ Desc = '字面"的社会救助保障待遇"已移除'   ; Ok = -not $xml2.Contains('的社会救助保障待遇') },
    @{ Desc = '{享受类型}{享受结果} 占位符保留'  ; Ok = ($xml2.Contains('享受类型') -and $xml2.Contains('享受结果')) }
)
$fail = $false
foreach ($c in $checks) { if ($c.Ok) { Write-Host ("  ✓ {0}" -f $c.Desc) -ForegroundColor Green } else { Write-Host ("  ✗ {0}" -f $c.Desc) -ForegroundColor Red; $fail = $true } }
if ($fail) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue; throw "回读校验未通过，未回写数据库" }

$newBytes = [System.IO.File]::ReadAllBytes($docxPath)
$newSha = & $sha $newBytes
Write-Host ("新 file_data：{0} 字节，SHA256(after)={1}" -f $newBytes.Length, $newSha)

# ── 6. 备份 + 回写数据库 ──
if ($DryRun) {
    Write-Host "-DryRun：跳过备份与回写。" -ForegroundColor Yellow
} else {
    Invoke-Psql @"
CREATE TABLE IF NOT EXISTS backup_20261103_changenotice_template AS
SELECT id, name, file_type, file_data, file_hash, config_json, sort_order, updated_at, now() AS backed_up_at
FROM nc_biz_templates WHERE id = 241;
"@ | Out-Null
    $bakCount = (Invoke-Psql "SELECT count(*) FROM backup_20261103_changenotice_template;") -join ''
    if ([int]$bakCount -lt 1) { throw "备份表写入失败（0 行）" }
    Write-Host ("备份表 backup_20261103_changenotice_template 就绪（{0} 行）" -f $bakCount)

    $newBytesHex = ($newBytes | ForEach-Object { $_.ToString('x2') }) -join ''
    $updSql = "UPDATE nc_biz_templates SET file_data = decode('" + $newBytesHex + "','hex'), file_hash = '" + $newSha + "', updated_at = now() WHERE id = 241;"
    Invoke-Psql $updSql | Out-Null

    $after = (Invoke-Psql "SELECT octet_length(file_data), file_hash FROM nc_biz_templates WHERE id = 241;") -join ''
    Write-Host ("回写完成：octet_length|file_hash = {0}" -f $after) -ForegroundColor Green
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "完成。注意：应用有模板进程内缓存，重新生成文书前请重启应用。"
