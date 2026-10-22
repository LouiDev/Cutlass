"""Offline check of a reference harness in build/examples (docs/AGENT_INTEGRATION.md 9.5), run by PythonExampleTests.

usage: python -I check_harness.py <harness.py> <openai|anthropic> <tools.json> <out-dir>

No network and no SDK: the vendor SDK module is replaced by a stub, and Cutlass.request is replaced by a fake that
answers with the given tools document (rendered by ToolExport, so it is exactly what GET /v1/tools?format=... serves)
and with sample envelopes. Checks that the tool loading yields the vendor shape unchanged, that envelopes and bare
documents both load, that the lock id is captured and dropped, and that image results become files.
"""
import base64, importlib.util, json, re, sys, types
from pathlib import Path

sys.dont_write_bytecode = True  # never leave __pycache__ beside the examples
harness, vendor, tools_file, out_dir = sys.argv[1], sys.argv[2], sys.argv[3], Path(sys.argv[4])
for sdk in ("openai", "anthropic"):  # stubs: the check must not need the SDK
    stub = types.ModuleType(sdk)
    stub.OpenAI = stub.Anthropic = object
    stub.APIError = type("APIError", (Exception,), {})
    sys.modules[sdk] = stub
spec = importlib.util.spec_from_file_location("harness", harness)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

tools_doc = Path(tools_file).read_bytes()
png = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAwS2OUAAAAABJRU5ErkJggg==")
answers = {
    ("GET", "v1/tools?format=" + vendor): (200, "application/json", tools_doc),
    ("GET", "v1/instructions"): (200, "application/json", b'{"text": "Rules"}'),
    ("GET", "v1/status"): (200, "application/json", b'{"revision": 3, "warnings": [], "result": {"app": "Cutlass"}}'),
}
calls = {
    "cutlass_acquire_lock": (200, "application/json", b'{"revision": 3, "result": {"lockId": "L1", "phase": "held"}}'),
    "cutlass_split": (423, "application/json", b'{"revision": 3, "error": {"code": "lock_lost", "message": "Lost."}}'),
    "cutlass_render_frame": (200, "application/json", json.dumps(
        {"revision": 3, "result": {"frame": 15, "width": 1, "height": 1, "pngBase64": base64.b64encode(png).decode()}}).encode()),
    "cutlass_contact_sheet": (200, "image/png", png),
    "cutlass_release_lock": (200, "application/json", b'{"revision": 3, "result": {}}'),
}
sent = []


def fake_request(method, path, body=None):
    sent.append((method, path, body, cut.lock))
    if path == "v1/call":
        return calls[body["tool"]]
    return answers[(method, path)]


cut = mod.Cutlass("http://localhost:1/", "token", "check")
cut.request = fake_request

# Tool definitions: a list in the vendor shape, names legal for every vendor (^[a-zA-Z0-9_-]{1,64}$).
tools = cut.get_json("v1/tools?format=" + vendor)
assert isinstance(tools, list) and len(tools) > 40, type(tools)
for t in tools:
    if vendor == "openai":
        assert t["type"] == "function" and set(t["function"]) >= {"name", "description", "parameters"}, t
        name, schema = t["function"]["name"], t["function"]["parameters"]
    else:
        assert set(t) >= {"name", "description", "input_schema"}, t
        name, schema = t["name"], t["input_schema"]
    assert re.fullmatch(r"[a-zA-Z0-9_-]{1,64}", name) and name.startswith("cutlass_"), name
    assert schema["type"] == "object", name
names = {(t["function"] if vendor == "openai" else t)["name"] for t in tools}
assert {"cutlass_status", "cutlass_acquire_lock", "cutlass_release_lock", "cutlass_render_frame"} <= names

# Bare documents and envelopes both load.
assert cut.get_json("v1/instructions")["text"] == "Rules"
assert cut.get_json("v1/status")["app"] == "Cutlass"


def run(tool, args=None):
    reply = cut.call(tool, args or {}, out_dir)
    return reply[0], reply[-1]  # the text for the model and the summary line (Anthropic adds is_error between)


# The lock id is kept after acquire, sent with the next call, and dropped after release.
run("cutlass_acquire_lock", {"agentName": "check", "reason": "check"})
assert cut.lock == "L1"
text, line = run("cutlass_split", {"frame": 10})
assert sent[-1][3] == "L1" and line.startswith("error lock_lost"), line
for tool in ("cutlass_render_frame", "cutlass_contact_sheet"):
    text, line = run(tool, {"frame": 15})
    result = json.loads(text)["result"]
    assert "pngBase64" not in text and Path(result["savedTo"]).read_bytes() == png and result["bytes"] == len(png), text
run("cutlass_release_lock")
assert cut.lock is None
print("harness check passed:", Path(harness).name, vendor, len(tools), "tools")
