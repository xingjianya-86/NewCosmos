<#
  NewCosmos 规范检查脚本（2026-11 引入，批次6 现代化检查机制）
  把 AGENTS §5 禁止事项 / §8 查询规范 / §10 日志纪律变成【可执行检查】：
  输出违规清单（报告式，不强制失败——存量违规分批治理，基线见输出尾部计数）。

  用法：
    powershell -File deploy\check_standards.ps1            # 全量报告
    powershell -File deploy\check_standards.ps1 -Category SQL   # 只查某类

  检查项与 AGENTS 条款对照：
    SQL1  字符串插值进 SQL（§5-1 禁止，只允许 $1..$n 位置参数）
    SQL2  SELECT *（§5-2 宽表禁用；窄表存量见计数）
    SQL3  EXTRACT() 包列用于过滤（§8 索引失效；豁免注释 [索引豁免] 除外）
    UI1   App.CurrentUserId ?? 1 回退管理员（批次1 已修，防回潮）
    SYNC  .Result / .Wait() 阻塞调用（死锁风险）
    LOG1  空 catch 吞异常（§7）
    LOG2  catch 内仅注释"静默处理"（§7）
    SEC   明文密码字面量入代码（§12）
    TODO  TODO/FIXME 残留计数
#>
[CmdletBinding()]
param(
    [ValidateSet('ALL','SQL','UI','SYNC','LOG','SEC','TODO')]
    [string]$Category = 'ALL'
)

$root = Split-Path $PSScriptRoot -Parent
$codeDirs = @('Pages','ViewModels','Services','Components','Helpers','Models','Navigation','Constants','Controls','Converters','Platforms')
$files = Get-ChildItem -Path $codeDirs -Recurse -Include *.cs,*.xaml -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|backup)\\' }

$results = [System.Collections.Generic.List[object]]::new()
function Hit($cat, $file, $line, $text) {
    $script:results.Add([pscustomobject]@{
        Cat = $cat; File = $file.FullName.Replace($root + '\', ''); Line = $line
        Text = $text.Trim().Substring(0, [Math]::Min(120, $text.Trim().Length))
    })
}

foreach ($f in $files) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i]
        if ($t -match '^\s*(///|//)') { continue }   # 注释行不计
        $n = $i + 1

        # SQL1: 字符串插值进 SQL（§5-1）。只报【值插值】风险；以下为受控标识符/片段插值（豁免）：
        #   ① 常量列清单（ApplicationColumns/WideTableColumns/SelectColumns/ApplicationFillColumns 等，AGENTS §8 鼓励的单点列清单模式）
        #   ② SqlConditionBuilder 产出的 where/conditions 片段（$n 位置参数构建器）
        #   ③ 白名单表名变量（TableNameValidator/ImportTemplateCatalog/SchemaService 受控，§8 允许动态表名过白名单）
        #   ④ unionSql：ElderlyCategoryService 身份比对合批片段，由服务内硬编码 probes 常量表名+prio
        #      生成，无外部输入（白名单背书见该类 probes 定义处注释）。
        #   ⑤ cols：FamilyMemberService.AddRangeAsync 的本地 const 列清单（50 列硬编码常量，
        #      与 ApplicationColumns 同模式）；values 由硬编码 rowTpl + 请求对象字段生成，无外部 SQL 输入。
        # 动态标识符插值仍应在调用点有白名单背书注释（本项靠评审把关）。
        $sql1Exempt = '\{(ApplicationColumns|WideTableColumns|SelectColumns|ApplicationFillColumns|where|conditions|valuesClause|updateSets|colList|cols|fromClause|filter|def\.TableName|definition\.TableName|def\.PersonsTable|def\.FamiliesTable|probe\.Table|TableName|PersonTableName|FamilyTableName|FamiliesTable|PersonsTable|tableName|table|detailSb|unionSql|unionParts|values|t)[.A-Za-z0-9_]*\}'
        if ($Category -in 'ALL','SQL' -and $t -match '\$"?(SELECT|INSERT|UPDATE|DELETE|WHERE|FROM)\s' -and $t -match '\{[^$]' -and $t -notmatch $sql1Exempt) {
            Hit 'SQL1' $f $n $t
        }
        # SQL2: SELECT *（宽表禁用；窄表≤15列存量豁免——批注见 NarrowTables，
        #       宽表已于 2026-11 批次6 治理为 Constants\WideTableColumns 显式列清单。
        #       只查 nc_ 前缀物理表：CTE 别名/unnest 函数展开/WMI 查询（Win32_Printer）非物理表访问）
        $NarrowTables = @(
            'nc_biz_temp_relief_accidents','nc_config_standards','nc_biz_temp_relief_diseases',
            'nc_biz_household_surveys','nc_biz_subsidies','nc_biz_land_confirmation_records',
            'nc_biz_land_registrations','nc_biz_monthly_meetings','nc_biz_machineries',
            'nc_biz_archives','nc_biz_financial_assets','nc_biz_properties','nc_view_dict_items',
            'nc_biz_near_relative_staffs','nc_biz_land_confirmation_persons','nc_biz_temp_relief_educations',
            'nc_biz_temp_relief_members','nc_biz_transfer_incomes','nc_biz_vehicles',
            'nc_config_system_standards','nc_view_dict_categories','nc_biz_land_confirmations',
            'nc_biz_property_incomes','nc_biz_other_incomes','nc_biz_rigid_expenditures',
            'nc_biz_breeding_incomes','nc_config_destitute_standards','nc_biz_archive_files',
            'nc_config_income_standards','nc_config_subsidy_standards'
        )
        if ($Category -in 'ALL','SQL' -and $t -match 'SELECT\s+\*\s+FROM\s+(nc_[a-z0-9_]*)' -and $NarrowTables -notcontains $Matches[1]) {
            Hit 'SQL2' $f $n $t
        }
        # SQL2b: SELECT * FROM {插值} —— 表名非字面量时上面的正则漏检（2026-11 实测
        # ElderlyApplicationService "$""SELECT * FROM {fromClause}""" 逃逸；插值表无法白名单，一律命中）
        if ($Category -in 'ALL','SQL' -and $t -match 'SELECT\s+\*\s+FROM\s+\{') {
            Hit 'SQL2' $f $n $t
        }
        # SQL3: EXTRACT( 用于 WHERE/AND 过滤且无豁免注释
        if ($Category -in 'ALL','SQL' -and $t -match 'EXTRACT\s*\(' -and ($t -match 'WHERE|AND\s+EXTRACT') -and ($lines[$i+1] -notmatch '索引豁免' -and $t -notmatch '索引豁免')) {
            Hit 'SQL3' $f $n $t
        }

        # UI1: 管理员回退
        if ($Category -in 'ALL','UI' -and $t -match 'CurrentUserId\s*\?\?\s*1\b') {
            Hit 'UI1' $f $n $t
        }

        # SYNC: 真阻塞等待（.Result 属性名误报太多——TaskCompletionSource/popup.Result 等读取引用不阻塞，只查显式阻塞 API；
        # 豁免：GetAwaiter().GetResult() 属"同步外观包装异步"合法模式——ConfigService 全同步 API 包装文件流读取、
        # OfficeComWorker 同步外观包 COM 队列化异步（内部已是线程安全队列，无 UI 上下文死锁风险））
        if ($Category -in 'ALL','SYNC' -and $t -match '\.Wait\(\)|\.GetAwaiter\(\)\.GetResult\(\)' -and $t -notmatch 'GetAwaiter\(\)\.GetResult\(\)') {
            Hit 'SYNC' $f $n $t
        }

        # LOG1: 空 catch {}（同块内；豁免：取消即退出的合法模式 / 崩溃记录器内的防御性静默）
        if ($Category -in 'ALL','LOG' -and $t -match 'catch\s*(\([^)]*\))?\s*$' -and $t -notmatch 'OperationCanceledException|TaskCanceledException') {
            # 下一非空行是 { 再下一行是 } → 空 catch
            $j = $i + 1
            while ($j -lt $lines.Count -and $lines[$j].Trim() -eq '') { $j++ }
            if ($j -lt $lines.Count -and $lines[$j].Trim() -eq '{') {
                $k = $j + 1
                while ($k -lt $lines.Count -and $lines[$k].Trim() -eq '') { $k++ }
                if ($k -lt $lines.Count -and ($lines[$k].Trim() -eq '}' -or $lines[$k].Trim() -match '^\}\s*(//.*)?$')) {
                    Hit 'LOG1' $f $n $t
                }
            }
        }
        # LOG2: 静默处理 注释型吞异常
        if ($Category -in 'ALL','LOG' -and $t -match '静默处理|静默吞|吞掉异常') {
            Hit 'LOG2' $f $n $t
        }

        # SEC: 明文密码字面量入代码（排除大写下划线的错误码/权限码常量，如 "INVALID_PASSWORD"）
        if ($Category -in 'ALL','SEC' -and $t -match '(Password|Pwd)\s*=\s*"[^"$]{6,}"' -and $t -notmatch 'enc:|\{|\$1|"[A-Z0-9_]{6,}"') {
            Hit 'SEC' $f $n $t
        }

        # TODO
        if ($Category -in 'ALL','TODO' -and $t -match 'TODO|FIXME') {
            Hit 'TODO' $f $n $t
        }
    }
}

Write-Host ""
Write-Host "==== NewCosmos 规范检查报告 ($(Get-Date -Format 'yyyy-MM-dd HH:mm')) ====" -ForegroundColor Cyan
$grouped = $results | Group-Object Cat | Sort-Object Name
foreach ($g in $grouped) {
    $color = if ($g.Name -in @('SEC','UI1','SQL1')) { 'Red' } elseif ($g.Count -gt 10) { 'Yellow' } else { 'Gray' }
    Write-Host ("{0,-5} {1,4} 处" -f $g.Name, $g.Count) -ForegroundColor $color
}
Write-Host ("合计 {0} 处" -f $results.Count)

# 明细写 UTF-8 文件（控制台代码页不一致时中文乱码，报告文件为准）
$reportPath = Join-Path $PSScriptRoot 'standards_report.txt'
$detail = @($results | ForEach-Object { "[{0}] {1}:{2}  {3}" -f $_.Cat, $_.File, $_.Line, $_.Text })
[System.IO.File]::WriteAllLines($reportPath, $detail, [System.Text.UTF8Encoding]::new($true))
Write-Host "明细已写入: $reportPath"

Write-Host ""
Write-Host "基线说明：存量违规按批次治理；本脚本用于防回潮与新增代码把关（AGENTS §5/§7/§8/§10/§12）。"
# 退出码恒 0（报告式）；接入 CI 后可按类别阈值改非零退出
exit 0
