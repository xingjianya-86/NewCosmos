# ============================================================================
# 模板导入脚本（通用）：把 Excel/Word 模板 + config.json 导入 nc_biz_templates
#
# 用途：将模板文件（.xlsx/.docx）及其字段配置（同名 .config.json）写入
#       nc_biz_templates(name, file_type, file_data, categories, config_json, sort_order)。
#       同名模板按"先删后插"替换（表上 name 无唯一约束，导入即权威覆盖）。
#
# 用法：
#   # 1) 清单模式（推荐，可精确指定 categories/configFile/sortOrder）
#   $env:NEWCOSMOS_DB_PASSWORD='***'
#   .\Scripts\import_templates.ps1 -Manifest .\Scripts\templates_manifest.json
#
#   # 2) 目录扫描模式：导入目录下所有 .xlsx/.docx，自动配对同名 .config.json
#   .\Scripts\import_templates.ps1 -SourceDir .\Templates_NEW -DefaultCategories "经济复核","档案_定期复核审批表"
#
#   # 3) 预演（只打印将执行的 SQL，不连库）
#   .\Scripts\import_templates.ps1 -Manifest .\Scripts\templates_manifest.json -DryRun
#
# 清单条目字段：
#   name        必填，模板名（与库中 name 对应）
#   file        必填，模板文件路径（相对仓库根或相对清单文件所在目录）
#   categories  选填，字符串数组，默认空
#   configFile  选填，字段配置 json 路径；缺省时自动尝试同目录 <fileBasename>.config.json
#   sortOrder   选填，整数，默认 0
#
# 依赖：本机 psql（默认 C:\pgsql\bin\psql.exe）。密码从环境变量
#       NEWCOSMOS_DB_PASSWORD 读取（AGENTS 约定，禁止明文写死）；未设置则交互输入。
# ============================================================================
[CmdletBinding()]
param(
    [string]$Manifest = "",
    [string]$SourceDir = "",
    [string[]]$DefaultCategories = @(),
    [int]$DefaultSortOrder = 0,
    [string]$DbHost = "",
    [int]$DbPort = 5432,
    [string]$DbName = "new_cosmos",
    [string]$DbUser = "new_cosmos",
    [string]$PsqlPath = "C:\pgsql\bin\psql.exe",
    [string]$RepoRoot = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$ScriptsDir = $PSScriptRoot
if ($RepoRoot -eq "") { $RepoRoot = Split-Path -Parent $ScriptsDir }
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

# ── 本地部署配置（gitignored）：Scripts\deploy.local.ps1 提供内网地址 ──
$deployLocal = Join-Path $ScriptsDir "deploy.local.ps1"
if (Test-Path -LiteralPath $deployLocal) { . $deployLocal }
if ($DbHost   -eq "" -and (Get-Variable LocalDbHost   -ErrorAction SilentlyContinue)) { $DbHost   = $LocalDbHost }
if ($PsqlPath -eq "" -and (Get-Variable LocalPsqlPath -ErrorAction SilentlyContinue)) { $PsqlPath = $LocalPsqlPath }
if ($DbHost -eq "") { throw "未配置 DbHost：请在 Scripts\deploy.local.ps1 设置 `$LocalDbHost，或用 -DbHost 传入" }

function Write-Step([string]$t) { Write-Host ""; Write-Host "== $t ==" -ForegroundColor Cyan }

function Resolve-TemplatePath([string]$path, [string]$baseDir) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }
    if ([IO.Path]::IsPathRooted($path)) {
        if (Test-Path -LiteralPath $path) { return (Resolve-Path -LiteralPath $path).Path }
        return $null
    }
    foreach ($base in @($RepoRoot, $baseDir)) {
        if ([string]::IsNullOrWhiteSpace($base)) { continue }
        $candidate = Join-Path $base $path
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    return $null
}

function ConvertTo-SqlLiteral([string]$s) {
    if ($null -eq $s) { return "''" }
    return "'" + $s.Replace("'", "''") + "'"
}

# ── 组装导入条目 ──
$entries = New-Object System.Collections.Generic.List[object]

if ($Manifest -ne "") {
    if (-not (Test-Path -LiteralPath $Manifest)) { throw "找不到清单文件: $Manifest" }
    $manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
    $manifestDir = Split-Path -Parent $manifestPath
    $items = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($items -isnot [System.Collections.IEnumerable]) { $items = @($items) }
    foreach ($it in $items) {
        $entries.Add([pscustomobject]@{
            Name        = [string]$it.name
            File        = [string]$it.file
            Categories  = @($it.categories | ForEach-Object { [string]$_ })
            ConfigFile  = [string]$it.configFile
            SortOrder   = if ($null -ne $it.sortOrder) { [int]$it.sortOrder } else { $DefaultSortOrder }
            BaseDir     = $manifestDir
        })
    }
}
elseif ($SourceDir -ne "") {
    $dir = Resolve-TemplatePath $SourceDir $RepoRoot
    if ($null -eq $dir -or -not (Test-Path -LiteralPath $dir -PathType Container)) { throw "找不到目录: $SourceDir" }
    foreach ($f in Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Extension -in @('.xlsx', '.docx') }) {
        $cfg = Join-Path $f.DirectoryName ($f.BaseName + '.config.json')
        $entries.Add([pscustomobject]@{
            Name        = $f.BaseName
            File        = $f.FullName
            Categories  = $DefaultCategories
            ConfigFile  = if (Test-Path -LiteralPath $cfg) { $cfg } else { "" }
            SortOrder   = $DefaultSortOrder
            BaseDir     = $dir
        })
    }
}
else {
    throw "请用 -Manifest 或 -SourceDir 指定导入来源（-Manifest .\Scripts\templates_manifest.json）"
}

if ($entries.Count -eq 0) { throw "没有可导入的模板条目" }

Write-Step "导入计划（$($entries.Count) 个模板）"

$defaultConfig = '{"fields":[],"tables":[],"compositeFields":[]}'
$sqlStatements = New-Object System.Collections.Generic.List[string]

foreach ($e in $entries) {
    if ([string]::IsNullOrWhiteSpace($e.Name)) { throw "清单条目缺少 name" }

    $filePath = Resolve-TemplatePath $e.File $e.BaseDir
    if ($null -eq $filePath) { throw "模板文件不存在: $($e.File)（name=$($e.Name)）" }

    $ext = [IO.Path]::GetExtension($filePath).ToLowerInvariant()
    $fileType = switch ($ext) {
        '.xlsx' { 'xlsx' }
        '.docx' { 'docx' }
        default { throw "不支持的模板扩展名: $ext（仅 .xlsx/.docx）" }
    }

    $cfgPath = ""
    if (-not [string]::IsNullOrWhiteSpace($e.ConfigFile)) {
        $cfgPath = Resolve-TemplatePath $e.ConfigFile $e.BaseDir
        if ($null -eq $cfgPath) { throw "配置 json 不存在: $($e.ConfigFile)（name=$($e.Name)）" }
    } else {
        $auto = Join-Path (Split-Path -Parent $filePath) ([IO.Path]::GetFileNameWithoutExtension($filePath) + '.config.json')
        if (Test-Path -LiteralPath $auto) { $cfgPath = (Resolve-Path -LiteralPath $auto).Path }
    }

    if ($cfgPath -ne "") {
        $configJson = [IO.File]::ReadAllText($cfgPath, [Text.Encoding]::UTF8).Trim()
        try { $null = $configJson | ConvertFrom-Json } catch { throw "配置 json 非法: $cfgPath - $($_.Exception.Message)" }
    } else {
        $configJson = $defaultConfig
    }

    $bytes = [IO.File]::ReadAllBytes($filePath)
    $hex = ([BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()

    if ($e.Categories.Count -gt 0) {
        $catList = ($e.Categories | ForEach-Object { ConvertTo-SqlLiteral $_ }) -join ','
        $catSql = "ARRAY[$catList]::text[]"
    } else {
        $catSql = "ARRAY[]::text[]"
    }

    $nameSql = ConvertTo-SqlLiteral $e.Name
    $configSql = ConvertTo-SqlLiteral $configJson

    Write-Host ("  - {0}  [{1}]  {2:N0} bytes  cats={3}  cfg={4}" -f `
        $e.Name, $fileType, $bytes.Length,
        ($(if ($e.Categories.Count) { $e.Categories -join ',' } else { '-' })),
        $(if ($cfgPath -ne "") { Split-Path -Leaf $cfgPath } else { '(default)' }))

    $sqlStatements.Add("DELETE FROM nc_biz_templates WHERE name = $nameSql;")
    $sqlStatements.Add(
        "INSERT INTO nc_biz_templates (name, file_type, file_data, categories, config_json, sort_order, created_at) " +
        "VALUES ($nameSql, '$fileType', decode('$hex','hex'), $catSql, $configSql::jsonb, $($e.SortOrder), NOW());")
}

$sqlText = ($sqlStatements -join "`r`n")

if ($DryRun) {
    Write-Step "DRY RUN（未连库）"
    $preview = Join-Path $env:TEMP "import_templates_preview.sql"
    [IO.File]::WriteAllText($preview, $sqlText, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "SQL 预览已写: $preview"
    Write-Host "共 $($entries.Count) 个模板，$($sqlStatements.Count) 条语句。"
    exit 0
}

# ── 执行 ──
if (-not (Test-Path -LiteralPath $PsqlPath)) { throw "找不到 psql: $PsqlPath（用 -PsqlPath 指定）" }

$password = $env:NEWCOSMOS_DB_PASSWORD
if ([string]::IsNullOrEmpty($password)) {
    $secure = Read-Host "请输入数据库密码（NEWCOSMOS_DB_PASSWORD 未设置）" -AsSecureString
    $password = (New-Object System.Net.NetworkCredential('', $secure)).Password
}
if ([string]::IsNullOrEmpty($password)) { throw "数据库密码为空" }

Write-Step "写入数据库 ${DbHost}:${DbPort}/$DbName（$($entries.Count) 个模板）"
$sqlFile = Join-Path $env:TEMP ("import_templates_{0}.sql" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
[IO.File]::WriteAllText($sqlFile, $sqlText, (New-Object System.Text.UTF8Encoding($false)))

$oldPwd = $env:PGPASSWORD
$oldEnc = $env:PGCLIENTENCODING
$env:PGPASSWORD = $password
$env:PGCLIENTENCODING = "UTF8"
try {
    & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -w -v ON_ERROR_STOP=1 -f $sqlFile
    if ($LASTEXITCODE -ne 0) { throw "psql 执行失败（退出码 $LASTEXITCODE）" }
}
finally {
    if ($null -eq $oldPwd) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue } else { $env:PGPASSWORD = $oldPwd }
    if ($null -eq $oldEnc) { Remove-Item Env:\PGCLIENTENCODING -ErrorAction SilentlyContinue } else { $env:PGCLIENTENCODING = $oldEnc }
}

Write-Host ""
Write-Host "导入完成：$($entries.Count) 个模板" -ForegroundColor Green
Write-Host "SQL 文件: $sqlFile"
