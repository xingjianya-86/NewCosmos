# ============================================================================
# 死亡情况说明 · 演示种子数据（造数 / 清除）
#
# 用途：开发与演示库缺少"按村组织的死亡申报"数据。本脚本在指定村上造出可冒烟的数据集
#       （重复申报 / 缺证明编号 / 已核实 / 未申报候选 / 作废），身份聚合复用库内真实在保人员。
#
# 用法：
#   $env:NEWCOSMOS_DB_PASSWORD='***'                 # 未设置则交互输入
#   .\Scripts\seed_demo_death_statement.ps1 -VillageName 柳树村 -Force     # 造数
#   .\Scripts\seed_demo_death_statement.ps1 -VillageName 柳树村 -Force -DryRun   # 只看计划
#   .\Scripts\seed_demo_death_statement.ps1 -VillageName 柳树村 -Clean     # 清除演示行
#   .\Scripts\seed_demo_death_statement.ps1 -ListVillages                  # 列出可用村名
#
# 安全：
#   · 默认拒绝执行，必须显式 -Force（防止误跑生产库）；
#   · 演示行统一 created_by='DEMO' 标记，-Clean 只删这些行；
#   · 密码读环境变量 NEWCOSMOS_DB_PASSWORD，禁止明文写进脚本（AGENTS §12）。
#
# 依赖：本机 psql（默认 C:\pgsql\bin\psql.exe）；连接参数缺省从 config\database.ini 读取。
# ============================================================================

[CmdletBinding(DefaultParameterSetName = "Seed")]
param(
    [string]$VillageName = "",
    [int]$VillageId = 0,
    [switch]$Force,
    [switch]$Clean,
    [switch]$ListVillages,
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

# ── 连接参数：缺省从 config\database.ini 读（按 key 匹配、忽略节名）──
$iniPath = Join-Path $RepoRoot "config\database.ini"
if (Test-Path -LiteralPath $iniPath) {
    $ini = @{}
    foreach ($line in Get-Content -LiteralPath $iniPath -Encoding UTF8) {
        if ($line -match '^\s*([A-Za-z_]+)\s*=\s*(.*?)\s*$') {
            $ini[$Matches[1]] = $Matches[2]
        }
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

function Invoke-PsqlScalar([string]$sql) {
    $out = & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -w -t -A -X -c $sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql 执行失败: $out" }
    return ($out | Out-String).Trim()
}

try {
    Write-Step "连接 ${DbHost}:${DbPort}/$DbName（用户 $DbUser）"

    # ── 列出可用村名 ──
    if ($ListVillages) {
        $sql = "SELECT id || '  ' || village_name || '  （镇ID ' || COALESCE(town_id::text,'-') || '）' FROM nc_regions_villages WHERE is_active ORDER BY id LIMIT 50"
        Write-Host (Invoke-PsqlScalar $sql)
        return
    }

    if ($VillageName -eq "" -and $VillageId -eq 0) {
        throw "请用 -VillageName 指定村/社区（可先用 -ListVillages 查看），或用 -VillageId 直接指定ID"
    }

    # ── 解析 village_id（按名称：精确或后缀归一化后相等）──
    if ($VillageId -eq 0) {
        $escaped = $VillageName.Replace("'", "''")
        $pattern = "(村民委员会|居民委员会|村公所|居委会|村委会|委员会|社区|村)+$"
        $sql = "SELECT id FROM nc_regions_villages WHERE is_active AND (village_name = '$escaped' OR regexp_replace(village_name, '$pattern', '') = regexp_replace('$escaped', '$pattern', '')) ORDER BY id LIMIT 1"
        $resolved = Invoke-PsqlScalar $sql
        if ($resolved -eq "") {
            Write-Host "未找到村/社区：$VillageName —— 可用村名（前 50）：" -ForegroundColor Yellow
            Write-Host (Invoke-PsqlScalar "SELECT id || '  ' || village_name FROM nc_regions_villages WHERE is_active ORDER BY id LIMIT 50")
            throw "解析失败"
        }
        $VillageId = [int]$resolved
    }
    Write-Host "目标村 ID = $VillageId" -ForegroundColor Cyan

    # ── 清除演示行 ──
    if ($Clean) {
        if ($DryRun) { Write-Host "[DryRun] 将删除 created_by='DEMO' 的申报与死亡登记演示行"; return }
        $before = Invoke-PsqlScalar "SELECT (SELECT COUNT(*) FROM nc_biz_death_declarations WHERE created_by='DEMO') || ' 条申报 / ' || (SELECT COUNT(*) FROM nc_biz_death_records WHERE created_by='DEMO') || ' 条死亡登记'"
        Write-Step "清除前：$before"
        $del = "DELETE FROM nc_biz_death_declarations WHERE created_by='DEMO'; DELETE FROM nc_biz_death_records WHERE created_by='DEMO';"
        $null = Invoke-PsqlScalar $del
        Write-Host "演示数据已清除" -ForegroundColor Green
        return
    }

    if (-not $Force) {
        throw "安全阀：演示造数会写入数据库，请显式加 -Force 执行（生产库请勿使用）。可先加 -DryRun 查看计划。"
    }

    # ── 造数前的可用性检查：本村必须至少有 1 名在保人员 ──
    $pattern = "(村民委员会|居民委员会|村公所|居委会|村委会|委员会|社区|村)+$"
    $countSql = @"
SELECT COUNT(*) FROM nc_biz_applications a
WHERE a.deleted_at IS NULL AND a.applicant_id_card IS NOT NULL AND a.applicant_id_card <> ''
  AND (a.hukou_village_id = $VillageId OR a.village_id = $VillageId
       OR regexp_replace(COALESCE(a.community,''), '$pattern','') =
          regexp_replace(COALESCE((SELECT village_name FROM nc_regions_villages WHERE id = $VillageId),''), '$pattern',''))
"@
    $personCount = Invoke-PsqlScalar $countSql
    Write-Host "本村可用在保户主：$personCount 名"
    if ([int]$personCount -eq 0) {
        throw "本村无在保人员，无法造演示数据（演示身份聚合依赖真实在保档案）"
    }

    $sqlFile = Join-Path $ScriptsDir "sql\seed_demo_death_statement.sql"
    if (-not (Test-Path -LiteralPath $sqlFile)) { throw "找不到种子 SQL: $sqlFile" }

    if ($DryRun) {
        Write-Host "[DryRun] 将执行: psql -f $sqlFile -v village_id=$VillageId" -ForegroundColor Yellow
        return
    }

    Write-Step "写入演示数据（村 $VillageId）"
    & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -w -X -v ON_ERROR_STOP=1 -v village_id=$VillageId -f $sqlFile
    if ($LASTEXITCODE -ne 0) { throw "psql 执行失败（退出码 $LASTEXITCODE）" }

    Write-Host ""
    Write-Host "演示数据已写入。冒烟路径：" -ForegroundColor Green
    Write-Host "  1) 侧栏「核对·档案 → 死亡情况说明」或首页横幅进入"
    Write-Host "  2) 搜索留空点「搜索」→ 应看到 3 名演示死者（已申报/未申报/作废需勾「含作废申报」）"
    Write-Host "  3) 选中重复申报者 → 数据提醒应出现「疑似重复申报」「缺死亡证明编号」"
    Write-Host "  4) 选中「未申报」者 → 提醒「未采集确切时刻」，可手工提交申报"
    Write-Host "  5) 身份清单显示该在保人员的真实身份（低保/高龄/临救等，命中什么显示什么）"
    Write-Host "  6) 清除：.\Scripts\seed_demo_death_statement.ps1 -VillageName <村名> -Clean" -ForegroundColor Yellow
}
finally {
    if ($null -eq $oldPwd) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue } else { $env:PGPASSWORD = $oldPwd }
    if ($null -eq $oldEnc) { Remove-Item Env:\PGCLIENTENCODING -ErrorAction SilentlyContinue } else { $env:PGCLIENTENCODING = $oldEnc }
}
