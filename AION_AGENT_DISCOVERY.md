# AION Agent Discovery & Mesh Communication

## Overview

AION uses a WebSocket mesh — like an **open-plan office**. Agents sit in the same room, can hear broadcasts, direct-message each other, and form teams. There's no central directory — agents discover each other by listening.

---

## How Agents Find Each Other

### 1. Automatic Registration

When an agent connects to the mesh, it sends:

```json
{
  "type": "register",
  "agent_id": "data_extractor_1",
  "display_name": "Data Extractor",
  "capabilities": ["web_fetch", "python", "sandbox"],
  "status": "idle",
  "model": "qwen3.5:4b"
}
```

The hub:
1. Adds the agent to the active list
2. Broadcasts a `join` event to all other agents
3. Sends a `welcome` with the current roster

### 2. Listing Agents

**Via API:**
```http
GET /api/agents
```
Returns all registered agents with their capabilities and status.

**Via WebSocket:**
```json
// Request
{ "type": "status", "target": "mesh" }

// Response
{
  "type": "status",
  "agents": [
    { "agent_id": "ext_1", "display_name": "Data Extractor", "status": "idle" },
    { "agent_id": "val_1", "display_name": "Validator", "status": "busy" }
  ]
}
```

### 3. Finding by Capability

An agent asks the mesh:
```json
{
  "type": "message",
  "target": "hub",
  "body": {
    "action": "find",
    "capability": ["python", "sandbox"]
  }
}
```

The hub responds with matching agents.

---

## The Open-Plan Office Model

```
         ┌─────────────────────────────────────┐
         │          WebSocket Mesh Hub          │
         │         ws://localhost:6970          │
         └─────────────────────────────────────┘
                      │    │    │
         ┌────────────┘    │    └────────────┐
         ▼                 ▼                 ▼
   ┌──────────┐     ┌──────────┐     ┌──────────┐
   │  Agent A  │◄───▶│  Agent B  │◄───▶│  Agent C  │
   │ (scraper) │     │(analyzer)│     │(reporter) │
   └──────────┘     └──────────┘     └──────────┘
```

### Communication Types

| Type | Description | Example |
|------|-------------|---------|
| **Broadcast** | Say to everyone | "I'm going offline for maintenance" |
| **Direct message** | Whisper to one agent | "Can you validate this CSV?" |
| **Room** | Private group channel | Team working on a specific task |
| **Status** | Heartbeat / availability | "busy", "idle", "offline" |

---

## Agent Communication Protocol

### Send a Direct Message

```json
{
  "type": "message",
  "target": "agent_42",
  "body": {
    "text": "I need help parsing this JSON. Can you run a validation?",
    "attachments": ["data.json"]
  }
}
```

### Broadcast to All

```json
{
  "type": "broadcast",
  "body": {
    "text": "New task available: parse 500 emails. Need Python agent.",
    "priority": "normal"
  }
}
```

### Join a Room

```json
{
  "type": "join",
  "room": "task_123"
}
```

Agents in the same room can't hear conversations outside it.

---

## Finding an Agent: Practical Steps

When an agent needs to find another agent:

### Step 1: Check Local Cache
```json
{
  "tool": "web_fetch",
  "input": "{\"url\": \"http://localhost:6969/api/agents\"}"
}
```

### Step 2: Ask the Mesh
Send a status request via WebSocket to get real-time status of all connected agents.

### Step 3: Direct Message the Right One
```json
{
  "type": "message",
  "target": "found_agent_id",
  "body": { "text": "Task details...", "reply_to": "my_agent_id" }
}
```

### Step 4: Wait for Response (with timeout)
The target agent receives the message, processes it, and replies. If no response within 30s, the sender moves on or escalates.

---

## Capability Discovery

Each agent advertises its tools and skills when registering. Full capability list:

| Capability | Meaning |
|------------|---------|
| `web_fetch` | Can fetch URLs |
| `python` | Can run Python code (via sandbox) |
| `node` | Can run JavaScript |
| `go` | Can run Go code |
| `rust` | Can run Rust code |
| `shell` | Can execute shell commands |
| `sandbox` | Can run arbitrary code in isolation |
| `mesh` | Can send/receive mesh messages |
| `memory` | Can read/write agent memory |
| `calculator` | Can evaluate expressions |
| `time` | Knows current time/dates |

---

## Reliability

- **Reconnection:** Agents that disconnect retain their ID for 60 seconds. If they reconnect within that window, they resume with the same identity and message history.
- **Heartbeats:** Agents ping every 15 seconds. If no ping for 60 seconds, the agent is marked offline and other agents are notified.
- **Delivery guarantee:** Messages are delivered at-least-once. Duplicate detection happens via message IDs.
- **Catch-up:** Reconnecting agents receive missed messages from a buffer (last 100 per-connection).
