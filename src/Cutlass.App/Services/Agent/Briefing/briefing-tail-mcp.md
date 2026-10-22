Tools: you reach Cutlass through the MCP server "cutlass" in {client}. The steps above map to these tools:
1. cutlass_status, then cutlass_acquire_lock
2. cutlass_describe_project
3. cutlass_begin_batch and cutlass_end_batch
4. cutlass_render_frame and cutlass_audio_levels
6. cutlass_release_lock

If you cannot see any cutlass_* tools, the MCP server is not registered in this session. Register it, then restart the session:
{registration}
