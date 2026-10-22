<#
.SYNOPSIS
    Story 3 of docs/AGENT_INTEGRATION.md: a script drives the headless agent HTTP API of `Cutlass.Agent.Mcp serve`.
.DESCRIPTION
    Generates a lavfi clip, starts `Cutlass.Agent.Mcp serve` on a new project (--create) with the work folder as its
    read and write folder, and then talks to it with Invoke-RestMethod only:
      status, lock/acquire, media/import, edit/place, exports (H.264), exports/{id}?waitMs polling, lock/release,
      and finally lock/acquire again plus /v1/shutdown {save: true} (shutdown is a locked call).
    It then checks that the server process exited, that the project file now holds the clip, and the export with
    ffprobe. No Avalonia, window or audio device is involved. Exit code 0 means every step passed.

    Needs ffmpeg and ffprobe on PATH and a built or published Cutlass.Agent.Mcp. No other process may serve agents
    for this user (a running Cutlass with agent connections on, or another serve), because there is one server per
    user (section 4.1).
.EXAMPLE
    powershell -File build/publish.ps1
    powershell -File build/examples/agent-serve-demo.ps1
.EXAMPLE
    pwsh build/examples/agent-serve-demo.ps1 -McpExe src/Cutlass.Agent.Mcp/bin/Debug/net10.0/Cutlass.Agent.Mcp.exe -Port 47410
#>
[CmdletBinding()]
param(
    [string]$McpExe,
    [string]$WorkDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'cutlass-serve-demo'),
    [int]$Port = 47399
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$onWindows = [Environment]::OSVersion.Platform -eq 'Win32NT'
if (-not $McpExe) {
    $rid = if ($onWindows) { 'win-x64' } else { 'linux-x64' }
    $name = if ($onWindows) { 'Cutlass.Agent.Mcp.exe' } else { 'Cutlass.Agent.Mcp' }
    $McpExe = Join-Path $repo "artifacts/publish/$rid/$name"
}
if (-not (Test-Path -LiteralPath $McpExe)) { throw "Cutlass.Agent.Mcp not found at $McpExe. Run build/publish.ps1 first or pass -McpExe." }
$McpExe = (Resolve-Path -LiteralPath $McpExe).Path

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$WorkDir = (Resolve-Path -LiteralPath $WorkDir).Path
$clip = Join-Path $WorkDir 'demo-clip.mp4'
$project = Join-Path $WorkDir 'demo.cutlass'
$export = Join-Path $WorkDir 'demo-export.mp4'
$stdoutFile = Join-Path $WorkDir 'serve.stdout.txt'
$stderrFile = Join-Path $WorkDir 'serve.stderr.txt'
foreach ($old in @($project, "$project.user", $export, $stdoutFile, $stderrFile)) {
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
}

function Step([string]$Text) { Write-Host "demo: $Text" }

# 1. A 4 s lavfi clip with sound (120 frames at 30 fps).
& ffmpeg -hide_banner -v error -y -f lavfi -i 'testsrc2=s=640x360:r=30:d=4' -f lavfi -i 'sine=f=440:r=48000:d=4' `
    -map 0:v -map 1:a -c:v mpeg4 -q:v 3 -c:a aac -ac 2 -shortest $clip
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg could not generate the demo clip.' }
Step "generated $clip"

# 2. Start serve in the background. Windows PowerShell 5.1 does not quote Start-Process arguments, so quote them.
$serveArgs = @('serve', '--project', $project, '--create', '--port', "$Port", '--allow-read', $WorkDir, '--allow-write', $WorkDir) |
    ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
$server = Start-Process -FilePath $McpExe -ArgumentList $serveArgs -PassThru -NoNewWindow `
    -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
# Touch the handle now: without it Windows PowerShell 5.1 reports no ExitCode for a -NoNewWindow process.
$null = $server.Handle
Step "started serve (pid $($server.Id))"

$baseUrl = $null
$tokenFile = $null
$deadline = (Get-Date).AddSeconds(60)
while (-not $baseUrl) {
    if ($server.HasExited) { throw "serve exited with $($server.ExitCode): $(Get-Content -Raw -LiteralPath $stderrFile)" }
    if ((Get-Date) -gt $deadline) { $server.Kill(); throw 'serve printed no baseUrl within 60 s.' }
    Start-Sleep -Milliseconds 200
    $lines = if (Test-Path -LiteralPath $stdoutFile) { Get-Content -LiteralPath $stdoutFile } else { @() }
    foreach ($line in $lines) {
        if ($line -like 'baseUrl: *') { $baseUrl = $line.Substring(9).Trim() }
        if ($line -like 'tokenFile: *') { $tokenFile = $line.Substring(11).Trim() }
    }
    if ($baseUrl -and -not $tokenFile) { $baseUrl = $null }
}
$token = (Get-Content -Raw -LiteralPath $tokenFile).Trim()
$headers = @{ Authorization = "Bearer $token"; 'X-Cutlass-Agent' = 'serve demo' }
Step "listening on $baseUrl"

function Invoke-Api([string]$Method, [string]$Path, $Body) {
    $uri = $baseUrl + $Path
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 8 -Compress }
    try {
        if ($null -eq $json) { return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType 'application/json' `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($json))
    }
    catch {
        $detail = $_.ErrorDetails.Message
        throw "$Method $Path failed: $($_.Exception.Message) $detail"
    }
}

$ok = $false
try {
    # 3. Status, then the lock. Every later call carries the lock id in X-Cutlass-Lock.
    $status = Invoke-Api GET 'v1/status'
    if (-not $status.result.ffmpeg.ready) { throw "ffmpeg is not ready: $($status.result.ffmpeg.reason)" }
    Step "status: host $($status.result.hostKind), ffmpeg $($status.result.ffmpeg.version), project $($status.result.project.path)"
    $grant = Invoke-Api POST 'v1/lock/acquire' @{ agentName = 'serve demo'; reason = 'Story 3 render'; leaseSeconds = 120 }
    $headers['X-Cutlass-Lock'] = $grant.result.lockId
    Step "lock $($grant.result.phase) until $($grant.result.leaseExpiresAt)"

    # 4. Import and place.
    $import = Invoke-Api POST 'v1/media/import' @{ paths = @($clip) }
    if ($import.result.failed.Count -gt 0) { throw "import failed: $($import.result.failed[0].message)" }
    $mediaId = $import.result.imported[0].id
    Step "imported $mediaId ($($import.result.imported[0].durationSeconds) s)"
    $place = Invoke-Api POST 'v1/edit/place' @{ mediaId = $mediaId; at = 0; mode = 'overwrite'; streams = 'both'; returnProject = $true }
    $frames = $place.project.sequence.durationFrames
    Step "placed: '$($place.undoLabel)', timeline $frames frames"
    if ($frames -ne 120) { throw "expected 120 timeline frames, got $frames" }

    # 5. Export H.264 and poll until it ends.
    $job = Invoke-Api POST 'v1/exports' @{ preset = 'h264'; quality = 'high'; range = 'whole'; outputPath = $export; overwrite = $true }
    $exportId = $job.result.id
    Step "export $exportId queued"
    do {
        $state = Invoke-Api GET "v1/exports/$exportId`?waitMs=20000"
        Step ("export {0} {1:P0}" -f $state.result.state, [double]$state.result.fraction)
    } while ($state.result.state -notin @('completed', 'failed', 'canceled'))
    if ($state.result.state -ne 'completed') { throw "export ended $($state.result.state): $($state.result.error)" }

    # 6. Release, then shut down with a fresh lock (shutdown is a locked call) and save the project.
    Invoke-Api POST 'v1/lock/release' @{} | Out-Null
    $headers.Remove('X-Cutlass-Lock')
    $lock = Invoke-Api GET 'v1/lock'
    Step "released; lock phase $($lock.result.phase)"
    $grant = Invoke-Api POST 'v1/lock/acquire' @{ agentName = 'serve demo'; reason = 'Shut down' }
    $headers['X-Cutlass-Lock'] = $grant.result.lockId
    Invoke-Api POST 'v1/shutdown' @{ save = $true } | Out-Null
    Step 'shutdown requested'
    $ok = $true
}
finally {
    if (-not $server.WaitForExit(30000)) {
        $server.Kill()
        $ok = $false
        Write-Host 'demo: serve did not exit within 30 s; killed it.'
    }
}

if (-not $ok) { exit 1 }
Step "serve exited with $($server.ExitCode)"
if ($server.ExitCode -ne 0) { exit 1 }

# 7. Check the saved project and the export.
$saved = Get-Content -Raw -LiteralPath $project | ConvertFrom-Json
if ($saved.media.Count -ne 1) { throw 'the saved project does not hold the imported clip.' }
$probe = & ffprobe -v error -select_streams v:0 -count_frames -show_entries 'stream=codec_name,width,height,nb_read_frames' -of csv=p=0 $export
if ($LASTEXITCODE -ne 0) { throw "ffprobe could not read $export" }
Step "ffprobe ${export}: $probe"
$parts = "$probe".Trim().Split(',')
if ($parts[0] -ne 'h264' -or [int]$parts[3] -ne 120) { throw "expected h264 with 120 frames, got $probe" }
Step 'PASS'
exit 0
