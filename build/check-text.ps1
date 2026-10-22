<#
.SYNOPSIS
    Fails when any text file in the repository contains an em dash (U+2014).

.DESCRIPTION
    Checks every file git knows about (tracked plus untracked files that are not ignored). Outside a git checkout it
    falls back to every file under the repository root except .git, bin and obj. Binary files (a NUL byte in the
    first 8000 bytes) are skipped. Exits with code 1 and lists file:line for each hit.
    Runs on Windows PowerShell 5.1 and PowerShell 7 (pwsh) on Windows and Linux.
#>
[CmdletBinding()]
param(
    [string]$Root
)

$ErrorActionPreference = 'Stop'
if (-not $Root) {
    # $PSScriptRoot is not available in param defaults on Windows PowerShell 5.1.
    $Root = Split-Path -Parent $PSScriptRoot
}
$Root = (Resolve-Path -LiteralPath $Root).Path
$forbidden = [string][char]0x2014

$files = @()
$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) {
    Push-Location -LiteralPath $Root
    # Windows PowerShell 5.1 turns native stderr into errors, which 'Stop' would make fatal outside a checkout.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $listed = & git ls-files --cached --others --exclude-standard 2>$null
        if ($LASTEXITCODE -eq 0) {
            $files = @($listed | Where-Object { $_ } | ForEach-Object { Join-Path $Root $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
        }
    }
    finally {
        $ErrorActionPreference = $previousPreference
        Pop-Location
    }
}

if ($files.Count -eq 0) {
    $sep = [regex]::Escape([System.IO.Path]::DirectorySeparatorChar)
    $skip = "${sep}(\.git|bin|obj)${sep}"
    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch $skip } |
        ForEach-Object { $_.FullName })
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$hits = 0
$checked = 0
foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($file)
    $probe = [Math]::Min($bytes.Length, 8000)
    $binary = $false
    for ($i = 0; $i -lt $probe; $i++) {
        if ($bytes[$i] -eq 0) { $binary = $true; break }
    }
    if ($binary) { continue }

    $checked++
    $text = $utf8.GetString($bytes)
    if (-not $text.Contains($forbidden)) { continue }

    $lines = $text -split "`n"
    for ($n = 0; $n -lt $lines.Length; $n++) {
        if ($lines[$n].Contains($forbidden)) {
            $relative = $file.Substring($Root.Length).TrimStart('\', '/')
            Write-Host ("{0}:{1}: em dash (U+2014)" -f $relative, ($n + 1))
            $hits++
        }
    }
}

if ($hits -gt 0) {
    Write-Host ("check-text: {0} em dash(es) found. Use commas, colons or parentheses instead." -f $hits)
    exit 1
}

Write-Host ("check-text: {0} text files clean." -f $checked)
exit 0
