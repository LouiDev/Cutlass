<#
.SYNOPSIS
    The HTTP-only agent path of docs/AGENT_INTEGRATION.md 9.5: no SDK, no MCP, only Invoke-RestMethod.
.DESCRIPTION
    Talks to a running Cutlass (agent connections on, or --agent-server) or to `Cutlass.Agent.Mcp serve`:
      1. GET  /v1/status                 health, ffmpeg and the open project
      2. POST /v1/lock/acquire           take the edit lock; its lockId goes into X-Cutlass-Lock from then on
      3. GET  /v1/project/describe       the compact text overview
      4. POST /v1/call                   one edit through the generic call route: cutlass_split at the middle of the
                                         first video clip, or cutlass_add_marker at frame 0 when there is no clip
      5. GET  /v1/frame/0?width=320      frame 0 as a PNG file
      6. POST /v1/lock/release           always, also after a failure
    Each step is printed. Exit code 0 when every step passed, else 1.

    The base URL and the token come from <data>/agent/endpoint.json and the token file it names (<data> is
    %LOCALAPPDATA%\Cutlass on Windows, ${XDG_DATA_HOME:-~/.local/share}/cutlass elsewhere), unless -BaseUrl with
    -Token or -TokenFile is given. Runs on Windows PowerShell 5.1 and PowerShell 7.
.EXAMPLE
    powershell -File build/examples/agent-http.ps1
.EXAMPLE
    pwsh build/examples/agent-http.ps1 -BaseUrl http://127.0.0.1:47410/ -TokenFile ~/.local/share/cutlass/agent/token -OutFile frame0.png
#>
[CmdletBinding()]
param(
    [string]$BaseUrl,
    [string]$Token,
    [string]$TokenFile,
    [string]$OutFile = (Join-Path ([System.IO.Path]::GetTempPath()) 'cutlass-agent-http-frame0.png'),
    [string]$AgentName = 'HTTP example'
)

$ErrorActionPreference = 'Stop'
function Step([string]$Text) { Write-Host "agent-http: $Text" }

if (-not $BaseUrl -or (-not $Token -and -not $TokenFile)) {
    $onWindows = [Environment]::OSVersion.Platform -eq 'Win32NT'
    $data = if ($onWindows) { Join-Path $env:LOCALAPPDATA 'Cutlass' }
            elseif ($env:XDG_DATA_HOME -and [System.IO.Path]::IsPathRooted($env:XDG_DATA_HOME)) { Join-Path $env:XDG_DATA_HOME 'cutlass' }
            else { Join-Path $HOME '.local/share/cutlass' }
    $endpointFile = Join-Path $data 'agent/endpoint.json'
    if (-not (Test-Path -LiteralPath $endpointFile)) {
        Step "no ${endpointFile}: start Cutlass with agent connections on, or Cutlass.Agent.Mcp serve, or pass -BaseUrl and -Token."
        exit 1
    }
    $endpoint = Get-Content -Raw -LiteralPath $endpointFile | ConvertFrom-Json
    if (-not $BaseUrl) { $BaseUrl = $endpoint.baseUrl }
    if (-not $Token -and -not $TokenFile) { $TokenFile = $endpoint.tokenFile }
}
if (-not $Token) { $Token = (Get-Content -Raw -LiteralPath $TokenFile).Trim() }
if (-not $BaseUrl.EndsWith('/')) { $BaseUrl += '/' }
$headers = @{ Authorization = "Bearer $Token"; 'X-Cutlass-Agent' = $AgentName }

function Invoke-Api([string]$Method, [string]$Path, $Body) {
    $uri = $BaseUrl + $Path
    try {
        if ($null -eq $Body) { return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers }
        $json = $Body | ConvertTo-Json -Depth 8 -Compress
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType 'application/json' `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($json))
    }
    catch {
        throw "$Method $Path failed: $($_.Exception.Message) $($_.ErrorDetails.Message)"
    }
}

$ok = $false
try {
    # 1. Status.
    $status = Invoke-Api GET 'v1/status'
    $s = $status.result
    Step "status: $($s.app) $($s.version), host $($s.hostKind), ffmpeg ready $($s.ffmpeg.ready), project '$($s.project.name)'"
    if (-not $s.ffmpeg.ready) { throw "ffmpeg is not ready: $($s.ffmpeg.reason)" }
    if (-not $s.project.hasProject) { throw 'no project is open.' }

    # 2. The lock. Every later call carries it in X-Cutlass-Lock.
    $grant = Invoke-Api POST 'v1/lock/acquire' @{ agentName = $AgentName; reason = 'HTTP example edit'; waitMs = 10000 }
    $headers['X-Cutlass-Lock'] = $grant.result.lockId
    Step "lock $($grant.result.phase) until $($grant.result.leaseExpiresAt)"

    # 3. Describe (text/plain).
    $describe = Invoke-Api GET 'v1/project/describe'
    Step 'describe:'
    "$describe".Split("`n") | Where-Object { $_ } | ForEach-Object { Write-Host "    $_" }

    # 4. One edit through /v1/call, the route a harness uses for every tool.
    $project = (Invoke-Api GET 'v1/project').result
    $clip = $project.tracks | Where-Object { $_.kind -eq 'video' } | ForEach-Object { $_.clips } |
        Where-Object { $_.duration -ge 2 } | Sort-Object start | Select-Object -First 1
    if ($clip) {
        $frame = [int]$clip.start + [int][Math]::Floor($clip.duration / 2)
        $edit = Invoke-Api POST 'v1/call' @{ tool = 'cutlass_split'; args = @{ frame = $frame } }
        Step "split $($clip.id) at frame ${frame}: '$($edit.undoLabel)', changed $($edit.changed), revision $($edit.revision)"
        if (-not $edit.changed) { throw 'the split changed nothing.' }
    }
    else {
        $edit = Invoke-Api POST 'v1/call' @{ tool = 'cutlass_add_marker'; args = @{ frame = 0; name = 'HTTP example' } }
        Step "no clip; added a marker at frame 0: '$($edit.undoLabel)', revision $($edit.revision)"
    }

    # 5. Frame 0 as a PNG file (the per-route form answers with image/png).
    $outDir = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutFile))
    if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    try {
        Invoke-WebRequest -UseBasicParsing -Method GET -Uri ($BaseUrl + 'v1/frame/0?width=320') -Headers $headers -OutFile $OutFile
    }
    catch {
        throw "GET v1/frame/0 failed: $($_.Exception.Message) $($_.ErrorDetails.Message)"
    }
    $bytes = [System.IO.File]::ReadAllBytes([System.IO.Path]::GetFullPath($OutFile))
    if ($bytes.Length -lt 8 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) {
        throw "$OutFile is not a PNG."
    }
    Step "frame 0 saved to $OutFile ($($bytes.Length) bytes)"
    $ok = $true
}
catch {
    Step "FAILED: $($_.Exception.Message)"
}
finally {
    # 6. Always give the project back.
    if ($headers.ContainsKey('X-Cutlass-Lock')) {
        try {
            Invoke-Api POST 'v1/lock/release' @{} | Out-Null
            Step 'lock released'
        }
        catch {
            Step "release failed: $($_.Exception.Message)"
            $ok = $false
        }
    }
}

if ($ok) { Step 'PASS'; exit 0 }
exit 1
