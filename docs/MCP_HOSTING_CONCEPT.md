# Concept: starting the MCP server from the app and briefing an agent

Status: **accepted and built on 2026-10-22, see AGENT_INTEGRATION 6.6** (which is normative; this file keeps the reasoning). **Superseded in part by AGENT_INTEGRATION 9 (provider neutrality, 2026-10-22):** the "Register with Claude Code" button, the Claude Code line of the panel and the single briefing template described below are replaced by client profiles (`agent-clients.json`), "Connect an agent client...", the "Clients" line and a neutral briefing body with per-client tails. Date: 2026-10-22. Builds on `docs/AGENT_INTEGRATION.md`, which stays normative for the API, the lock and the MCP exe.

## 1. What is missing today

- The MCP server is the exe `Cutlass.Agent.Mcp`, which an MCP client spawns over stdio and which forwards to the app's HTTP API. The app only shows two commands to copy in Settings > Agents. There is no "start" anywhere in the regular UI.
- A user who has an agent session open (Claude Code, Claude Desktop, Cursor) has to explain by hand that Cutlass is available and what to do with it.

## 2. The idea in one sentence

One **Agent menu** in the app with "Start MCP server", a status badge, a one-time "Register with Claude Code" button, and a **"Copy briefing for your agent"** action that produces a ready prompt: it tells the agent that the Cutlass MCP server is available, what is open, and the exact steps to work with it.

## 3. What "start" means

The MCP exe is always started by the client, never by the app (that is how stdio MCP works). What the app can start is everything the exe needs:

1. the agent server (HTTP API, lock, token), which today hides behind Settings > Agents > "Allow agent connections" or `--agent-server`;
2. the client registration, so the client knows the exe exists.

So "Start MCP server" in the app means: turn the agent server on for this session (and optionally remember it), check that the exe is present, and show green when a client can connect. "Stop" turns the server off and releases any agent lock (with the usual confirmation if an agent is editing).

A later, optional step is an MCP endpoint hosted by the app itself (`/mcp` on the same port); the SDK supports it without ASP.NET. It is not needed for this concept and is kept out of scope.

## 4. UI overview

### 4.1 Agent menu (new top-level menu between Sequence and View)

```
Agent
  Start MCP server            F9                (toggles to "Stop MCP server")
  --------------------------------------------
  Copy briefing for your agent
  Copy briefing (HTTP only, no MCP)
  --------------------------------------------
  Register with Claude Code...
  Show registration for other clients...        (opens Settings > Agents, MCP setup group)
  --------------------------------------------
  Agent panel                                   (existing)
  Take over                                     (existing, enabled while locked)
```

### 4.2 Status bar badge (existing badge, extended)

```
[ o MCP off ]            grey   : server off
[ o MCP ready ]          green  : server on, exe found, no session
[ o MCP: Claude Code ]   accent : an agent holds the lock (existing text)
[ ! MCP: port in use ]   danger : server failed; click opens Settings > Agents
```

Clicking the badge opens the Agent panel.

### 4.3 Agent panel (existing panel, one new header block)

```
+--------------------------------------------------------------+
| MCP server   o ready   http://localhost:47390/          [Stop]|
| Exe          ...\Cutlass.Agent.Mcp.exe   found                |
| Claude Code  registered (user scope)      [Re-register]       |
|                                                               |
| [ Copy briefing for your agent ]   [ Open docs ]              |
+--------------------------------------------------------------+
| Lock: free                                                    |
| Calls (newest first) ...                                      |
+--------------------------------------------------------------+
```

"Claude Code: registered" comes from `claude mcp get cutlass` (run once when the panel opens, cached); when the CLI is not on PATH the line reads "Claude Code: not found" and the Register button is disabled with a tooltip.

### 4.4 First start

The first time "Start MCP server" is used, a small dialog explains the two steps and offers them together:

```
Start the MCP server
  [x] Allow agent connections for this session
  [ ] Keep it on in future sessions
  [x] Register with Claude Code now (runs: claude mcp add cutlass -- <exe>)
                                              [Start]  [Cancel]
```

Registration runs the exact command shown, through the app's process helper, and reports the CLI's output. It is a one-time step; later starts skip the dialog unless the registration is missing.

## 5. The briefing prompt

"Copy briefing for your agent" renders a text from a template with live values and puts it on the clipboard. The user pastes it into the agent session (Claude Code, Claude Desktop, Cursor, anything with the `cutlass` MCP server registered).

### 5.1 Live values

| Placeholder | Source |
|---|---|
| `{project}` | project name and path (or "an untitled project") |
| `{sequence}` | width x height, frame rate, duration in timecode and frames |
| `{tracks}` | track list with clip counts (from `ProjectDescriber`, first lines) |
| `{media}` | imported media names |
| `{lock}` | "free" or "held by X" |
| `{url}` | the agent server base URL |
| `{exe}` | the MCP exe path |
| `{registration}` | the `claude mcp add` command |

### 5.2 Template (default, English; editable copy at `<data>/agent/briefing.md`)

```
Cutlass (a video editor) is running on this machine and exposes an MCP server named "cutlass".
Use its tools to work in the open project. Do not ask me to click anything; the editor is read only for me while you hold the lock.

Current state
- Project: {project}
- Sequence: {sequence}
- Tracks: {tracks}
- Media: {media}
- Lock: {lock}

How to work
1. Call cutlass_status, then cutlass_acquire_lock with your name and a short reason. Wait if it reports busy.
2. Call cutlass_describe_project before any edit; use clip and track ids from it, never guess ids.
3. Wrap each task in cutlass_begin_batch / cutlass_end_batch with a short label, so I can undo it as one step.
4. Check your work with cutlass_render_frame (and cutlass_audio_levels for sound) instead of assuming.
5. Timeline positions are frames; source positions are flicks; the project frame rate is in cutlass_status.
6. When done, cutlass_release_lock, then tell me what changed in a few lines.

If you cannot see any cutlass_* tools, the MCP server is not registered in this session. Register it and restart the session:
  {registration}
```

The "HTTP only" variant replaces the tool steps with `curl` examples against `{url}` using the bearer token (token inserted only when the user confirms, since the clipboard is less private than a config file).

### 5.3 Why a prompt and not automation

The agent session already exists on the user's side; Cutlass cannot inject into it. A pasted briefing is provider neutral, works in every client, and carries the state the agent would otherwise have to discover with several calls. The server instructions that the MCP server sends on `initialize` stay as they are; the briefing adds the current state and the user's intent on top.

## 6. Changes by project

| Project | Change |
|---|---|
| `Cutlass.App` | Agent menu and shortcut; `AgentBriefing` (template load, placeholder fill, clipboard); first-start dialog; `ClaudeCodeRegistration` (runs `claude mcp add` / `claude mcp get` through a process helper, parses the result); panel header block; badge texts; `CommandIds` entries so menu and keymap share the path |
| `Cutlass.Agent` | nothing required; `ProjectDescriber` gains a `Summary(maxLines)` overload for the briefing |
| `Cutlass.Agent.Mcp` | nothing |
| Settings | `AgentBriefingTemplatePath` (optional override), `McpStartDialogSeen` |
| Tests | unit: briefing rendering with a fixed project (golden text), registration state parsing from fake CLI output, menu command gating while locked; smoke `m9` gains `agent-briefing` (renders and checks the placeholders) |
| Docs | AGENT_INTEGRATION 6.6 "Agent menu and briefing", DESIGN 3 (menu), 4.5 (panel), 7 (shortcut), README "AI agents" (the two-step start) |

## 7. Build order

1. Agent menu with Start/Stop over the existing `AgentServerController`, badge texts, panel header block. *Verify:* m9 still green; a screenshot of the menu and panel.
2. Briefing template, renderer, Copy actions, unit golden test. *Verify:* paste the briefing into a `claude -p` run with the MCP registered and watch story 1 happen without further instructions.
3. First-start dialog and Claude Code registration (add, get, re-register). *Verify:* on a machine with the CLI, Register shows "registered (user scope)"; without the CLI the button is disabled.
4. Docs.

## 8. Decisions needed

1. Shortcut for Start/Stop: `F9` (built; `Ctrl+Shift+M` is "previous marker" and `Ctrl+Alt+M` is AltGr+M, the micro sign, on German layouts).
2. Briefing language: English only (matches the tool descriptions) or follow the OS language.
3. Should the HTTP-only briefing include the token in the clipboard text after a confirmation, or only a pointer to the token file (recommended: confirmation).

---

**TLDR:** An Agent menu starts and stops the agent server the MCP exe needs, a badge shows the state, a one-time button registers the exe with Claude Code, and "Copy briefing for your agent" produces a pasteable prompt with the current project state and the exact tool steps. Four small build steps, no changes to the API or the exe.
