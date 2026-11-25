<#
  Single entry for all static checks.

  Usage:
    powershell -File deploy\check_all.ps1
    powershell -File deploy\check_all.ps1 -Category SQL
    powershell -File deploy\check_all.ps1 -Category DEF

  Exit code: 1 when any check fails.
#>
[CmdletBinding()]
param(
    [ValidateSet('ALL','SQL','UI','SYNC','LOG','SEC','OUT','TODO','DEF')]
    [string]$Category = 'ALL'
)

$exitCode = 0

if ($Category -eq 'DEF') {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check_defensive.ps1')
    exit $LASTEXITCODE
}

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check_standards.ps1') -Category $Category
if ($LASTEXITCODE -ne 0) { $exitCode = 1 }

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check_defensive.ps1')
if ($LASTEXITCODE -ne 0) { $exitCode = 1 }

exit $exitCode
