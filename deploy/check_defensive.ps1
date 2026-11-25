<#
  Defensive-code regression check.

  Rules:
    DEF1  empty catch (except OperationCanceledException / best-effort cleanup)
    DEF2  catch returns null/default/empty/false/0 without logging or rethrowing
    DEF3  null-conditional on DI-injected Logger/ServiceProvider/_logger/_db
    DEF4  null checks on required DI services (dialog/dialogService/picker)
    DEF5  context-free log lines ("executing operation")
    DEF6  context-free error/warning log lines ("operation failed" / "warning")

  Usage:
    powershell -File deploy\check_defensive.ps1
    powershell -File deploy\check_defensive.ps1 -ReportOnly

  Exit code: 1 when violations found (0 with -ReportOnly).
#>
[CmdletBinding()]
param(
    [switch]$ReportOnly
)

$root = Split-Path $PSScriptRoot -Parent
$dirs = @('Pages','ViewModels','Services','Components','Helpers','Models','Navigation','Constants','Controls','Converters','Platforms')
$files = Get-ChildItem -Path $dirs -Recurse -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|backup)\\' }

$hits = [System.Collections.Generic.List[object]]::new()
function Add-Hit($cat, $file, $line, $text) {
    $clean = $text.Trim()
    if ($clean.Length -gt 140) { $clean = $clean.Substring(0, 140) }
    $hits.Add([pscustomobject]@{
        Cat  = $cat
        File = $file.FullName.Replace($root + '\', '')
        Line = $line
        Text = $clean
    })
}

foreach ($f in $files) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i]
        if ($t -match '^\s*(///|//|\*)') { continue }

        $isCleanup = $t -match 'File\.Delete|Directory\.Delete|Process\.Kill|_cts\?\.Cancel|_printTcs|_captureTcs'

        # DEF1: empty catch on one line
        if ($t -match 'catch\s*(\([^)]*\))?\s*\{\s*\}') {
            if ($t -notmatch 'OperationCanceledException' -and -not $isCleanup) {
                Add-Hit 'DEF1' $f ($i + 1) $t
            }
        }

        # DEF2: catch -> return default without log/throw
        if ($t -match '^\s*catch\b' -and $t -notmatch 'OperationCanceledException') {
            $windowEnd = [Math]::Min($lines.Count - 1, $i + 8)
            $body = ($lines[$i..$windowEnd] -join "`n")
            $returnsDefault = $body -match 'return\s+(null|default|false|0m?|string\.Empty|Array\.Empty|new\s+(List|Dictionary|HashSet))'
            $hasTrace = $body -match 'Log(Error|Warn|Exception|Debug|Business)|Logger\.|Serilog\.|_log\?\.|_logger\.|throw|Result\.(FromException|Failure)'
            if ($returnsDefault -and -not $hasTrace -and -not $isCleanup) {
                Add-Hit 'DEF2' $f ($i + 1) $t
            }
        }

        # DEF3: null-conditional on non-null DI members
        if ($t -cmatch 'ServiceProvider\?\.' -or
            $t -cmatch '(?<![A-Za-z0-9_])Logger\?\.' -or
            $t -cmatch '(?<![A-Za-z0-9_])_logger\?\.' -or
            $t -cmatch '(?<![A-Za-z0-9_])_db\?\.') {
            Add-Hit 'DEF3' $f ($i + 1) $t
        }

        # DEF4: null checks on required DI services
        if ($t -match 'if\s*\(\s*(dialog|dialogService|picker)\s*!=\s*null\s*\)') {
            Add-Hit 'DEF4' $f ($i + 1) $t
        }

        # DEF5: context-free log line ("executing operation")
        if ($t -match '(LogInfo|Logger\.Info|_logger\.Info)\(\$?"\u6267\u884C\u64CD\u4F5C"' ) {
            Add-Hit 'DEF5' $f ($i + 1) $t
        }

        # DEF6: context-free error/warning log line ("operation failed" / "warning")
        if ($t -match '(LogError|Logger\.Error|_logger\.Error)\(\$?"\u64CD\u4F5C\u5931\u8D25"' -or
            $t -match '(LogWarn|Logger\.Warn|_logger\.Warn)\(\$?"\u8B66\u544A"') {
            Add-Hit 'DEF6' $f ($i + 1) $t
        }
    }
}

if ($hits.Count -eq 0) {
    Write-Output 'No defensive-code violations found.'
    exit 0
}

$hits | Group-Object Cat | ForEach-Object {
    Write-Output ''
    Write-Output ("[{0}] {1} violation(s)" -f $_.Name, $_.Count)
    $_.Group | ForEach-Object {
        Write-Output ("  {0}:{1}: {2}" -f $_.File, $_.Line, $_.Text)
    }
}
Write-Output ''
Write-Output ("Total: {0}" -f $hits.Count)

if ($ReportOnly) { exit 0 }
exit 1
