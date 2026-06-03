# AION Tool Building Guide

## Overview

AION agents can use, register, and create tools. A tool is any unit of work an agent can invoke — from a simple calculator to a multi-language sandboxed code runner.

Tools are registered in `ToolRegistry` and exposed to the agent via the system prompt. Agents see available tools and can request to create new ones at runtime.

---

## 1. The ITool Interface

Every tool implements this interface:

```csharp
public enum ToolCapability { ReadOnly = 1, Write = 2, Execute = 3, Root = 4 }

public interface ITool
{
    string Name { get; }                        // Unique tool name
    string Description { get; }                 // What it does (shown to agent)
    ToolCapability Capability { get; }          // Permission level
    Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default);
}
```

**Name** — snake_case, all lowercase. Used by agents to invoke via JSON.  
**Description** — one-line summary. Agents use this to decide which tool fits the task.  
**Capability** — determines what the SafetyGate allows:
- `ReadOnly (1)` — safe, no side effects (calculator, web fetch, clock)
- `Write (2)` — modifies state (file write, config change)
- `Execute (3)` — runs arbitrary code (shell, sandbox)
- `Root (4)` — system-level changes (reserved)

**ExecuteAsync** — receives a string input (JSON or raw text), returns `ToolResult`.

### ToolResult

```csharp
public record ToolResult(bool Success, string? Output, string? Error, double Confidence = 1.0)
```

- `ToolResult.Ok(output)` — success with text output
- `ToolResult.Fail(error)` — failure with error message

---

## 2. Registering a Tool (Built-in)

In `Program.cs`, add your tool:

```csharp
toolRegistry.Register(new MyTool());
toolRegistry.RegisterAlias("short_name", "my_tool"); // optional alias
```

Existing built-in tools:
| Name | Aliases | Capability | Description |
|------|---------|------------|-------------|
| `web_fetch` | `search_web` | ReadOnly | Fetch URL and return clean text |
| `calculator` | `calculate` | ReadOnly | Evaluate math expressions |
| `now` | `time` | ReadOnly | Current date/time/timezone |
| `shell_command` | `exec`, `sh` | Execute | Shell command with timeout |
| `sandbox` | `code` | Execute | Run code in Docker/host (Python, JS, Go, Ruby, Rust, .NET, sh) |

---

## 3. Choosing a Language for Dynamic Tools

Agents can create tools at runtime by writing code and registering via `POST /api/tools/create`. Here's the suitability guide:

| Language | Speed | Fine-Tuning | Best For | Notes |
|----------|-------|-------------|----------|-------|
| **Python** | Medium | ✅ Easy | Data processing, APIs, files, ML inference | Best default. Largest ecosystem, easiest to debug, most libraries. Sandbox image: `python:3.12-slim` (~130MB) |
| **JavaScript/Node** | Fast | ⚠️ Moderate | Web scraping, JSON processing, async I/O | Good for fetch-heavy pipelines. Sandbox image: `node:20-slim` (~190MB) |
| **Go** | Very Fast | ❌ Hard | CLI tools, network services, performance-critical | Compiles to binary, fastest execution. Best for high-throughput ops. Sandbox image: `golang:1.23-alpine` (~400MB) |
| **Ruby** | Medium | ✅ Easy | File processing, text generation | Slower than Python for similar tasks. Smaller ecosystem. Sandbox image: `ruby:3.3-slim` (~150MB) |
| **Rust** | Fastest | ❌ Hardest | System tools, parsing, memory-safe ops | Compile time is long (~30s for simple programs). Best for tools that run repeatedly. Sandbox image: `rust:1.80-slim` (~600MB) |
| **Shell/sh** | Fast | N/A | Glue, piping, file ops, orchestration | No safety guarantees (runs bare). Only use for simple orchestration. Sandbox image: `alpine:3.20` (~8MB) |
| **C# (.NET)** | Fast | ⚠️ Moderate | AION platform integration, complex logic | Uses `dotnet script` — needs SDK image (~1.8GB). Best for tools that need AION's own types. Sandbox image: `mcr.microsoft.com/dotnet/sdk:9.0` |

### Decision Flow:

```
Need maximum speed on repeated calls? → Rust or Go
Need quick prototyping + data processing? → Python
Need web scraping/async I/O? → Node
Need to pipe shell commands? → Shell (but use sandbox if Docker available)
Need AION platform types? → C#
Just need it to work right now? → Python
```

---

## 4. Creating a Dynamic Tool (Runtime)

### Via API

```http
POST /api/tools/create
Content-Type: application/json

{
  "name": "validate_email",
  "description": "Check if an email address is valid format and domain exists",
  "code": "import re, socket\n
addr = user_input.strip()\n
if not re.match(r'^[\\w\\.-]+@[\\w\\.-]+\\.\\w+$', addr):\n
    print('INVALID_FORMAT')\n
else:\n
    domain = addr.split('@')[1]\n
    try:\n
        socket.getaddrinfo(domain, 25)\n
        print('VALID')\n
    except:\n
        print('DOMAIN_NOT_FOUND')",
  "language": "python"
}
```

Response:
```json
{ "ok": true, "name": "validate_email" }
```

### Via Agent

An agent can write and register a tool by calling the shell_command to POST to the API:

```json
{
  "tool": "shell_command",
  "input": "curl -s -X POST http://localhost:6969/api/tools/create -H 'Content-Type: application/json' -d '...'"
}
```

Once registered, the tool appears in the agent's available tool list immediately.

### Deleting a Tool

```http
DELETE /api/tools/{name}
```

---

## 5. How Input is Passed to Dynamic Tools

When a dynamic tool is called, the agent's input is passed via:

1. **Command-line arguments** — `user_input = sys.argv[1:]` (preferred)
2. **AION_INPUT environment variable** — fallback if no args
3. **stdin** — last resort if both above are empty

In Python, the wrapping code automatically provides `user_input` as a variable. Your tool code just uses it:

```python
# user_input is automatically available
result = calculate_something(user_input)
print(result)
```

---

## 6. Safety & Capability Levels

The `CapabilityGate` checks every tool call:

| Agent Level | Can Call |
|-------------|----------|
| 0 (Guest) | Nothing |
| 1 (Standard) | ReadOnly tools |
| 2 (Power) | ReadOnly + Write |
| 3 (Admin) | ReadOnly + Write + Execute |
| 4 (Root) | Everything |

Dynamic tools are created at `Execute` level (3). To promote to `Root`, register in `Program.cs`.

---

## 7. Tool Discovery (Finding Agents)

Agents find each other through the WebSocket mesh at `ws://localhost:6970/hub/mesh`.

### Mesh Agent Discovery

When an agent connects:
1. It sends a `register` message with its `agent_id`, `display_name`, and capabilities
2. The MeshHub broadcasts the join to all other agents
3. Agents maintain a local list of connected peers via `state.agents` (WebSocket context)

### To Find an Agent by Capability

1. Call `web_fetch` on the mesh info endpoint: `GET http://localhost:6969/agents`
2. The endpoint returns all registered agents with their capabilities
3. Filter by description or name to find the right agent

### Open-Plan Office Model (Inter-Agent Communication)

Agents share a WebSocket hub — like an open office where anyone can talk to anyone:

- **Broadcast:** Send a message to all agents (announcement/status)
- **Direct message:** Send to a specific agent by `agent_id`
- **Join/Leave:** Automatic notifications when agents connect/disconnect
- **Rooms:** Agents can create private rooms for team tasks

To send a message to another agent, use the `mesh` tool:
```json
{
  "tool": "mesh",
  "input": "{\"action\": \"send\", \"target\": \"agent_name\", \"message\": \"hello\"}"
}
```

---

## 8. Quick Reference

| Action | Method |
|--------|--------|
| List all tools | `GET /api/tools` |
| Run a tool | `POST /api/run {"tool": "...", "input": "..."}` |
| Create a tool | `POST /api/tools/create` |
| Delete a tool | `DELETE /api/tools/{name}` |
| List agents | `GET /api/agents` |
| Message an agent | `POST /api/agents/{id}/message` |
| List models | `GET /api/models` |
| Health check | `GET /api/health` |
