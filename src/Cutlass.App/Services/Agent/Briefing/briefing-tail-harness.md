Tools: you reach Cutlass over its local HTTP API from your harness ({client}).
- Base URL: {url}
- Every request needs the header "Authorization: Bearer <token>". Token: {token}
- Tool definitions in your harness's format: {tools_url}
- Call a tool: POST {url}v1/call with {"tool": "<name>", "args": { ... }}. The reply holds revision, warnings, result and error; image tools return result.pngBase64.
- The steps above use cutlass_status, cutlass_acquire_lock, cutlass_describe_project, cutlass_begin_batch and cutlass_end_batch, cutlass_render_frame and cutlass_audio_levels, and cutlass_release_lock.
- The workflow as text for a system prompt: {url}v1/instructions
- A runnable example loop: {example}
