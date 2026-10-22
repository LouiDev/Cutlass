# Agent examples

Runnable proof that any tool-calling model or plain script can drive Cutlass (`docs/AGENT_INTEGRATION.md` 9.5). None of them is tied to one vendor: the tool definitions come from the server in the format each harness consumes, and every tool call goes through the same route.

| File | Needs | What it shows |
|---|---|---|
| `agent-openai-compatible.py` | Python 3.8+, `pip install openai` | Any OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, OpenAI, Azure OpenAI, Groq, Mistral, ...): loads `/v1/tools?format=openai`, uses `/v1/instructions` as the system prompt, runs a tool loop (at most 40 steps) against `/v1/call`. |
| `agent-anthropic.py` | Python 3.8+, `pip install anthropic`, `ANTHROPIC_API_KEY` | The same loop with the Anthropic SDK and `/v1/tools?format=anthropic`. Default model `claude-opus-5-5`; server-side refusal fallbacks on (`--no-fallbacks` turns them off). |
| `agent-http.ps1` | Windows PowerShell 5.1 or PowerShell 7 | The HTTP-only path, no SDK: status, lock, describe, one edit through `/v1/call` (a split at the middle of the first video clip, or a marker at frame 0 when there is no clip), frame 0 to a PNG file, release. CI runs it against `serve`. |
| `agent-serve-demo.ps1` | PowerShell, ffmpeg, a built or published `Cutlass.Agent.Mcp` | Story 3: starts `serve` itself on a new project, imports and places a clip, exports H.264, shuts the server down with a save and checks the export with ffprobe. |

## The contract the examples use

1. **Tool definitions** in your harness's format: `GET /v1/tools?format=openai|anthropic|gemini|markdown`, or `GET /v1/openapi.json` for frameworks that ingest OpenAPI.
2. **One call route:** `POST /v1/call` with `{"tool": "<name>", "args": {...}}`. Tool names are the `cutlass_*` names of the definitions. Image tools answer with `pngBase64` in `result`.
3. **The workflow text** for the system prompt: `GET /v1/instructions` (lock, describe, batch, verify, release).

Every request carries `Authorization: Bearer <token>`. After `cutlass_acquire_lock`, send its `lockId` as `X-Cutlass-Lock` on every call; the Python harnesses do this for the model, renew the lease every 10 s while the model thinks, and release the lock on exit (also on Ctrl+C). They save image results as PNG files and tell the model the path and size.

## Start Cutlass first

All examples find Cutlass through `<data>/agent/endpoint.json` and the token file it names (`<data>` is `%LOCALAPPDATA%\Cutlass` on Windows, `${XDG_DATA_HOME:-~/.local/share}/cutlass` on Linux). Either:

- run the app and turn on **Settings > Agents > Allow agent connections** (or **Agent > Start MCP server**, F9, or start it with `--agent-server`); you then watch the agent work in the timeline; or
- run the engine headless: `Cutlass.Agent.Mcp serve --project my.cutlass --create --allow-read D:\Footage --allow-write D:\renders`.

Override the discovery with `--endpoint <baseUrl> --token <token>` (Python) or `-BaseUrl <url> -Token <token>` / `-TokenFile <file>` (PowerShell). The agent may read only the project and media folders plus the allowed read folders, and write only to the export folder plus the allowed write folders, so give paths inside them in the task.

## Running the Python harnesses

Use a virtual environment; the harnesses need nothing but their SDK (they use `urllib` for Cutlass):

```
python -m venv .venv
.venv\Scripts\activate            (Linux: source .venv/bin/activate)
pip install openai anthropic
```

**Ollama** (default `--base-url http://localhost:11434/v1`, `--api-key ollama`). Use a model whose `ollama show <model>` lists the `tools` capability, for example `gpt-oss:20b`, `qwen3`, `llama3.1` or `mistral-small`. The first request (57 tool definitions plus the instructions) is about 5300 tokens, more than the 4096-token context Ollama gives many models by default (it answers `exceed_context_size_error`), so raise it before `ollama serve`, for example `OLLAMA_CONTEXT_LENGTH=32768` (Windows: `set OLLAMA_CONTEXT_LENGTH=32768`).

```
python build/examples/agent-openai-compatible.py --model gpt-oss:20b --task "Import D:\Footage\a.mp4 and D:\Footage\b.mp4, place them end to end, add a centred title 'Hello' over the first second, render frame 15 and release the lock."
```

**OpenAI:** `--base-url https://api.openai.com/v1 --api-key %OPENAI_API_KEY% --model <model>` (the API key also defaults to `OPENAI_API_KEY`).

**LM Studio:** start the local server (Developer tab), load a tool-capable model, then `--base-url http://localhost:1234/v1 --api-key lm-studio --model <model id>`.

**vLLM:** `vllm serve <model> --enable-auto-tool-choice --tool-call-parser <parser for the model>`, then `--base-url http://localhost:8000/v1 --api-key EMPTY --model <model>`.

**Anthropic:**

```
set ANTHROPIC_API_KEY=...
python build/examples/agent-anthropic.py --task "Split V1 at frame 60 and add a marker there"
```

Each tool call prints one line (`[step] tool {args} -> ok rev 7 Agent: Split` or `-> error lock_lost: ...`). The exit code is 0 when the model answers without a tool call (it reports done), 1 when it hit `--max-steps` (40) or the model endpoint failed. Small local models can lose track of ids or let the lease run out; the server then answers `not_found` or `lock_lost`, which the model sees as a normal tool result.

## Running the HTTP example

```
powershell -File build/examples/agent-http.ps1 [-OutFile frame0.png]
```

It prints each step and `PASS`, and exits 0, or prints `FAILED: <reason>` and exits 1. The lock is released in every case.

## Tests

`tests/Cutlass.IntegrationTests/Examples` runs `agent-http.ps1` against an in-process headless host (both the split and the marker path), checks that both Python harnesses parse and stay under 150 lines, and runs `check_harness.py`, which loads each harness with a stub SDK and a fake transport serving the real `/v1/tools` document of its vendor format. No test calls a model.
