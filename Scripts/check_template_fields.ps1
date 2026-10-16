# ============================================================================
# 模板字段体检脚本：nc_biz_templates.config_json 占位符映射一致性检查
#
# 用途：连接数据库导出全部模板的占位符映射，与 Constants\FieldKeys.cs
#       白名单比对，输出四类问题。作为迁移前基线、迁移后验收、发版前体检。
#
# 检查类别（-FailOn 可指定命中即非零退出）：
#   Unknown      fieldKey 不在 FieldKeys 白名单，且非 _N 槽位键（真孤儿，如 CAREGIVER_*）
#   NonAscii     fieldKey 不匹配 ^[A-Z0-9_]+$ （中文键等，迁移目标）
#   MultiSource  同模板内同一 placeholder 对应多个不同 fieldKey（值源冲突，危险）
#   SlotOrphan   _N 槽位键的基键不在白名单（疑似孤儿，默认不 fail）
#
# 仅报告不失败的项：SlotIndexed（_N 槽位且基键在白名单，装配点循环拼接，正常）、
#   MultiPlaceholder（一键多占位符，别名合法）、NoFields（模板无 fields 配置）。
#
# 用法：
#   $env:NEWCOSMOS_DB_PASSWORD='***'
#   .\Scripts\check_template_fields.ps1                          # 只打印摘要
#   .\Scripts\check_template_fields.ps1 -ReportPath .\docs\20261016_模板字段体检报告.md
#   .\Scripts\check_template_fields.ps1 -FailOn Unknown,NonAscii,MultiSource   # 发版前体检
#   .\Scripts\check_template_fields.ps1 -ExportCsv .\out\fields.csv
#
# 依赖：本机 psql（默认 C:\pgsql\bin\psql.exe）。密码从环境变量
#       NEWCOSMOS_DB_PASSWORD 读取（AGENTS 约定，禁止明文写死）；未设置则交互输入。
# ============================================================================
[CmdletBinding()]
param(
    [string]$DbHost = "",
    [int]$DbPort = 5432,
    [string]$DbName = "new_cosmos",
    [string]$DbUser = "new_cosmos",
    [string]$PsqlPath = "C:\pgsql\bin\psql.exe",
    [string]$RepoRoot = "",
    [string]$ReportPath = "",
    [string]$ExportCsv = "",
    [ValidateSet("Unknown","NonAscii","MultiSource","SlotOrphan")]
    [string[]]$FailOn = @(),
    # 批6（2026-10-16）：Unknown 孤儿基线锁（-1=不检查）。Unknown = config 有行但 FieldKeys 无对应常量/
    # 代码无供给的既存孤儿，直接纳入 -FailOn 会恒失败；阈值锁死"只准减不准增"，发版传当前基线，清理后下调。
    [int]$MaxUnknown = -1
)

$ErrorActionPreference = "Stop"
$OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptsDir = $PSScriptRoot
if ($RepoRoot -eq "") { $RepoRoot = Split-Path -Parent $ScriptsDir }
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

# -- 本地部署配置（gitignored）：Scripts\deploy.local.ps1 提供内网地址 --
$deployLocal = Join-Path $ScriptsDir "deploy.local.ps1"
if (Test-Path -LiteralPath $deployLocal) { . $deployLocal }
if ($DbHost   -eq "" -and (Get-Variable LocalDbHost   -ErrorAction SilentlyContinue)) { $DbHost   = $LocalDbHost }
if ($PsqlPath -eq "" -and (Get-Variable LocalPsqlPath -ErrorAction SilentlyContinue)) { $PsqlPath = $LocalPsqlPath }
if ($DbHost -eq "") { throw "未配置 DbHost：请在 Scripts\deploy.local.ps1 设置 `$LocalDbHost，或用 -DbHost 传入" }

function Write-Step([string]$t) { Write-Host ""; Write-Host ("== " + $t + " ==") -ForegroundColor Cyan }

# -- FieldKeys 白名单（取常量值，批1 拆 partial 后 FieldKeys*.cs 通配兼容） --
Write-Step "解析 FieldKeys 白名单"
$fwFiles = @(Get-ChildItem -Path (Join-Path $RepoRoot "Constants") -Filter "FieldKeys*.cs" -File)
if ($fwFiles.Count -eq 0) { throw "找不到 Constants\FieldKeys*.cs" }
$whitelist = New-Object 'System.Collections.Generic.HashSet[string]'
$fwPattern = 'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"'
foreach ($fw in $fwFiles) {
    # -Encoding UTF8 必填：PS5.1 默认按 ANSI(GBK) 解码，会把中文常量值 mangle 掉，
    # 导致中文键匹配不上白名单而被误判 Unknown、且值碰撞少计数（曾 601→592）。
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $fw.FullName -Raw -Encoding UTF8), $fwPattern)) {
        [void]$whitelist.Add($m.Groups[1].Value)
    }
}
Write-Host ("  文件 {0} 个，白名单常量值 {1} 个" -f $fwFiles.Count, $whitelist.Count)

# -- 连接检查 --
if (-not (Test-Path -LiteralPath $PsqlPath)) { throw "找不到 psql: $PsqlPath（用 -PsqlPath 指定）" }
$password = $env:NEWCOSMOS_DB_PASSWORD
if ([string]::IsNullOrEmpty($password)) {
    $secure = Read-Host "请输入数据库密码（NEWCOSMOS_DB_PASSWORD 未设置）" -AsSecureString
    $password = (New-Object System.Net.NetworkCredential('', $secure)).Password
}
if ([string]::IsNullOrEmpty($password)) { throw "数据库密码为空" }

# -- 查询封装：JSON 单行输出，避免 tab/换行/编码污染 --
# 注意：PowerShell 5.1 用 [Console]::OutputEncoding 解码 psql 输出（默认 GBK 会把
# UTF-8 多字节截断成 ?，JSON 失效），必须临时切到 UTF8，finally 还原。
$oldPwd = $env:PGPASSWORD
$oldEnc = $env:PGCLIENTENCODING
$oldConsoleEnc = [Console]::OutputEncoding
$env:PGPASSWORD = $password
$env:PGCLIENTENCODING = "UTF8"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try {
    function Invoke-Q([string]$sql) {
        $out = & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -w -A -t -v ON_ERROR_STOP=1 -c $sql 2>&1
        if ($LASTEXITCODE -ne 0) { throw ("psql 执行失败（退出码 " + $LASTEXITCODE + "）：`n" + $out) }
        return ($out -join "`n").Trim()
    }

    Write-Step ("查询模板清单 " + $DbHost + ":" + $DbPort + "/" + $DbName)
    $tplSql = "select coalesce(json_agg(row_to_json(x) order by x.template_id), '[]'::json) from (select t.id as template_id, t.name as template_name, (t.config_json is not null) as has_config, (t.config_json ? 'fields') as has_fields from nc_biz_templates t) x;"
    $templates = (Invoke-Q $tplSql) | ConvertFrom-Json

    Write-Step "查询占位符映射展开"
    $mapSql = "select coalesce(json_agg(row_to_json(x)), '[]'::json) from (select t.id as template_id, t.name as template_name, f->>'fieldKey' as field_key, f->>'placeholder' as placeholder, f->>'defaultValue' as default_value, f->>'format' as fmt from nc_biz_templates t, jsonb_array_elements(coalesce(t.config_json->'fields', '[]'::jsonb)) f where t.config_json is not null order by t.id, f->>'fieldKey') x;"
    $mappings = (Invoke-Q $mapSql) | ConvertFrom-Json
}
finally {
    [Console]::OutputEncoding = $oldConsoleEnc
    if ($null -eq $oldPwd) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue } else { $env:PGPASSWORD = $oldPwd }
    if ($null -eq $oldEnc) { Remove-Item Env:\PGCLIENTENCODING -ErrorAction SilentlyContinue } else { $env:PGCLIENTENCODING = $oldEnc }
}

# -- 分类 --
Write-Step "分类检查"
$slotPattern = '^(.*)_([0-9]+)$'
$asciiPattern = '^[A-Z0-9_]+$'

$findings = @{
    Unknown      = New-Object System.Collections.ArrayList
    NonAscii     = New-Object System.Collections.ArrayList
    MultiSource  = New-Object System.Collections.ArrayList
    SlotOrphan   = New-Object System.Collections.ArrayList
    SlotIndexed  = New-Object System.Collections.ArrayList
    MultiPlaceholder = New-Object System.Collections.ArrayList
}
$noFields = @($templates | Where-Object { -not $_.has_fields })

function Add-Finding($list, $m, $reason) {
    $o = [pscustomobject]@{
        template_id = $m.template_id; template_name = $m.template_name
        field_key = $m.field_key; placeholder = $m.placeholder
        default_value = $m.default_value; fmt = $m.fmt; reason = $reason }
    [void]$list.Add($o)
}

foreach ($m in $mappings) {
    $k = $m.field_key
    if ([string]::IsNullOrEmpty($k)) { Add-Finding $findings.Unknown $m "fieldKey 为空"; continue }
    if ($k -notmatch $asciiPattern) { Add-Finding $findings.NonAscii $m "非 ASCII 键" }
    if ($whitelist.Contains($k)) { continue }
    if ($k -match $slotPattern) {
        $base = $Matches[1]
        if ($whitelist.Contains($base)) { [void]$findings.SlotIndexed.Add($m) }
        else { Add-Finding $findings.SlotOrphan $m ("基键 " + $base + " 不在白名单") }
    } else {
        Add-Finding $findings.Unknown $m "不在 FieldKeys 白名单"
    }
}

# MultiSource：同模板同 placeholder 多值源（危险）
$srcGroups = $mappings | Where-Object { -not [string]::IsNullOrEmpty($_.placeholder) } |
    Group-Object template_id, placeholder | Where-Object { ($_.Group.field_key | Sort-Object -Unique).Count -gt 1 }
foreach ($g in $srcGroups) {
    $keys = ($g.Group.field_key | Sort-Object -Unique) -join " | "
    [void]$findings.MultiSource.Add([pscustomobject]@{
        template_id = $g.Group[0].template_id; template_name = $g.Group[0].template_name
        placeholder = $g.Group[0].placeholder; field_key = $keys; reason = "同占位符多值源" })
}

# MultiPlaceholder：一键多占位符（别名，合法仅报告）
$aliasGroups = $mappings | Where-Object { -not [string]::IsNullOrEmpty($_.field_key) } |
    Group-Object template_id, field_key | Where-Object { ($_.Group.placeholder | Sort-Object -Unique).Count -gt 1 }
foreach ($g in $aliasGroups) {
    $phs = ($g.Group.placeholder | Sort-Object -Unique) -join " | "
    [void]$findings.MultiPlaceholder.Add([pscustomobject]@{
        template_id = $g.Group[0].template_id; template_name = $g.Group[0].template_name
        field_key = $g.Group[0].field_key; placeholder = $phs; reason = "一键多占位符" })
}

# -- 摘要 --
$distinctKeys = $mappings | ForEach-Object { $_.field_key } | Where-Object { $_ } | Sort-Object -Unique
$hasConfigCount = @($templates | Where-Object has_config).Count
Write-Step "体检摘要"
Write-Host ("  模板总数           : {0}（有 config {1}，无 fields {2}）" -f $templates.Count, $hasConfigCount, $noFields.Count)
Write-Host ("  映射行数           : {0}" -f $mappings.Count)
Write-Host ("  distinct fieldKey  : {0}" -f $distinctKeys.Count)
Write-Host ("  Unknown (真孤儿)   : {0}" -f $findings.Unknown.Count)
Write-Host ("  NonAscii (中文键)  : {0}" -f $findings.NonAscii.Count)
Write-Host ("  MultiSource (危险) : {0}" -f $findings.MultiSource.Count)
Write-Host ("  SlotOrphan         : {0}" -f $findings.SlotOrphan.Count)
Write-Host ("  SlotIndexed (正常) : {0}" -f $findings.SlotIndexed.Count)
Write-Host ("  MultiPlaceholder   : {0}（别名，仅报告）" -f $findings.MultiPlaceholder.Count)

if ($ExportCsv -ne "") {
    $dir = Split-Path -Parent $ExportCsv
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $mappings | Export-Csv -LiteralPath $ExportCsv -Encoding UTF8 -NoTypeInformation
    Write-Host ("  全量映射已导出     : {0}" -f $ExportCsv)
}

# -- Markdown 报告 --
if ($ReportPath -ne "") {
    Write-Step "生成 Markdown 报告"
    $L = New-Object System.Collections.Generic.List[string]
    $L.Add('# 模板字段体检报告')
    $L.Add('')
    $L.Add('> 生成时间：' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '　生成脚本：`Scripts\check_template_fields.ps1`')
    $L.Add('> 检查对象：`nc_biz_templates.config_json` 占位符映射 vs `Constants\FieldKeys.cs` 白名单')
    $L.Add('')
    $L.Add('## 摘要')
    $L.Add('')
    $L.Add('| 指标 | 数值 | 说明 |')
    $L.Add('|---|---|---|')
    $L.Add(('| 模板总数 | {0} | 有 config {1}，无 fields {2} |' -f $templates.Count, $hasConfigCount, $noFields.Count))
    $L.Add(('| 映射行数 | {0} |  |' -f $mappings.Count))
    $L.Add(('| distinct fieldKey | {0} |  |' -f $distinctKeys.Count))
    $L.Add(('| Unknown（真孤儿） | {0} | 不在白名单且非槽位键 |' -f $findings.Unknown.Count))
    $L.Add(('| NonAscii（中文键） | {0} | 迁移目标 |' -f $findings.NonAscii.Count))
    $L.Add(('| MultiSource（危险） | {0} | 同模板同占位符多值源 |' -f $findings.MultiSource.Count))
    $L.Add(('| SlotOrphan | {0} | 槽位基键不在白名单 |' -f $findings.SlotOrphan.Count))
    $L.Add(('| SlotIndexed（正常） | {0} | `_N` 槽位，装配点循环拼接 |' -f $findings.SlotIndexed.Count))
    $L.Add(('| MultiPlaceholder | {0} | 一键多占位符，别名合法 |' -f $findings.MultiPlaceholder.Count))

    $sections = @(
        @{ n = "Unknown";           t = "Unknown（真孤儿，须补白名单或修配置）" },
        @{ n = "NonAscii";          t = "NonAscii（中文键，迁移为 ASCII）" },
        @{ n = "MultiSource";       t = "MultiSource（同占位符多值源，危险）" },
        @{ n = "SlotOrphan";        t = "SlotOrphan（槽位基键孤儿）" },
        @{ n = "MultiPlaceholder";  t = "MultiPlaceholder（一键多占位符，仅报告）" }
    )
    foreach ($s in $sections) {
        $list = $findings[$s.n]
        $L.Add('')
        $L.Add(('## {0}（{1}）' -f $s.t, $list.Count))
        $L.Add('')
        if ($list.Count -eq 0) { $L.Add('_无_'); continue }
        $L.Add('| 模板ID | 模板名 | fieldKey | placeholder | 说明 |')
        $L.Add('|---|---|---|---|---|')
        foreach ($x in ($list | Sort-Object template_id, field_key)) {
            $ph = if ([string]::IsNullOrEmpty($x.placeholder)) { "" } else { $x.placeholder }
            $rs = if ($x.PSObject.Properties['reason']) { $x.reason } else { "" }
            $L.Add(('| {0} | {1} | `{2}` | {3} | {4} |' -f $x.template_id, $x.template_name, $x.field_key, $ph, $rs))
        }
    }
    if ($noFields.Count -gt 0) {
        $L.Add('')
        $L.Add('## NoFields（模板无 fields 配置，直替换或未配）')
        $L.Add('')
        $L.Add('| 模板ID | 模板名 |')
        $L.Add('|---|---|')
        foreach ($x in ($noFields | Sort-Object template_id)) { $L.Add(('| {0} | {1} |' -f $x.template_id, $x.template_name)) }
    }
    $dir = Split-Path -Parent $ReportPath
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [IO.File]::WriteAllText($ReportPath, (($L -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("  报告已生成         : {0}" -f $ReportPath) -ForegroundColor Green
}

# -- FailOn 退出码 --
$hit = @()
foreach ($c in $FailOn) { if ($findings[$c].Count -gt 0) { $hit += ("{0}={1}" -f $c, $findings[$c].Count) } }
# 批6（2026-10-16）：-MaxUnknown 基线锁（防新增孤儿：新上传模板造出 config 有键、代码无供给的行即失败）
if ($MaxUnknown -ge 0 -and $findings.Unknown.Count -gt $MaxUnknown) {
    $hit += ("Unknown={0}>MaxUnknown={1}" -f $findings.Unknown.Count, $MaxUnknown)
}
if ($hit.Count -gt 0) {
    Write-Host ""
    Write-Host ("体检未通过（-FailOn 命中）: " + ($hit -join ", ")) -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "体检通过" -ForegroundColor Green
exit 0
