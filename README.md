# AION — Agent Swarm Operating System

**Self-building, self-hosting AI agent system.**
Build your own AI workforce. One binary. Clean, modular C#.

## Quick Start

```bash
# Prerequisites
dotnet --version  # needs 9.0+
node --version     # needs 20+

# Build backend
cd Aion.Core && dotnet build && cd ..
cd Aion.Host && dotnet build && cd ..

# Build frontend
cd aion-ui && npm install && npm run build && cd ..

# Run
cd Aion.Host && dotnet run
```

Open http://localhost:6969 — the dashboard loads immediately.
Go through the Setup Wizard at /setup.

## Architecture

```
Aion.Core/         - Core library (zero external runtime deps)
  AgentLoop.cs     - Main execution loop with retry/confidence
  Repair/          - JSON repair, type coercion, content sanitizer
  Safety/          - Capability gate, rate limiter
  Memory/          - SQLite memory store, plan store, logger
  Tools/           - Tool registry + built-in tools
  Services/        - LLM service, prompt builder
  Mesh/            - WebSocket hubs for agent communication
  Migrations/      - SQLite schema creation
  Models/          - Data models (need from AION_spec.md)
  CLI/             - Command-line interface
  Configuration/   - Config manager

Aion.Host/         - Web server (ASP.NET 9)
  Program.cs       - Startup, DI, middleware
  Controllers/     - REST API

aion-ui/           - React frontend
  src/
    contexts/      - WebSocket, Auth providers
    pages/         - Dashboard, Logs, Settings, Setup Wizard
    components/    - Layout, ErrorBoundary, LoadingSkeleton
```

## Ports

| Port | Purpose | Access |
|------|---------|--------|
| 6969 | REST API | All interfaces |
| 6970 | WebSocket mesh | All interfaces |
| 6971 | Setup wizard | 127.0.0.1 only |

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | /api/health | System health |
| GET | /api/version | Version info |
| GET | /api/tools | List registered tools |
| GET | /api/config | Get config (masked) |
| POST | /api/run | Execute a tool directly |
| POST | /api/agents/{id}/message | Send agent a message |
| POST | /api/agents/{id}/task | Assign agent a task |
| GET | /api/memory/search | Semantic search memory |
| POST | /api/memory/store | Store memory entry |
| GET | /api/logs | Query system logs |

## Configuration

Config lives at `~/.aion/aion-config.json`. Manage via CLI or Settings UI.

```bash
# CLI
aion config show
aion config set llm.provider openai
aion config set llm.model gpt-4
```

## WebSocket Protocol

Connect to `ws://host:6970/hub/mesh` and send JSON messages:

```json
{"type": "register", "agentId": "worker-1", "displayName": "Worker 1"}
{"type": "message", "to": "#general", "body": {"text": "hello"}}
```

Server sends: `welcome`, `broadcast`, `deliver`, `agent_status`, `system`, `error`.

## License

Proprietary. DS Forgeworks Ltd.
