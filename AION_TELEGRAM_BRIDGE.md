# AION Telegram Bridge

This document tells AION agents how to connect to Telegram.
It also guides the user through what needs to be done.

---

## What This Does

Connects AION to Telegram so you can:
- Chat with your AION agents from your phone
- Receive notifications when tasks complete
- Send commands and get results remotely

---

## User Setup (do this once)

### 1. Create a Telegram Bot

1. Open Telegram and search for **@BotFather**
2. Send: `/newbot`
3. Choose a name (e.g. "My AION Agent")
4. Choose a username (must end in `bot`, e.g. `my_aion_bot`)
5. BotFather will give you a **token** like: `123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11`

Save this token. You'll need it below.

### 2. Find Your Chat ID

1. Start a chat with your new bot
2. Send any message
3. Visit: `https://api.telegram.org/bot<YOUR_TOKEN>/getUpdates`
4. Find your chat ID in the JSON response (look for `"chat":{"id":123456789}`)

### 3. Configure AION

In the AION Settings page, add:
- **Telegram Bot Token:** the token from BotFather
- **Telegram Chat ID:** your chat ID from step 2

Or set via API:
```
POST /api/settings
{
  "telegram_token": "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11",
  "telegram_chat_id": "123456789"
}
```

---

## Agent Instructions

When a user asks you to connect to Telegram or send a Telegram message:

### Sending a Message

Use the `shell_command` tool:
```bash
curl -s -X POST "https://api.telegram.org/bot<TELEGRAM_TOKEN>/sendMessage" \
  -d "chat_id=<CHAT_ID>" \
  -d "text=<YOUR_MESSAGE>" \
  -d "parse_mode=Markdown"
```

Get the token and chat ID from:
- `GET /api/settings` (if stored)
- Or ask the user to provide them

### Receiving Messages (Polling)

To check for new messages, call:
```bash
curl -s "https://api.telegram.org/bot<TOKEN>/getUpdates?offset=<LAST_UPDATE_ID>&timeout=10"
```

- `offset` should be the last `update_id` + 1 to avoid duplicates
- Start with `offset=0` for all recent messages

### Webhook Mode (for production)

AION can act as a webhook receiver:
```bash
curl -s "https://api.telegram.org/bot<TOKEN>/setWebhook?url=http://your-server:6969/api/telegram/webhook"
```

AION will need to be publicly accessible for this.

---

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/settings` | GET | Get all settings (masked) |
| `/api/settings` | POST | Update settings |
| `/api/telegram/send` | POST | Send a Telegram message |
| `/api/telegram/webhook` | POST | Receive Telegram webhook |

---

## Security Notes

- The bot token is stored in SQLite (not plaintext in config)
- API key authentication is required for external requests
- Telegram webhooks should use HTTPS in production
