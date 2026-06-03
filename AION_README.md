# AION — Quick Start & Walkthrough

## What is AION?

AION is a self-building Agent Swarm Operating System. It's designed to run on your own hardware (even a weak machine with 4b models), orchestrate multiple AI agents, give them tools, sandbox their code, and let them communicate.

Unlike cloud services, AION owns everything — no subscriptions, no data leaving your machine, no API bills.

---

## First Install

```bash
git clone https://github.com/DS-Forgeworks/aion.git
cd aion
bash setup.sh
```

The script:
1. Detects your OS (Linux, macOS, Windows)
2. Installs curl, Node.js 20, and .NET SDK 9 if missing
3. Builds the backend (C#) and frontend (React)
4. Starts the server on `http://localhost:6969`
5. Detects or guides you through Ollama installation
6. Shows you the dashboard URL

**On a weak system:** The setup completes in ~3-5 minutes. Models are small. The sandbox still works for offloading logic.

---

## Architecture

```
┌─────────────┐     ┌──────────────┐     ┌──────────────┐
│  Web UI     │────▶│  AION API    │────▶│  Agent Loop  │
│  (React)    │     │  (C#/ASP.NET)│     │  (C#)        │
└─────────────┘     └──────┬───────┘     └──────┬───────┘
                           │                     │
                    ┌──────▼───────┐      ┌──────▼───────┐
                    │  ToolRegistry │      │  LLM Service │
                    │  (ITool[])    │      │  (Ollama/API)│
                    └──────┬───────┘      └──────────────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
        ┌─────────┐ ┌───────────┐ ┌──────────┐
        │ Shell   │ │ Sandbox   │ │ WebFetch │
        │ Tool    │ │ (Docker)  │ │ Tool     │
        └─────────┘ └───────────┘ └──────────┘
```

---

## Use Cases

### On a Weak Machine (4b models, 8GB RAM)

The AI itself can't do complex reasoning. But with tools:

1. **Validation tasks:** Agent writes a Python validation script, sandbox runs it, agent reads the result. Faster and more reliable than having the LLM reason through it.

2. **Data extraction:** Agent uses `web_fetch` to grab pages, `shell_command` to grep/awk, writes a script to structure the data. The LLM coordinates; the tools do the work.

3. **Multi-step orchestration:** Agent breaks a complex task into steps, sandbox-validates each step's code, creates tools for repeatable subtasks, delegates to other mesh agents.

4. **Agent-created tools:** Agent writes a tool to solve a recurring problem, registers it, and future agents use it without reinventing.

### On a Strong Machine (7b+ models, 16GB+ VRAM)

Same architecture, but the LLM can directly handle more reasoning. Agents become advisors rather than coordinators.

---

## Key Concepts

### Agents
Units of work. Each agent has a name, capability level, and access to tools. Agents live on the WebSocket mesh and can talk to each other.

### Tools
Functions an agent can call. Built-in: web_fetch, calculator, time, shell, sandbox. Dynamic: agents can create their own.

### Sandbox
Docker-isolated code execution. Network disabled, memory limited, auto-cleaned. Supports Python, JS, Go, Ruby, Rust, .NET, shell. Falls back to local if Docker is missing.

### Mesh
WebSocket hub connecting all agents. Like an open-plan office — agents broadcast status, direct-message peers, form rooms. The hub handles reconnection, dedup, and message ordering.

### Capability Levels
- **1 (ReadOnly):** Safe tools only (web_fetch, calculator)
- **2 (Write):** Can modify state
- **3 (Execute):** Can run arbitrary code (shell, sandbox)
- **4 (Root):** System-level changes

---

## Ports

| Port | Purpose |
|------|---------|
| 6969 | HTTP API + Web UI dashboard |
| 6970 | WebSocket agent mesh hub |

---

## Configuration

Config file: `~/.aion/aion-config.json`

```json
{
  "Version": 1,
  "Workspace": "~/.aion/workspace",
  "Language": "en",
  "Llm": {
    "Provider": "ollama",
    "Model": "qwen3.5:4b",
    "Endpoint": "http://127.0.0.1:11434",
    "ApiKey": null
  },
  "Safety": {
    "SafeMode": true,
    "ShellEnabled": false
  },
  "Mesh": {
    "Enabled": true,
    "Port": 6970
  }
}
```

Edit via Web UI → Settings page, or directly in the file and restart.

---

## Running Again

```bash
./aion.sh          # Linux/macOS
aion.cmd           # Windows (double-click)
```

Or from anywhere after first run:
```bash
./dist/Aion.Host.dll  # via dotnet
```

---

## API Reference

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Server status |
| `/api/version` | GET | Version info |
| `/api/agents` | GET | List agents |
| `/api/agents/{id}/message` | POST | Send message to agent |
| `/api/tools` | GET | List all tools |
| `/api/tools/create` | POST | Register a new tool |
| `/api/tools/{name}` | DELETE | Remove a tool |
| `/api/run` | POST | Execute a tool directly |
| `/api/config` | GET | Current config |
| `/api/setup` | POST | Save setup wizard config |
| `/api/models` | GET | List Ollama models |
| `/api/memory/search` | GET | Search agent memory |
| `/api/memory/store` | POST | Store a memory entry |
| `/api/logs` | GET | Query logs |

---

## Troubleshooting

**"No models found"** — Ollama isn't running or has no models pulled.  
`ollama pull qwen3.5:4b`

**Sandbox not working** — Docker isn't installed or running. Falls back to local execution.  
`docker ps` to check.

**Server won't start** — Port 6969 already in use. Kill the old process:  
`pkill -f "Aion.Host"`

**Web UI blank** — Frontend not built. Run from project root:  
`cd aion-ui && npm run build && cp -r dist/* ../dist/wwwroot/`
