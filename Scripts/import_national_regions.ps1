# ============================================================================
# 全国行政区划导入（按码合并，保留现有 id）
#
# 数据源：adyliu/china_area（国家统计局「统计用区划代码和城乡划分代码」2023-06-30 版）
#   https://github.com/adyliu/china_area
#   直链：https://raw.githubusercontent.com/adyliu/china_area/master/area_code_2024.csv.gz
#   CSV：code(12位) , name , level(1..5) , pcode , category
#
# 为什么按码合并而不是全量重建：
#   库中 towns 1,703 / villages 14,451 的 code 填充率 100%，且业务表已引用现有 id
#   （orgs.town_id=柳树镇11197、apps.hukou_village_id=4 个村）→ TRUNCATE 会打成孤儿外键。
#
# 用法：
#   $env:NEWCOSMOS_DB_PASSWORD='***'
#   .\Scripts\import_national_regions.ps1 -DryRun              # 只装载+统计+回滚，不写库
#   .\Scripts\import_national_regions.ps1 -Force               # 正式导入
#   .\Scripts\import_national_regions.ps1 -CsvPath D:\xx.csv   # 指定已下载的 CSV
#
# 安全：
#   · 默认拒绝执行，必须 -Force（防误跑生产库）；
#   · 只 UPDATE/INSERT，无 DROP/TRUNCATE；重复执行幂等；
#   · 密码只读环境变量 NEWCOSMOS_DB_PASSWORD（AGENTS §12）。
# ============================================================================

[CmdletBinding()]
param(
    [string]$CsvPath = "",
    [switch]$Force,
    [switch]$DryRun,
    [string]$DbHost = "",
    [int]$DbPort = 0,
    [string]$DbName = "",
    [string]$DbUser = "",
    [string]$PsqlPath = "C:\pgsql\bin\psql.exe",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

$ScriptsDir = $PSScriptRoot
if ($RepoRoot -eq "") { $RepoRoot = Split-Path -Parent $ScriptsDir }
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

function Write-Step([string]$t) { Write-Host ""; Write-Host "== $t ==" -ForegroundColor Cyan }

# ── CSV 就位检查（不存在则给直链）──
if ($CsvPath -eq "") { $CsvPath = Join-Path $RepoRoot "Scripts\regions_data\area_code_2024.csv" }
if (-not (Test-Path -LiteralPath $CsvPath)) {
    $gz = "$CsvPath.gz"
    if (-not (Test-Path -LiteralPath $gz)) {
        Write-Host "未找到数据文件: $CsvPath" -ForegroundColor Yellow
        Write-Host "请下载（约 5.4MB）："
        Write-Host "  https://raw.githubusercontent.com/adyliu/china_area/master/area_code_2024.csv.gz"
        Write-Host "解压后放到上面路径，或用 -CsvPath 指定。" -ForegroundColor Yellow
        throw "CSV 数据文件缺失"
    }
    Write-Step "解压 $gz"
    $in = [IO.File]::OpenRead($gz)
    $gzStream = New-Object IO.Compression.GzipStream($in, [IO.Compression.CompressionMode]::Decompress)
    $out = [IO.File]::Create($CsvPath)
    $gzStream.CopyTo($out); $out.Close(); $gzStream.Close(); $in.Close()
}
# psql 的 \copy 走客户端路径：统一正斜杠，避开反斜杠转义问题
$CsvPath = (Resolve-Path -LiteralPath $CsvPath).Path.Replace('\', '/')
Write-Step "数据文件: $CsvPath"

# ── 连接参数（缺省从 config\database.ini 读）──
$iniPath = Join-Path $RepoRoot "config\database.ini"
if (Test-Path -LiteralPath $iniPath) {
    $ini = @{}
    foreach ($line in Get-Content -LiteralPath $iniPath -Encoding UTF8) {
        if ($line -match '^\s*([A-Za-z_]+)\s*=\s*(.*?)\s*$') { $ini[$Matches[1]] = $Matches[2] }
    }
    if ($DbHost -eq "" -and $ini.ContainsKey("Host")) { $DbHost = $ini["Host"] }
    if ($DbPort -eq 0 -and $ini.ContainsKey("Port")) { $DbPort = [int]$ini["Port"] }
    if ($DbName -eq "" -and $ini.ContainsKey("DatabaseName")) { $DbName = $ini["DatabaseName"] }
    if ($DbUser -eq "" -and $ini.ContainsKey("Username")) { $DbUser = $ini["Username"] }
}
if ($DbHost -eq "") { $DbHost = "127.0.0.1" }
if ($DbPort -eq 0) { $DbPort = 5432 }
if ($DbName -eq "") { $DbName = "new_cosmos" }
if ($DbUser -eq "") { $DbUser = "new_cosmos" }

if (-not (Test-Path -LiteralPath $PsqlPath)) { throw "找不到 psql: $PsqlPath（用 -PsqlPath 指定）" }

if (-not $Force -and -not $DryRun) {
    throw "安全阀：本脚本会写入行政区划表。确认后请加 -Force 正式执行（或先用 -DryRun 预演）。"
}

$password = $env:NEWCOSMOS_DB_PASSWORD
if ([string]::IsNullOrEmpty($password)) {
    $secure = Read-Host "请输入数据库密码（NEWCOSMOS_DB_PASSWORD 未设置）" -AsSecureString
    $password = (New-Object System.Net.NetworkCredential('', $secure)).Password
}
if ([string]::IsNullOrEmpty($password)) { throw "数据库密码为空" }

$oldPwd = $env:PGPASSWORD
$oldEnc = $env:PGCLIENTENCODING
$env:PGPASSWORD = $password
$env:PGCLIENTENCODING = "UTF8"

try {
    Write-Step "连接 ${DbHost}:${DbPort}/$DbName（用户 $DbUser）"

    # ── 生成 SQL（把 CSV 路径写进临时文件；模板见 Scripts\sql\import_national_regions.sql）──
    $template = Join-Path $ScriptsDir "sql\import_national_regions.sql"
    if (-not (Test-Path -LiteralPath $template)) { throw "找不到 SQL 模板: $template" }

    $sqlText = [IO.File]::ReadAllText($template, [Text.UTF8Encoding]::new($false))
    $sqlText = $sqlText.Replace("__CSV_PATH__", $CsvPath)

    if ($DryRun) {
        # 预演：整段事务末尾改为 ROLLBACK（只装载临时表 + 统计，不写任何业务表）
        $sqlText = $sqlText -replace '(?m)^COMMIT;\s*$', 'ROLLBACK;'
    }

    $sqlFile = Join-Path $env:TEMP ("import_regions_{0}.sql" -f (Get-Date -Format 'yyyyMMddHHmmss'))
    [IO.File]::WriteAllText($sqlFile, $sqlText, [Text.UTF8Encoding]::new($false))
    Write-Step $(if ($DryRun) { "预演（将回滚）: $sqlFile" } else { "执行: $sqlFile" })

    & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -w -X -v ON_ERROR_STOP=1 -f $sqlFile
    if ($LASTEXITCODE -ne 0) { throw "psql 执行失败（退出码 $LASTEXITCODE）" }

    Write-Host ""
    if ($DryRun) {
        Write-Host "预演完成（已回滚，未写库）。请核对上面「各级条数」与「孤儿行」输出。" -ForegroundColor Green
        Write-Host "确认无误后执行：.\Scripts\import_national_regions.ps1 -Force" -ForegroundColor Yellow
    } else {
        Write-Host "导入完成。请核对末尾「各级条数」与「孤儿行」（孤儿应为 0）。" -ForegroundColor Green
    }
}
finally {
    if ($null -eq $oldPwd) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue } else { $env:PGPASSWORD = $oldPwd }
    if ($null -eq $oldEnc) { Remove-Item Env:\PGCLIENTENCODING -ErrorAction SilentlyContinue } else { $env:PGCLIENTENCODING = $oldEnc }
}
