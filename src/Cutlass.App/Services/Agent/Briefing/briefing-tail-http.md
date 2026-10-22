Tools: you reach Cutlass over its local HTTP API at {url} with curl. Every request needs the header "Authorization: Bearer $CUTLASS_TOKEN". Set the token once in your shell (bash), and never print it:
  {token}

The steps above as curl calls (JSON field names are camelCase):
1. Read the status, then take the lock and keep result.lockId for every change (wait if it reports busy):
   curl -s -H "Authorization: Bearer $CUTLASS_TOKEN" {url}v1/status
   curl -s -H "Authorization: Bearer $CUTLASS_TOKEN" -H "Content-Type: application/json" -d '{"agentName":"<your name>","reason":"<short reason>"}' {url}v1/lock/acquire
2. Describe the project:
   curl -s -H "Authorization: Bearer $CUTLASS_TOKEN" {url}v1/project/describe
3. Edit with the lock id between POST v1/batch/begin {"label":"<short label>"} and POST v1/batch/end {}, for example place media at frame 0:
   curl -s -H "Authorization: Bearer $CUTLASS_TOKEN" -H "X-Cutlass-Lock: <lockId>" -H "Content-Type: application/json" -d '{"mediaId":"<media id>","at":0,"mode":"overwrite","streams":"both"}' {url}v1/edit/place
4. Save a frame as a PNG file and look at it:
   curl -s -H "Authorization: Bearer $CUTLASS_TOKEN" -o frame.png "{url}v1/frame/30?width=640"
6. Release the lock:
   curl -s -H "Authorization: Bearer $CUTLASS_TOKEN" -H "X-Cutlass-Lock: <lockId>" -H "Content-Type: application/json" -d '{}' {url}v1/lock/release

Every tool is also reachable as POST {url}v1/call with {"tool": "<name>", "args": { ... }}; the list is at {url}v1/tools?format=markdown, and every endpoint is described in {docs} (section 4).
