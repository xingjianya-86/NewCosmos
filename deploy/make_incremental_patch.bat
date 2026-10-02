@echo off
rem ============================================================================
rem  make_incremental_patch.bat  (self-contained: batch launcher + PowerShell body)
rem  Generates the incremental update patch (BsDiff) between the previous stable
rem  installer and the current one, re-signs update.json with schema=2 (patch
rem  block), and optionally uploads both to the update server.
rem
rem  Run:  just double-click, or from a cmd prompt:
rem        C:\NewCosmosCode\deploy\make_incremental_patch.bat
rem
rem  The heavy PatchTool (BsDiff on ~400MB installers) may take many minutes.
rem  Keep the window open.
rem ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "$m=('#PS'+'_BODY'); $l=Get-Content -LiteralPath '%~f0' -Encoding UTF8; $i=[Array]::IndexOf($l,$m); if($i -lt 0){Write-Host 'marker not found'; exit 1}; $b=$l[($i+1)..($l.Count-1)] -join ([char]10); & ([scriptblock]::Create($b)) '%~dp0'"
exit /b %ERRORLEVEL%
#PS_BODY
$ErrorActionPreference = 'Stop'
try {

# ----------------------------- CONFIG (edit if needed) -----------------------
$Version    = '1.1.20261004'                       # new version to release
$Prev       = '1.1.20261003'                       # base version the patch applies to
$Channel    = 'stable'
$ServerHost = ''                                   # ? deploy\deploy.local.ps1 ? $LocalServerHost
$ServerUser = 'root'
$ServerRoot = ''                                   # ? deploy\deploy.local.ps1 ? $LocalServerRoot
$SshKey     = Join-Path $env:USERPROFILE '.ssh\id_ed25519_newcosmos'
$SignKey    = Join-Path $env:USERPROFILE '.newcosmos\update_signing_key.pem'
$VerifyUrl  = ''                                   # ? deploy\deploy.local.ps1 ? $LocalVerifyUrl
# -----------------------------------------------------------------------------

if ($env:PATCH_VALIDATE -eq '1') { Write-Host 'BAT/PS body parsed OK'; exit 0 }

function Step([string]$t) { Write-Host ''; Write-Host "== $t ==" -ForegroundColor Cyan }

$ScriptsDir = ($args[0]).TrimEnd('\')
$Repo       = Split-Path -Parent $ScriptsDir

# local deploy config (gitignored): deploy\deploy.local.ps1 provides internal addresses
$deployLocal = Join-Path $ScriptsDir 'deploy.local.ps1'
if (Test-Path -LiteralPath $deployLocal) { . $deployLocal }
if (-not $ServerHost -and $LocalServerHost) { $ServerHost = $LocalServerHost }
if (-not $ServerRoot -and $LocalServerRoot) { $ServerRoot = $LocalServerRoot }
if (-not $VerifyUrl  -and $LocalVerifyUrl)  { $VerifyUrl  = $LocalVerifyUrl }

$newExe       = Join-Path $Repo "publish\NewCosmosSetup_$Version.exe"
$releaseDir   = Join-Path $Repo "publish\release\$Version"
$baseDir      = Join-Path $Repo "publish\release\$Prev"
$baseExe      = Join-Path $baseDir  "NewCosmosSetup_$Prev.exe"
$patchName    = "NewCosmosPatch_${Prev}_${Version}.bin"
$patchPath    = Join-Path $releaseDir $patchName
$manifestPath = Join-Path $releaseDir 'update.json'
$patchTool    = Join-Path $ScriptsDir 'PatchTool\bin\Release\net10.0\PatchTool.dll'
$signTool     = Join-Path $ScriptsDir 'UpdateSigningTool\bin\Release\net10.0\UpdateSigningTool.dll'

Step "Check inputs"
foreach ($f in @($newExe, $SignKey)) { if (-not (Test-Path -LiteralPath $f)) { throw "Missing: $f" } }
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Write-Host "repo    : $Repo"
Write-Host "new exe : $newExe"
Write-Host "base exe: $baseExe"

# --- base installer: download from server if not present locally ---
if (-not (Test-Path -LiteralPath $baseExe)) {
    Step "Download base installer $Prev from server"
    if (-not (Test-Path -LiteralPath $SshKey)) { throw "Base missing locally and no SSH key: $SshKey" }
    New-Item -ItemType Directory -Force -Path $baseDir | Out-Null
    $remote = "$ServerUser@$ServerHost"
    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new "${remote}:$ServerRoot/releases/$Prev/NewCosmosSetup_$Prev.exe" $baseExe
    if ($LASTEXITCODE -ne 0) { throw "base installer download failed" }
}

# --- build PatchTool if its dll is missing ---
if (-not (Test-Path -LiteralPath $patchTool)) {
    Step "Build PatchTool"
    & dotnet build (Join-Path $ScriptsDir 'PatchTool\PatchTool.csproj') -c Release -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "PatchTool build failed" }
}

# --- build UpdateSigningTool if its dll is missing ---
if (-not (Test-Path -LiteralPath $signTool)) {
    Step "Build UpdateSigningTool"
    & dotnet build (Join-Path $ScriptsDir 'UpdateSigningTool\UpdateSigningTool.csproj') -c Release -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "UpdateSigningTool build failed" }
}

# --- create patch ---
Step "Create patch $Prev -> $Version  (may take several minutes)"
$sw = [Diagnostics.Stopwatch]::StartNew()
& dotnet $patchTool create --old $baseExe --new $newExe --out $patchPath
if ($LASTEXITCODE -ne 0) { throw "patch create failed" }
$sw.Stop()
if (-not (Test-Path -LiteralPath $patchPath)) { throw "patch file not produced: $patchPath" }

$exeSize   = (Get-Item -LiteralPath $newExe).Length
$exeSha    = (Get-FileHash -LiteralPath $newExe    -Algorithm SHA256).Hash.ToLowerInvariant()
$patchSize = (Get-Item -LiteralPath $patchPath).Length
$patchSha  = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()

# --- reuse publishedAt / notes from the existing manifest if present ---
if (Test-Path -LiteralPath $manifestPath) {
    $old = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $publishedAt = $old.publishedAt
    $notes       = $old.notes
} else {
    $publishedAt = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')
    $notes       = ''
}

Write-Host ''
Write-Host ("patch   : {0} ({1:N0} bytes, {2:N1} MB) in {3:N0}s" -f $patchName, $patchSize, ($patchSize/1MB), $sw.Elapsed.TotalSeconds) -ForegroundColor Green
Write-Host ("exe sha : {0}" -f $exeSha)
Write-Host ("patch sha: {0}" -f $patchSha)

# --- sign schema=2 manifest ---
Step "Sign schema=2 manifest"
$canonical = "2|$Channel|$Version|$Version|false|$publishedAt|NewCosmosSetup_$Version.exe|$exeSize|$exeSha|$patchSha|$Prev"
$canonFile = Join-Path $releaseDir 'canonical_patch.txt'
[IO.File]::WriteAllText($canonFile, $canonical, (New-Object System.Text.UTF8Encoding($false)))
$sigFile = Join-Path $releaseDir 'update.sig'
& dotnet $signTool sign --key $SignKey --canonical $canonFile --out $sigFile
if ($LASTEXITCODE -ne 0) { throw "manifest signing failed" }
$signature = (Get-Content -LiteralPath $sigFile -Raw).Trim()

$manifest = [ordered]@{
    schema              = 2
    channel             = $Channel
    latestVersion       = $Version
    minSupportedVersion = $Version
    force               = $false
    publishedAt         = $publishedAt
    notes               = $notes
    package             = [ordered]@{ url = "NewCosmosSetup_$Version.exe"; size = $exeSize; sha256 = $exeSha }
    patch               = [ordered]@{ url = $patchName; size = $patchSize; sha256 = $patchSha; baseVersion = $Prev }
    signature           = $signature
}
[IO.File]::WriteAllText($manifestPath, (ConvertTo-Json $manifest -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "manifest: $manifestPath (schema=2 with patch)"

# --- upload ---
$remote = "$ServerUser@$ServerHost"
$manual = @"
Manual upload commands:
  scp -i "$SshKey" "$patchPath" "${remote}:$ServerRoot/stable/"
  scp -i "$SshKey" "$manifestPath" "${remote}:$ServerRoot/stable/"
  scp -i "$SshKey" "$patchPath" "${remote}:$ServerRoot/releases/$Version/"
"@

if ($ServerHost -and (Test-Path -LiteralPath $SshKey)) {
    $ans = Read-Host 'Upload patch + update.json to the server now? (Y/N)'
    if ($ans -eq '' -or $ans -match '^(y|Y)') {
        Step "Upload to $ServerHost$ServerRoot"
        & ssh -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $remote "mkdir -p $ServerRoot/stable $ServerRoot/releases/$Version"
        if ($LASTEXITCODE -ne 0) { throw "ssh failed (check deploy key)" }
        & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $patchPath $manifestPath "${remote}:$ServerRoot/stable/"
        if ($LASTEXITCODE -ne 0) { throw "upload stable failed" }
        & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new $patchPath "${remote}:$ServerRoot/releases/$Version/"
        if ($LASTEXITCODE -ne 0) { Write-Host 'release-archive upload failed (non-fatal)' -ForegroundColor Yellow }

        Step "Verify online $VerifyUrl"
        Start-Sleep -Seconds 2
        try {
            $r = Invoke-WebRequest -Uri $VerifyUrl -TimeoutSec 25 -UseBasicParsing
            $j = $r.Content | ConvertFrom-Json
            Write-Host ("online schema={0} latest={1} patch={2} base={3}" -f $j.schema, $j.latestVersion, $j.patch.url, $j.patch.baseVersion) -ForegroundColor Green
        } catch { Write-Host "verify failed (check manually): $($_.Exception.Message)" -ForegroundColor Yellow }
    } else {
        Write-Host 'Upload skipped.'
        Write-Host $manual
    }
} else {
    Write-Host "ServerHost not configured (deploy\deploy.local.ps1) or SSH key missing; upload manually:" -ForegroundColor Yellow
    Write-Host $manual
}

Write-Host ''
Write-Host 'DONE' -ForegroundColor Green
exit 0
}
catch {
    Write-Host ''
    Write-Host ("ERROR: {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 1
}
