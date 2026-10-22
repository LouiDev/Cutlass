<#
.SYNOPSIS
    Publishes Cutlass and its MCP server as self-contained apps in one folder (TECH_BRIEF 12, M7; AGENT_INTEGRATION
    8, A4) and checks the result.

.DESCRIPTION
    Runs 'dotnet publish src/Cutlass.App -c Release -r <rid> --self-contained' into artifacts/publish/<rid> (or
    -Output), then publishes src/Cutlass.Agent.Mcp the same way into a staging folder and merges it in. Nothing is
    trimmed: Avalonia, the MVVM toolkit, the MCP SDK and System.Text.Json metadata rely on reflection.
    Two self-contained apps share one folder, so a file both publish must be byte-identical (the runtime, the Cutlass
    libraries, the fonts). The merge fails when one differs, because the second app would silently replace an
    assembly the first app's deps.json resolves (AGENT_INTEGRATION 8, "Two self-contained apps in one folder").
    Afterwards it checks that the folder holds the app, the MCP exe, the OpenAL Soft native of the runtime
    (soft_oal.dll or libopenal.so, from Silk.NET.OpenAL.Soft.Native) and the bundled Inter fonts with OFL.txt
    (TECH_BRIEF 5.5), copies docs/AGENT_INTEGRATION.md beside the exes, and, when the runtime matches this
    OS, runs 'Cutlass.Agent.Mcp --print-registration' on the published exe. The default output folder is emptied
    first; a custom -Output only with -Clean.
    win-x64 and linux-x64 both publish from either OS (cross-publishing needs no extra workload).
    Runs on Windows PowerShell 5.1 and PowerShell 7 (pwsh). build/publish.sh does the same in bash.

.EXAMPLE
    ./build/publish.ps1                      # win-x64
.EXAMPLE
    ./build/publish.ps1 -Runtime linux-x64
.EXAMPLE
    ./build/publish.ps1 -Output C:/Temp/cutlass -Clean
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$Output,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) {
    $Output = Join-Path $root (Join-Path 'artifacts/publish' $Runtime)
    $Clean = $true
}

if ($Clean -and (Test-Path -LiteralPath $Output)) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

function Invoke-Publish([string]$ProjectPath, [string]$Destination) {
    & dotnet publish $ProjectPath -c $Configuration -r $Runtime --self-contained -o $Destination --disable-build-servers -m:1 -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish of $ProjectPath failed with exit code $LASTEXITCODE"
    }
}

Invoke-Publish (Join-Path $root 'src/Cutlass.App/Cutlass.App.csproj') $Output

# The MCP exe goes to a staging folder first, so a clash with the app is found before anything is overwritten.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('cutlass-mcp-publish-' + [guid]::NewGuid().ToString('N').Substring(0, 12))
try {
    Invoke-Publish (Join-Path $root 'src/Cutlass.Agent.Mcp/Cutlass.Agent.Mcp.csproj') $staging

    $outputFull = (Resolve-Path -LiteralPath $Output).Path
    $stagingFull = (Resolve-Path -LiteralPath $staging).Path
    $conflicts = @()
    $toCopy = @()
    foreach ($file in Get-ChildItem -LiteralPath $stagingFull -Recurse -File) {
        $relative = $file.FullName.Substring($stagingFull.Length).TrimStart('\', '/')
        $target = Join-Path $outputFull $relative
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            $a = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $b = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            if ($a -ne $b) {
                $conflicts += $relative
            }
        }
        else {
            $toCopy += $relative
        }
    }

    if ($conflicts.Count -gt 0) {
        throw ("Cutlass.App and Cutlass.Agent.Mcp publish different versions of: {0}. Pin one version for both in Directory.Packages.props, or publish the MCP exe framework-dependent into a subfolder (AGENT_INTEGRATION 8)." -f ($conflicts -join ', '))
    }

    foreach ($relative in $toCopy) {
        $target = Join-Path $outputFull $relative
        $parent = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
        }
        Copy-Item -LiteralPath (Join-Path $stagingFull $relative) -Destination $target
    }
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

# The agent docs beside the exes, where "Open agent docs" in Settings > Agents and the Agent panel looks first
# (AGENT_INTEGRATION 6.1; AgentServerController.AgentDocsPath).
Copy-Item -LiteralPath (Join-Path $root 'docs/AGENT_INTEGRATION.md') -Destination (Join-Path $Output 'AGENT_INTEGRATION.md') -Force

# What a published folder must hold to run: the app, the MCP exe, the OpenAL native for the runtime, the drawtext
# fonts and the agent docs.
if ($Runtime -eq 'win-x64') {
    $required = @('Cutlass.App.exe', 'Cutlass.Agent.Mcp.exe', 'soft_oal.dll')
}
else {
    $required = @('Cutlass.App', 'Cutlass.Agent.Mcp', 'libopenal.so')
}
$required += @('Cutlass.App.deps.json', 'Cutlass.Agent.Mcp.deps.json')
$required += @('fonts/Inter-Regular.ttf', 'fonts/Inter-SemiBold.ttf', 'fonts/Inter-Bold.ttf', 'fonts/OFL.txt', 'AGENT_INTEGRATION.md')
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Output $_) -PathType Leaf) })
if ($missing.Count -gt 0) {
    throw ("The published folder {0} lacks: {1}" -f $Output, ($missing -join ', '))
}

# Run the published MCP exe once when it can run here; it must print the registration and exit with 0.
$runsHere = ($Runtime -eq 'win-x64' -and [System.IO.Path]::DirectorySeparatorChar -eq '\') -or
    ($Runtime -eq 'linux-x64' -and [System.IO.Path]::DirectorySeparatorChar -eq '/' -and (Test-Path -LiteralPath '/proc'))
if ($runsHere) {
    $mcpExe = Join-Path $Output $required[1]
    $registration = & $mcpExe --print-registration
    if ($LASTEXITCODE -ne 0 -or -not ($registration -match 'claude mcp add cutlass')) {
        throw "The published $($required[1]) --print-registration failed (exit code $LASTEXITCODE)."
    }
    Write-Host ($registration -join [Environment]::NewLine)
}
else {
    Write-Host "Skipped running $($required[1]) --print-registration: $Runtime does not run on this OS."
}

$bytes = (Get-ChildItem -LiteralPath $Output -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("Published {0} to {1} ({2:0} MB); checked {3}" -f $Runtime, $Output, ($bytes / 1MB), ($required -join ', '))
