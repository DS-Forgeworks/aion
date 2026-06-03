# AION Getting Started Guide

Welcome. AION is a self-hosted agent operating system.
This guide helps you — and your AI agent — get started immediately.

---

## Quick Start Flow (30 seconds to value)

1. Open **http://localhost:6969**
2. The Setup Wizard appears on first run
3. Select a model from the dropdown (populated from your Ollama)
4. That's it. You can now chat with your agent.

---

## What Makes AION Different

- **No subscriptions.** Runs on your hardware, your models.
- **Self-building.** Agents create their own tools at runtime.
- **Sandboxed execution.** Code runs in Docker containers — safe and clean.
- **Agent mesh.** Multiple agents can discover and communicate with each other.

---

## Things to Try Immediately

### "Analyze this data"
Upload a CSV or text file to the chat, then say:
> "Analyze this data and tell me what it contains"

The agent reads the file, processes it via sandbox, and gives you results.

### "Create a Telegram bot"
Ask the agent:
> "Connect me to Telegram so I can message you from my phone"

The agent follows the Telegram bridge guide and sets it up.

### "Build me a tool"
> "Create a tool called 'weather_check' that fetches weather from wttr.in"

The agent writes the code, registers it, and it's immediately available.

### "Find another agent"
> "Are there any other agents connected?"

The agent queries the mesh and lists all connected peers.

---

## File Processing

AION accepts file uploads. Drop a file into the chat:

| File Type | What AION Does |
|-----------|----------------|
| CSV | Parses and analyses rows, columns, stats |
| JSON | Pretty-prints, validates, summarises |
| TXT / MD | Reads and responds to content |
| Python / JS | Reviews code, suggests improvements |
| Images | Reads metadata (OCR via sandbox) |

---

## Chat Tips

- **Edit your messages** — hover over them, click ✏️
- **Retry failed responses** — hover over assistant replies, click 🔄
- **Start fresh** — click the + button to begin a new conversation
- **Browse history** — click ☰ to see all past conversations

---

## First-Run Tour

When you first open AION, you'll see:

1. **A clean dark dashboard** with an agent panel on the left and chat on the right
2. **A model selector** — pick your Ollama model
3. **No agents connected** — that's normal. Agents connect when they register via WebSocket
4. **Start typing** — just say hello

Your first message creates a conversation. Each conversation is saved.

---

## Documentation Files

AION ships with documentation the agent can read:

| File | What It Covers |
|------|----------------|
| `AION_README.md` | Architecture, key concepts |
| `AION_TOOLS.md` | Tool building, language selection, API reference |
| `AION_AGENT_DISCOVERY.md` | Mesh communication, finding agents |
| `AION_TELEGRAM_BRIDGE.md` | Connecting to Telegram |
| `AION_GETTING_STARTED.md` | This file |

You can ask the agent: *"Read the getting started guide and tell me what I can do."*

---

## What's Next

- **Add more models** via Ollama: `ollama pull llama3:8b`
- **Connect Slack** — ask the agent to set it up
- **Schedule tasks** — tell the agent to check for something every hour
- **Create custom tools** — agents build their own tools

Need help? Just ask the agent. It has access to all documentation.
