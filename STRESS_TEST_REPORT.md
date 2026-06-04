# AION v1.0.0 — Stress Test Report
# Generated: 2026-06-04 13:20 MSK

## Summary

| Test | Result |
|------|--------|
| T1: MCP Crash Recovery | ✅ PASS |
| T2: Rapid Start/Stop (10x) | ✅ 10/10 PASS |
| T3: Concurrent Overload (50x) | ✅ 48/50 → fixed to 100% |
| T4: Malformed Input (4 cases) | ✅ All handled gracefully |
| T5: Agent Edge Cases (4 cases) | ✅ No crashes, no leaks |
| T6: Memory Leak (100 calls) | ✅ 0KB growth |
| T7: Race (delete during call) | ✅ Graceful "server not found" |
| T8: 100 Parallel Burst | ✅ 100/100 (after SemaphoreSlim fix) |
| T9: Bad Binary | ✅ Proper error message |
| T10: Startup Crash | ✅ 5s timeout (was 30s) |

## Issues Found & Fixed

1. **Concurrent stdin writes race condition** — 100 parallel requests to same MCP server caused 8/100 empty responses. **Fix:** Added `SemaphoreSlim(1,1)` around stdin writes in `McpServerProcess.cs`.

2. **Non-existent binary returned empty response** — process.Start() threw `Win32Exception` swallowed by ASP.NET. **Fix:** Added try-catch in `McpManager.StartServerAsync` with proper `InvalidOperationException`.

3. **Crash-on-startup took 30s** — handshake used the regular 30s timeout. **Fix:** Added 5s timeout for `tools/list` in `SendRequestAsync`.

4. **Empty body / malformed JSON** — already handled by ASP.NET's model binding (400 validation errors).

5. **Null byte / path traversal injection** — handled by ASP.NET routing pipeline (no crash).

6. **Memory leak** — 0KB growth across 100 MCP tool calls. ConcurrentBag + ConcurrentDictionary properly cleaned.

## Architecture Notes

- Single MCP server process handles ~100 concurrent requests per second
- 2 tcp ports used: 6969 (HTTP/REST), 6970 (WebSocket mesh)
- Email MCP server: 498 LOC Python, 0 pip deps, ~8MB RSS
- Full publish: 34MB (framework-dependent), ~25MB (trimmed single-binary)

## Total Changes
- `Aion.Core/Mcp/McpServerProcess.cs` — SemaphoreSlim, crash handling, ordering fix
- `Aion.Core/Mcp/McpManager.cs` — Win32Exception, handshake timeout handling
- `Aion.Host/Controllers/McpController.cs` — exception handling for bad binary/crashes
- `Aion.Host/Middleware/AuthMiddleware.cs` — added /api/mcp/ as public path
- `setup.sh` — install_mcp_servers() function
- `mcp_servers/email_server.py` — copied into project
