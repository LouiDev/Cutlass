#!/usr/bin/env python3
"""Drive Cutlass with any OpenAI-compatible chat model (docs/AGENT_INTEGRATION.md 9.5).

Works with Ollama, LM Studio, vLLM, OpenAI, Azure OpenAI, Groq, Mistral and every other endpoint with OpenAI-style
tool calling. It loads GET /v1/tools?format=openai (the tool definitions) and GET /v1/instructions (the workflow, as
the system prompt), then runs a tool loop where every call is POST /v1/call {tool, args} with the bearer token. It
keeps the lockId from cutlass_acquire_lock and sends it as X-Cutlass-Lock, renews the lease while the model thinks,
saves image results to PNG files (the model gets the path and size) and releases the lock on exit, also on Ctrl+C;
exit code 0 when the model answers without a tool call (it reports done), else 1.

Endpoint and token default to <data>/agent/endpoint.json and the token file it names (<data>: %LOCALAPPDATA%/Cutlass
on Windows, ${XDG_DATA_HOME:-~/.local/share}/cutlass elsewhere): start Cutlass with agent connections on, or
`Cutlass.Agent.Mcp serve --project x.cutlass`, first. Needs only `pip install openai`. See README.md here.
  python agent-openai-compatible.py --model gpt-oss:20b --task "Add a centred title 'Hello' over the first second"
"""
import argparse, base64, json, os, signal, sys, tempfile, threading, time, urllib.error, urllib.request
from pathlib import Path
from openai import APIError, OpenAI

FALLBACK_INSTRUCTIONS = ("You edit a video in Cutlass through tools (this text is used only without /v1/instructions). "
    "Call cutlass_status, then cutlass_acquire_lock; inspect with cutlass_describe_project; wrap changes in batches; "
    "frames are integers, ids come from the project. Render a frame to check; call cutlass_release_lock at the end.")
HARNESS_NOTE = ("\nThis harness saves images you render to files and tells you their path and size; you cannot see "
                "them. When the task is complete, release the lock and answer with a short summary and no tool call.")


def data_dir():
    if os.name == "nt":
        return Path(os.environ["LOCALAPPDATA"]) / "Cutlass"
    xdg = os.environ.get("XDG_DATA_HOME", "")
    return (Path(xdg) if os.path.isabs(xdg) else Path.home() / ".local" / "share") / "cutlass"


class Cutlass:
    """The Cutlass HTTP API with the bearer token and the lock header (urllib only, no proxy for loopback)."""
    def __init__(self, base_url, token, agent):
        self.base, self.token, self.agent, self.lock = base_url.rstrip("/") + "/", token, agent, None
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def request(self, method, path, body=None):
        headers = {"Authorization": "Bearer " + self.token, "X-Cutlass-Agent": self.agent}
        headers.update({"X-Cutlass-Lock": self.lock} if self.lock else {})
        data = None if body is None else json.dumps(body).encode("utf-8")
        headers.update({"Content-Type": "application/json"} if data else {})
        req = urllib.request.Request(self.base + path.lstrip("/"), data=data, method=method, headers=headers)
        try:
            with self.http.open(req, timeout=600) as r:
                return r.status, r.headers.get_content_type(), r.read()
        except urllib.error.HTTPError as e:
            return e.code, e.headers.get_content_type(), e.read()

    def get_json(self, path):
        status, _, raw = self.request("GET", path)
        if status != 200:
            raise RuntimeError(f"GET {path} gave HTTP {status}: {raw[:300]!r}")
        doc = json.loads(raw)  # a bare document or the {revision, result} envelope
        return doc["result"] if isinstance(doc, dict) and "result" in doc else doc

    def call(self, tool, args, out_dir):
        """Runs one tool; returns (text for the model, one-line summary)."""
        status, ctype, raw = self.request("POST", "v1/call", {"tool": tool, "args": args})
        if ctype == "image/png":
            return self.save_png(raw, tool, {}, out_dir)
        try:
            doc = json.loads(raw)
        except ValueError:
            return raw.decode("utf-8", "replace"), f"HTTP {status}, not JSON"
        if err := doc.get("error"):
            return json.dumps(doc), f"error {err.get('code')}: {err.get('message', '')[:120]}"
        result = doc.get("result")
        if isinstance(result, dict) and result.get("pngBase64"):
            meta = {k: v for k, v in result.items() if k != "pngBase64"}
            return self.save_png(base64.b64decode(result["pngBase64"]), tool, meta, out_dir)
        if tool == "cutlass_acquire_lock" and isinstance(result, dict) and result.get("lockId"):
            self.lock = result["lockId"]  # sent as X-Cutlass-Lock from now on
        self.lock = None if tool == "cutlass_release_lock" and status == 200 else self.lock
        brief = doc.get("undoLabel") or json.dumps(result)[:100]
        return json.dumps(doc)[:30000], f"ok rev {doc.get('revision')} {brief}"

    def save_png(self, png, tool, meta, out_dir):
        out_dir.mkdir(parents=True, exist_ok=True)
        (path := out_dir / f"{tool}-{int(time.time() * 1000)}.png").write_bytes(png)
        meta = {**meta, "savedTo": str(path), "bytes": len(png)}
        return json.dumps({"result": meta}), f"saved {path} ({len(png)} bytes)"

    def heartbeat(self, stop):  # leases are 15 s or longer, so renew every 10 s while the model thinks
        while not stop.wait(10):
            if self.lock:
                self.request("POST", "v1/lock/renew", {})


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--base-url", default="http://localhost:11434/v1", help="OpenAI-compatible endpoint (Ollama)")
    p.add_argument("--model", required=True, help="a tool-calling model, for example gpt-oss:20b")
    p.add_argument("--api-key", default=os.environ.get("OPENAI_API_KEY", "ollama"))
    p.add_argument("--task", required=True, help="what the model should do in Cutlass")
    p.add_argument("--endpoint", help="Cutlass base URL (default: from endpoint.json)")
    p.add_argument("--token", help="bearer token (default: the token file endpoint.json names)")
    p.add_argument("--out-dir", default=os.path.join(tempfile.gettempdir(), "cutlass-harness"))
    p.add_argument("--max-steps", type=int, default=40)
    a = p.parse_args()
    if not (a.endpoint and a.token):
        info = json.loads((data_dir() / "agent" / "endpoint.json").read_text(encoding="utf-8"))
        a.endpoint = a.endpoint or info["baseUrl"]
        a.token = a.token or Path(info["tokenFile"]).read_text(encoding="utf-8").strip()
    cut = Cutlass(a.endpoint, a.token, f"{a.model} (OpenAI-compatible harness)")
    tools = cut.get_json("v1/tools?format=openai")
    try:
        system = cut.get_json("v1/instructions")["text"]
    except (RuntimeError, KeyError, TypeError):
        system = FALLBACK_INSTRUCTIONS
    print(f"cutlass: {a.endpoint}, {len(tools)} tools; model {a.model} at {a.base_url}")
    llm = OpenAI(base_url=a.base_url, api_key=a.api_key)
    messages = [{"role": "system", "content": system + HARNESS_NOTE}, {"role": "user", "content": a.task}]
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(1))
    threading.Thread(target=cut.heartbeat, args=(stop := threading.Event(),), daemon=True).start()
    try:
        for step in range(1, a.max_steps + 1):
            msg = llm.chat.completions.create(model=a.model, messages=messages, tools=tools).choices[0].message
            calls = msg.tool_calls or []
            turn = {"role": "assistant", "content": msg.content or ""}
            wire = [c.model_dump(include={"id", "type", "function"}) for c in calls]
            messages.append({**turn, "tool_calls": wire} if calls else turn)
            if not calls:
                print(f"done after {step - 1} tool steps: {msg.content}")
                return 0
            for c in calls:
                try:
                    args = json.loads(c.function.arguments or "{}")
                    text, line = cut.call(c.function.name, args, Path(a.out_dir))
                except ValueError as e:
                    text, line = f"invalid JSON arguments: {e}", "invalid JSON arguments"
                print(f"[{step}] {c.function.name} {c.function.arguments} -> {line}")
                messages.append({"role": "tool", "tool_call_id": c.id, "content": text})
        print(f"stopped: no final answer after {a.max_steps} steps")
        return 1
    except (KeyboardInterrupt, APIError) as e:  # Ctrl+C, or the endpoint failed (context too small, ...)
        print(f"stopped: {e!r}")
        return 1
    finally:
        stop.set()
        if cut.lock:
            cut.request("POST", "v1/lock/release", {})
            print("released the lock the model still held")


if __name__ == "__main__":
    sys.exit(main())
