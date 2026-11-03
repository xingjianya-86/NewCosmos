<#
  数据库迁移对账脚本（2026-11 引入，批次6 现代化检查机制）
  解决 AGENTS §2 "手写迁移 docs\migrations\*.sql 需人工执行，升级时易漏"的检查缺口：
    docs/migrations/*.sql（文件清单） ↔ nc_sys_migrations（已执行登记） diff 出未应用清单。

  工作流（人工执行纪律不变，脚本只做对账）：
    1) 首次启用登记表：psql -f deploy\migrations_registry_init.sql
    2) 执行迁移：psql -f docs\migrations\xxx.sql（人工审阅）
    3) 登记：powershell -File deploy\check_migrations.ps1 -Mark 20261101_xxx.sql
    4) 日常检查：powershell -File deploy\check_migrations.ps1        # 未应用清单 + 非零退出码
    5) 结构核对（历史迁移补登记）：powershell -File deploy\check_migrations.ps1 -Verify
       —— 解析 pending 文件中的【幂等结构断言】（CREATE TABLE IF NOT EXISTS / ADD COLUMN IF NOT EXISTS /
          CREATE INDEX IF NOT EXISTS / DROP COLUMN），逐条对照 information_schema/pg_catalog，
          全部成立 → 结构已生效 → 自动登记（applied_by='verify'）。
          含数据操作（UPDATE/DELETE/INSERT/TRUNCATE/DO $$ 块）的文件不自动判定，仍列 pending 请人工审阅。

  凭据：优先环境变量 NEWCOSMOS_DB_PASSWORD；回退 config\database.ini 的 Password=（enc: 无法解密时会提示）。
  连接参数同样读 database.ini（Host/Port/DatabaseName/Username）。
#>
[CmdletBinding()]
param(
    [string]$Mark = '',   # 登记已执行的迁移文件名（如 20261101_recovery_code.sql）
    [switch]$Verify       # 对 pending 迁移做结构核对，全部断言成立的自动登记
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# ── 1. 读 database.ini 连接参数 ──
$iniPath = Join-Path $root 'config\database.ini'
if (-not (Test-Path $iniPath)) { Write-Host "未找到 $iniPath" -ForegroundColor Red; exit 2 }
$ini = @{}
Get-Content $iniPath -Encoding UTF8 | ForEach-Object {
    if ($_ -match '^\s*([^#;=\s][^=]*)=(.*)$') { $ini[$Matches[1].Trim()] = $Matches[2].Trim() }
}
$host_ = $ini['Host']; $port = $ini['Port']; $db = $ini['DatabaseName']; $user = $ini['Username']
$psql = if (Test-Path 'C:\pgsql\bin\psql.exe') { 'C:\pgsql\bin\psql.exe' } else { 'psql' }
$env:PGPASSWORD = $env:NEWCOSMOS_DB_PASSWORD
if (-not $env:PGPASSWORD) {
    $pwdRaw = $ini['Password']
    if ($pwdRaw -and -not $pwdRaw.StartsWith('enc:')) { $env:PGPASSWORD = $pwdRaw }
    else { Write-Host "提示: 未设置 NEWCOSMOS_DB_PASSWORD 且 ini 密码为 enc: 加密，psql 可能提示输密码" -ForegroundColor Yellow }
}

function Invoke-Psql($sql) {
    $tmp = Join-Path $env:TEMP ("ncmig_{0}.sql" -f ([guid]::NewGuid().ToString('N')))
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        # PS5.1 坑：外部命令写 stderr（NOTICE/WARNING）+ ErrorActionPreference=Stop 会抛 NativeCommandError，
        # 临时降级 Continue 并手动检查退出码。
        $eap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { $out = & $psql -X -w -h $host_ -p $port -U $user -d $db -t -A -F '|' -f $tmp 2>&1 } finally { $ErrorActionPreference = $eap }
        if ($LASTEXITCODE -ne 0) { throw ($out -join [char]10) }
        # 过滤 NOTICE/WARNING 行（如 "already exists, skipping"）
        return @($out | Where-Object { $_ -notmatch '^\s*(NOTICE|WARNING):' })
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

# ── 2. 确保登记表存在 ──
Invoke-Psql @"
CREATE TABLE IF NOT EXISTS nc_sys_migrations (
    id serial PRIMARY KEY,
    file_name varchar(200) NOT NULL UNIQUE,
    applied_at timestamptz NOT NULL DEFAULT now(),
    applied_by varchar(100)
);
"@ | Out-Null

# ── 3. -Mark：登记已执行 ──
if ($Mark) {
    $safe = $Mark.Replace("'", "''")
    Invoke-Psql "INSERT INTO nc_sys_migrations (file_name, applied_by) VALUES ('$safe', 'check_migrations.ps1') ON CONFLICT (file_name) DO NOTHING;" | Out-Null
    Write-Host "已登记: $Mark" -ForegroundColor Green
    exit 0
}

# ── 4. 对账：文件 vs 登记 ──
$migDir = Join-Path $root 'docs\migrations'
$files = if (Test-Path $migDir) { Get-ChildItem $migDir -Filter *.sql | Select-Object -ExpandProperty Name } else { @() }
$applied = @(Invoke-Psql 'SELECT file_name FROM nc_sys_migrations;' | Where-Object { $_ -and $_.Trim() })

$pending = $files | Where-Object { $applied -notcontains $_ } | Sort-Object
$orphans = $applied | Where-Object { $files -notcontains $_ }

# ── 4b. -Verify：结构核对，全部幂等断言成立的 pending 自动登记 ──
$autoApplied = @()
if ($Verify -and $pending.Count -gt 0) {
    $dataPattern = '(?im)^\s*(UPDATE\s|DELETE\s+FROM|INSERT\s+INTO|TRUNCATE|CREATE\s+TEMP|DO\s+\$)|CREATE\s+TABLE\s+\w+\s+AS\s+SELECT'

    foreach ($f in $pending) {
        $code = ([System.IO.File]::ReadAllText((Join-Path $migDir $f)) -split "`n" |
            Where-Object { $_ -notmatch '^\s*--' }) -join "`n"

        # 含数据操作/过程块 → 不可自动判定（人工审阅纪律）
        if ($code -match $dataPattern) { continue }

        # 提取幂等结构断言（DROP TABLE 不作判据：常与重建配对，目标状态由 CREATE 决定）
        $tables = @([regex]::Matches($code, '(?is)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+"?([a-z_][a-z0-9_]*)"?') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $columns = @([regex]::Matches($code, '(?is)ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?"?([a-z_][a-z0-9_]*)"?[^;]*?ADD\s+COLUMN\s+(?:IF\s+NOT\s+EXISTS\s+)?"?([a-z_][a-z0-9_]*)"?') |
            ForEach-Object { $_.Groups[1].Value + '.' + $_.Groups[2].Value } | Sort-Object -Unique)
        $indexes = @([regex]::Matches($code, '(?is)CREATE\s+(?:UNIQUE\s+)?INDEX\s+IF\s+NOT\s+EXISTS\s+"?([a-z_][a-z0-9_]*)"?') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)

        if (($tables.Count + $columns.Count + $indexes.Count) -eq 0) { continue }  # 无结构断言 → 不判定

        $failed = @()
        foreach ($t in $tables) {
            $q = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='$t'"
            if ((Invoke-Psql $q) -notmatch '^[1-9]') { $failed += "表不存在: $t" }
        }
        foreach ($c in $columns) {
            $tbl, $col = $c -split '\.', 2
            $q = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='$tbl' AND column_name='$col'"
            if ((Invoke-Psql $q) -notmatch '^[1-9]') { $failed += "列不存在: $c" }
        }
        foreach ($ix in $indexes) {
            $q = "SELECT COUNT(*) FROM pg_indexes WHERE schemaname='public' AND indexname='$ix'"
            if ((Invoke-Psql $q) -notmatch '^[1-9]') { $failed += "索引不存在: $ix" }
        }

        if ($failed.Count -eq 0) {
            $safe = $f.Replace("'", "''")
            Invoke-Psql "INSERT INTO nc_sys_migrations (file_name, applied_by) VALUES ('$safe', 'verify') ON CONFLICT (file_name) DO NOTHING;" | Out-Null
            $autoApplied += $f
            Write-Host "  [v] $f  (结构断言全部成立，已自动登记)" -ForegroundColor Green
        } else {
            $failed | ForEach-Object { Write-Host "  [x] $f :: $_" -ForegroundColor Red }
        }
    }

    if ($autoApplied.Count -gt 0) {
        # 重算 pending（自动登记已生效）
        $applied = @(Invoke-Psql 'SELECT file_name FROM nc_sys_migrations;' | Where-Object { $_ -and $_.Trim() })
        $pending = $files | Where-Object { $applied -notcontains $_ } | Sort-Object
    }
}

Write-Host ""
Write-Host "==== 迁移对账 ($(Get-Date -Format 'yyyy-MM-dd HH:mm')) ====" -ForegroundColor Cyan
Write-Host ("迁移文件总数: {0} / 已登记: {1}" -f $files.Count, ($files.Count - $pending.Count))

if ($pending.Count -gt 0) {
    Write-Host ""
    Write-Host "未应用迁移（需人工审阅后执行 psql -f，再 -Mark 登记）:" -ForegroundColor Red
    $pending | ForEach-Object { Write-Host "  [ ] $_" -ForegroundColor Red }
}
if ($orphans.Count -gt 0) {
    Write-Host ""
    Write-Host "已登记但文件缺失（文件被改名/删除？）:" -ForegroundColor Yellow
    $orphans | ForEach-Object { Write-Host "  [?] $_" -ForegroundColor Yellow }
}
if ($pending.Count -eq 0 -and $orphans.Count -eq 0) {
    Write-Host "全部迁移已应用，无遗漏。" -ForegroundColor Green
}

Write-Host ""
$reportPath = Join-Path $PSScriptRoot 'migrations_report.txt'
[System.IO.File]::WriteAllLines($reportPath,
    @("检查时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')") +
    @($autoApplied | ForEach-Object { "[VERIFY-APPLIED] $_" }) +
    @($pending | ForEach-Object { "[PENDING] $_" }) +
    @($orphans | ForEach-Object { "[ORPHAN] $_" }),
    [System.Text.UTF8Encoding]::new($true))
Write-Host "报告: $reportPath"

exit $(if ($pending.Count -gt 0) { 1 } else { 0 })
