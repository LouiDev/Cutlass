<#
.SYNOPSIS
    Runs a smoke script (build/smoke/m4.smoke, m5.smoke, m6.smoke, m7.smoke, m7b.smoke or m9.smoke) in the real app against
    freshly generated clips.

.DESCRIPTION
    -Script m4 (the default) generates two 6 s lavfi clips (testsrc2 plus a -4.4 dBFS tone, different hues) with the ffmpeg on
    PATH, copies build/smoke/m4.smoke next to them and starts the app with --smoke-script. The app imports, edits,
    scrubs, saves, reopens and exports through its view model commands and checks the export with ffprobe.

    -Script m5 generates clipA.mp4 (12 s, 1280x720), clipB.mp4 (12 s, 2560x1440, which the Auto proxy policy proxies)
    with loud amplitude-modulated tones, and music.wav (24 s, audio only), then starts the app with
    --import clipA.mp4 clipB.mp4 --smoke-script m5.smoke. The script waits for the media jobs, checks the thumbnails,
    peaks and proxy, plays 10 s on the audio device with the drift check, checks live mute and solo on the master
    meter, and shuttles at 2x and 4x.

    -Script m6 generates the same two 6 s clips as m4 (180 frames at 30 fps each) and runs build/smoke/m6.smoke: the
    Source monitor with In/Out and the three-point insert and overwrite keys, click-to-type timecode, match frame,
    copy and paste, Q and W ripple trims, video and audio crossfades, speed, closing a gap, adding a track, and an
    export whose frame count ffprobe checks. After the export it drops Effects tab items and Source ranges (video
    only, audio only, both) and takes the DESIGN 5 checklist screenshots (shots/05 to shots/12).

    -Script m7 generates clipA.mp4 and clipB.mp4 (4 s, 640x360, 30 fps, 120 frames each) and runs
    build/smoke/m7.smoke: the Settings dialog through Ctrl+, (validation, live defaults, the ffmpeg re-detect, the
    About page), then a 120-frame sequence with a crossfade and a title, exported once per DESIGN 9 preset (H.264,
    H.265, VP9, AV1, ProRes, GIF, MP3, AAC, WAV, FLAC), each checked with ffprobe for codec, container, frame count or
    duration and BT.709 tags. The settings in effect before the run are restored at its end.

    -Script m7b generates the m7 clips plus gone.mp4 and runs build/smoke/m7b.smoke: the three clips end to end, a
    save, gone.mp4 deleted and the project reopened (the offline clip is hatched with the OFFLINE badge), then
    screenshots of the real dialog windows (Relink, the four Settings pages, Export, Speed, Sequence Settings) for
    the DESIGN 8 visual check.

    -Script m9 generates the m4 clips plus gap.mp4 (6 s whose tone drops out for 1.5 s in the middle) and starts the app
    with --agent-server --port 47399. build/smoke/m9.smoke drives the agent HTTP API in process (AGENT_INTEGRATION 7.3):
    a call without the lock, acquire with the banner screenshot, the read-only UI (Ctrl+Z and Delete are swallowed),
    an agent batch with place, split and a title, silence removal, a frame rendered through the API and saved as PNG,
    pause and resume, take over, one Ctrl+Z that reverts a whole agent batch, an agent export checked with ffprobe and
    the Agent panel screenshot.

    -AppPath runs a published Cutlass executable instead of 'dotnet run' (for example the output of
    build/publish.ps1), so the smoke test can check a publish end to end. -Configuration (default Debug) and -NoBuild
    pass through to 'dotnet run' (CI runs the Release build it already made).

    Every run writes screenshots to <OutDir>/shots and exits with 0 when every step passed. The log is
    <OutDir>/<script>.smoke.log. Needs a desktop session (on Linux run it under xvfb-run). Runs on Windows
    PowerShell 5.1 and PowerShell 7 (pwsh).

.EXAMPLE
    ./build/smoke.ps1 -OutDir C:/Temp/cutlass-smoke
.EXAMPLE
    ./build/smoke.ps1 -Script m5 -OutDir C:/Temp/cutlass-smoke-m5
.EXAMPLE
    pwsh build/smoke.ps1 -Script m6
.EXAMPLE
    powershell -File build/smoke.ps1 -Script m7
.EXAMPLE
    powershell -File build/smoke.ps1 -Script m7b
.EXAMPLE
    powershell -File build/smoke.ps1 -Script m9
.EXAMPLE
    powershell -File build/smoke.ps1 -AppPath artifacts/publish/win-x64/Cutlass.App.exe
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [ValidateSet('m4', 'm5', 'm6', 'm7', 'm7b', 'm9')]
    [string]$Script = 'm4',
    [string]$AppPath,
    [string]$Configuration = 'Debug',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) {
    $OutDir = Join-Path ([System.IO.Path]::GetTempPath()) ($(if ($Script -eq 'm4') { 'cutlass-smoke' } else { "cutlass-smoke-$Script" }))
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
foreach ($old in @('out', 'shots')) {
    $path = Join-Path $OutDir $old
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

function Invoke-Ffmpeg([string[]]$FfArgs, [string]$Name) {
    & ffmpeg @FfArgs
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg could not generate $Name" }
}

# libx264 when the build has it (long-GOP H.264 like camera files), else mpeg4.
$encoders = & ffmpeg -hide_banner -encoders 2>$null
$video = if ($encoders -match '\blibx264\b') { @('-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-g', '60') } else { @('-c:v', 'mpeg4', '-q:v', '3') }
$appArgs = @()
# The clip tones peak at 0.6 (about -4.4 dBFS; the lavfi sine source is only -18 dBFS), so the waveforms in the
# smoke screenshots are clearly visible.
if ($Script -eq 'm7' -or $Script -eq 'm7b') {
    # Small clips keep the ten preset exports (H.265 and AV1 included) to about a minute.
    $clips = @(@{ Name = 'clipA.mp4'; Hue = 0; Tone = 440 }, @{ Name = 'clipB.mp4'; Hue = 180; Tone = 880 })
    if ($Script -eq 'm7b') { $clips += @{ Name = 'gone.mp4'; Hue = 90; Tone = 660 } }
    foreach ($clip in $clips) {
        $ffArgs = @('-hide_banner', '-v', 'error', '-y',
            '-f', 'lavfi', '-i', "testsrc2=s=640x360:r=30:d=4,hue=h=$($clip.Hue):s=1.4",
            '-f', 'lavfi', '-i', "aevalsrc=0.6*sin(2*PI*$($clip.Tone)*t):s=48000:d=4",
            '-map', '0:v', '-map', '1:a') + $video + @('-c:a', 'aac', '-ac', '2', '-b:a', '128k', '-shortest', (Join-Path $OutDir $clip.Name))
        Invoke-Ffmpeg $ffArgs $clip.Name
    }
}
elseif ($Script -eq 'm4' -or $Script -eq 'm6' -or $Script -eq 'm9') {
    # m9 adds gap.mp4, whose tone is gated off from 2.25 s to 3.75 s (the silence the agent finds and removes).
    $clips = @(@{ Name = 'clipA.mp4'; Hue = 0; Tone = '0.6*sin(2*PI*440*t)' }, @{ Name = 'clipB.mp4'; Hue = 180; Tone = '0.6*sin(2*PI*880*t)' })
    if ($Script -eq 'm9') { $clips += @{ Name = 'gap.mp4'; Hue = 90; Tone = '0.6*sin(2*PI*660*t)*(lt(t\,2.25)+gte(t\,3.75))' } }
    foreach ($clip in $clips) {
        $ffArgs = @('-hide_banner', '-v', 'error', '-y',
            '-f', 'lavfi', '-i', "testsrc2=s=1280x720:r=30:d=6,hue=h=$($clip.Hue):s=1.4",
            '-f', 'lavfi', '-i', "aevalsrc=$($clip.Tone):s=48000:d=6",
            '-map', '0:v', '-map', '1:a') + $video + @('-c:a', 'aac', '-ac', '2', '-b:a', '128k', '-shortest', (Join-Path $OutDir $clip.Name))
        Invoke-Ffmpeg $ffArgs $clip.Name
    }

    if ($Script -eq 'm9') {
        # AGENT_INTEGRATION 7.3: the agent HTTP API on a fixed port for this run only (the saved setting is unchanged).
        $appArgs = @('--agent-server', '--port', '47399')
    }
}
else {
    # The tones swing between -10 and -6 dBFS (0.5 * (0.7 + 0.3 * sin)), so the meter moves but never drops near the
    # live-mute thresholds of the script.
    $clips = @(@{ Name = 'clipA.mp4'; Size = '1280x720'; Hue = 0; Tone = 440 }, @{ Name = 'clipB.mp4'; Size = '2560x1440'; Hue = 180; Tone = 660 })
    foreach ($clip in $clips) {
        $ffArgs = @('-hide_banner', '-v', 'error', '-y',
            '-f', 'lavfi', '-i', "testsrc2=s=$($clip.Size):r=30:d=12,hue=h=$($clip.Hue):s=1.4",
            '-f', 'lavfi', '-i', "aevalsrc=0.5*sin(2*PI*$($clip.Tone)*t)*(0.7+0.3*sin(2*PI*0.5*t)):s=48000:d=12",
            '-map', '0:v', '-map', '1:a') + $video + @('-c:a', 'aac', '-ac', '2', '-b:a', '160k', '-shortest', (Join-Path $OutDir $clip.Name))
        Invoke-Ffmpeg $ffArgs $clip.Name
    }

    Invoke-Ffmpeg @('-hide_banner', '-v', 'error', '-y', '-f', 'lavfi', '-i', 'sine=f=220:r=48000:d=24',
        '-af', 'volume=2.4', '-ac', '2', '-c:a', 'pcm_s16le', (Join-Path $OutDir 'music.wav')) 'music.wav'
    $appArgs = @('--import', (Join-Path $OutDir 'clipA.mp4'), (Join-Path $OutDir 'clipB.mp4'))
}

$scriptFile = Join-Path $OutDir "$Script.smoke"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "smoke/$Script.smoke") -Destination $scriptFile -Force
if ($AppPath) {
    $exe = (Resolve-Path -LiteralPath $AppPath).Path
    & $exe @($appArgs + @('--smoke-script', $scriptFile)) | Out-Host
}
else {
    $runArgs = @('run', '--project', (Join-Path $repo 'src/Cutlass.App'), '-c', $Configuration, '--disable-build-servers', '-m:1')
    if ($NoBuild) { $runArgs += '--no-build' }
    $runArgs += @('--') + $appArgs + @('--smoke-script', $scriptFile)
    & dotnet @runArgs
}
$code = $LASTEXITCODE
$log = "$scriptFile.log"
if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log | Select-Object -Last 5 }
Write-Host ("smoke: exit code {0}, log {1}" -f $code, $log)
exit $code
