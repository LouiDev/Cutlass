#!/usr/bin/env bash
# Publishes Cutlass and its MCP server as self-contained apps in one folder (TECH_BRIEF 12, M7; AGENT_INTEGRATION 8,
# A4) and checks the result. Mirrors build/publish.ps1: 'dotnet publish src/Cutlass.App -c Release -r <rid>
# --self-contained' into artifacts/publish/<rid> (emptied first) or the given folder (left as is), then
# src/Cutlass.Agent.Mcp the same way into a staging folder that is merged in. A file both publish must be
# byte-identical (the runtime, the Cutlass libraries, the fonts); the merge fails when one differs, because the second
# app would silently replace an assembly the first app's deps.json resolves. Nothing is trimmed. Afterwards it checks
# that the folder holds the app, the MCP exe, the OpenAL Soft native of the runtime and the bundled Inter fonts with
# OFL.txt, copies docs/AGENT_INTEGRATION.md beside the exes and, when the runtime matches this OS, runs
# 'Cutlass.Agent.Mcp --print-registration' on the published exe.
# Usage: build/publish.sh [linux-x64|win-x64] [Configuration] [OutputDir]
set -euo pipefail

runtime="${1:-linux-x64}"
configuration="${2:-Release}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${3:-}"

case "$runtime" in
    linux-x64) app="Cutlass.App"; mcp="Cutlass.Agent.Mcp"; required=("Cutlass.App" "Cutlass.Agent.Mcp" "libopenal.so") ;;
    win-x64) app="Cutlass.App.exe"; mcp="Cutlass.Agent.Mcp.exe"; required=("Cutlass.App.exe" "Cutlass.Agent.Mcp.exe" "soft_oal.dll") ;;
    *) echo "Unsupported runtime: $runtime (use linux-x64 or win-x64)" >&2; exit 2 ;;
esac
required+=("Cutlass.App.deps.json" "Cutlass.Agent.Mcp.deps.json")
required+=("fonts/Inter-Regular.ttf" "fonts/Inter-SemiBold.ttf" "fonts/Inter-Bold.ttf" "fonts/OFL.txt" "AGENT_INTEGRATION.md" "agent-clients.json")

if [ -z "$output" ]; then
    output="$root/artifacts/publish/$runtime"
    rm -rf "$output"
fi

publish() {
    dotnet publish "$1" -c "$configuration" -r "$runtime" --self-contained -o "$2" \
        --disable-build-servers -m:1 -p:PublishTrimmed=false
}

publish "$root/src/Cutlass.App/Cutlass.App.csproj" "$output"

staging="$(mktemp -d "${TMPDIR:-/tmp}/cutlass-mcp-publish-XXXXXX")"
trap 'rm -rf "$staging"' EXIT
publish "$root/src/Cutlass.Agent.Mcp/Cutlass.Agent.Mcp.csproj" "$staging"

conflicts=()
while IFS= read -r -d '' file; do
    relative="${file#"$staging"/}"
    if [ -f "$output/$relative" ] && ! cmp -s "$file" "$output/$relative"; then
        conflicts+=("$relative")
    fi
done < <(find "$staging" -type f -print0)
if [ "${#conflicts[@]}" -gt 0 ]; then
    echo "Cutlass.App and Cutlass.Agent.Mcp publish different versions of: ${conflicts[*]}." >&2
    echo "Pin one version for both in Directory.Packages.props, or publish the MCP exe framework-dependent into a subfolder (AGENT_INTEGRATION 8)." >&2
    exit 1
fi
# Every overlapping file is identical, so copying the whole staging folder only adds the MCP exe's own files.
cp -R "$staging/." "$output/"

# The agent docs beside the exes, where "Open agent docs" in Settings > Agents and the Agent panel looks first.
cp "$root/docs/AGENT_INTEGRATION.md" "$output/AGENT_INTEGRATION.md"

missing=()
for file in "${required[@]}"; do
    [ -f "$output/$file" ] || missing+=("$file")
done
if [ "${#missing[@]}" -gt 0 ]; then
    echo "The published folder $output lacks: ${missing[*]}" >&2
    exit 1
fi

runs_here=false
case "$(uname -s)" in
    Linux) [ "$runtime" = "linux-x64" ] && runs_here=true ;;
    MINGW* | MSYS* | CYGWIN*) [ "$runtime" = "win-x64" ] && runs_here=true ;;
esac

if [ "$runtime" = "linux-x64" ] && [ "$(uname -s)" = "Linux" ]; then
    # A publish from Windows cannot set the execute bit; on Linux make sure it is there.
    chmod +x "$output/$app" "$output/$mcp"
fi

if [ "$runs_here" = true ]; then
    if ! registration="$("$output/$mcp" --print-registration)"; then
        echo "The published $mcp --print-registration failed." >&2
        exit 1
    fi
    # One block per client profile: a line that starts with the profile name (AGENT_INTEGRATION 9.4).
    absent=()
    while IFS= read -r name; do
        grep -qF -- "$name" <<<"$(grep -E '^[^ ]' <<<"$registration")" || absent+=("$name")
    done < <(grep -oE '"name": *"[^"]+"' "$output/agent-clients.json" | sed -E 's/"name": *"([^"]+)"//')
    if [ "${#absent[@]}" -gt 0 ]; then
        echo "The published $mcp --print-registration printed no block for: ${absent[*]}" >&2
        exit 1
    fi
    echo "$registration"
else
    echo "Skipped running $mcp --print-registration: $runtime does not run on this OS."
fi

echo "Published $runtime to $output; checked ${required[*]}"
