#!/usr/bin/env python3
"""
Email MCP Server for AION.

Exposes IMAP email tools via the Model Context Protocol over stdio.
Manages: list accounts, read inbox, send email, reply, bulk operations.

Runs as a subprocess managed by AION's McpManager.
"""

import sys
import json
import imaplib
import smtplib
import email
import email.utils
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart
from email.header import decode_header
import os
import base64
import sqlite3
import time
from datetime import datetime, timedelta

# ─── MCP Server Protocol ──────────────────────────────────────────

_next_id = 0

def send_response(request_id, result=None, error=None):
    """Send a JSON-RPC 2.0 response to stdout."""
    msg = {"jsonrpc": "2.0", "id": request_id}
    if error:
        msg["error"] = {"code": error[0], "message": error[1]}
    else:
        msg["result"] = result
    line = json.dumps(msg, ensure_ascii=False)
    sys.stdout.write(line + "\n")
    sys.stdout.flush()

def send_notification(method, params):
    """Send a notification (no id — progress updates, etc.)."""
    msg = {"jsonrpc": "2.0", "method": method, "params": params}
    line = json.dumps(msg, ensure_ascii=False)
    sys.stdout.write(line + "\n")
    sys.stdout.flush()

def read_request():
    """Read a JSON-RPC request from stdin."""
    line = sys.stdin.readline()
    if not line:
        return None
    return json.loads(line)

# ─── Account Database ──────────────────────────────────────────

DB_PATH = os.path.expanduser("~/.aion/email_accounts.db")

def init_db():
    os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.execute("""
        CREATE TABLE IF NOT EXISTS accounts (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            email TEXT NOT NULL,
            imap_server TEXT NOT NULL,
            imap_port INTEGER DEFAULT 993,
            smtp_server TEXT NOT NULL,
            smtp_port INTEGER DEFAULT 587,
            username TEXT,
            password TEXT,
            use_ssl INTEGER DEFAULT 1,
            created_at TEXT DEFAULT (datetime('now'))
        )
    """)
    conn.commit()
    conn.close()

def get_account(account_id):
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    row = conn.execute("SELECT * FROM accounts WHERE id = ?", (account_id,)).fetchone()
    conn.close()
    if not row:
        return None
    return dict(row)

def list_accounts():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    rows = conn.execute("SELECT id, name, email FROM accounts").fetchall()
    conn.close()
    return [dict(r) for r in rows]

def save_account(account_id, name, email_addr, imap_server, imap_port, 
                 smtp_server, smtp_port, username, password):
    conn = sqlite3.connect(DB_PATH)
    conn.execute("""
        INSERT OR REPLACE INTO accounts 
        (id, name, email, imap_server, imap_port, smtp_server, smtp_port, username, password)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (account_id, name, email_addr, imap_server, imap_port, 
          smtp_server, smtp_port, username, password))
    conn.commit()
    conn.close()

def delete_account(account_id):
    conn = sqlite3.connect(DB_PATH)
    conn.execute("DELETE FROM accounts WHERE id = ?", (account_id,))
    conn.commit()
    conn.close()

# ─── IMAP Helpers ────────────────────────────────────────────────

def decode_mime_header(header_value):
    """Decode a MIME encoded header (e.g., =?UTF-8?B?...?=) to plain text."""
    if not header_value:
        return ""
    parts = decode_header(header_value)
    result = []
    for part, encoding in parts:
        if isinstance(part, bytes):
            try:
                result.append(part.decode(encoding or "utf-8", errors="replace"))
            except (LookupError, UnicodeDecodeError):
                result.append(part.decode("utf-8", errors="replace"))
        else:
            result.append(str(part))
    return " ".join(result)

def connect_imap(account):
    """Connect to IMAP server and return connection."""
    conn = imaplib.IMAP4_SSL(account["imap_server"], account["imap_port"])
    conn.login(account["username"], account["password"])
    return conn

def fetch_emails(account, folder="INBOX", since_days=7, max_results=20, unread_only=False):
    """Fetch emails from an IMAP account."""
    conn = connect_imap(account)
    conn.select(folder)
    
    since_date = (datetime.now() - timedelta(days=since_days)).strftime("%d-%b-%Y")
    
    search_criteria = f'SINCE {since_date}'
    if unread_only:
        search_criteria = f'(UNSEEN SINCE {since_date})'
    
    status, data = conn.search(None, search_criteria)
    if status != "OK":
        conn.logout()
        return {"error": "Search failed", "emails": []}
    
    uids = data[0].split() if data[0] else []
    uids = uids[-max_results:]  # Take most recent N
    
    emails_list = []
    for uid in uids:
        status, msg_data = conn.fetch(uid, "(RFC822)")
        if status != "OK":
            continue
        
        raw_email = msg_data[0][1]
        msg = email.message_from_bytes(raw_email)
        
        subject = decode_mime_header(msg.get("Subject", ""))
        sender = decode_mime_header(msg.get("From", ""))
        date = msg.get("Date", "")
        message_id = msg.get("Message-ID", str(uid))
        
        # Extract body
        body = ""
        if msg.is_multipart():
            for part in msg.walk():
                if part.get_content_type() == "text/plain":
                    payload = part.get_payload(decode=True)
                    if payload:
                        body = payload.decode("utf-8", errors="replace")
                    break
        else:
            payload = msg.get_payload(decode=True)
            if payload:
                body = payload.decode("utf-8", errors="replace")
        
        # Truncate body for listing
        body_preview = body[:500] if body else ""
        
        emails_list.append({
            "uid": uid.decode() if isinstance(uid, bytes) else str(uid),
            "subject": subject,
            "from": sender,
            "date": date,
            "body_preview": body_preview,
            "is_read": False,  # Would need SEEN flag check
            "message_id": message_id,
        })
    
    conn.logout()
    return {"emails": emails_list, "total": len(emails_list)}

def read_email_body(account, uid, folder="INBOX"):
    """Fetch full body of a specific email."""
    conn = connect_imap(account)
    conn.select(folder)
    
    status, msg_data = conn.fetch(uid, "(RFC822)")
    if status != "OK":
        conn.logout()
        return {"error": "Email not found"}
    
    raw_email = msg_data[0][1]
    msg = email.message_from_bytes(raw_email)
    
    subject = decode_mime_header(msg.get("Subject", ""))
    sender = decode_mime_header(msg.get("From", ""))
    date = msg.get("Date", "")
    
    # Extract body
    body = ""
    html_body = ""
    if msg.is_multipart():
        for part in msg.walk():
            ctype = part.get_content_type()
            if ctype == "text/plain" and not body:
                payload = part.get_payload(decode=True)
                if payload:
                    body = payload.decode("utf-8", errors="replace")
            elif ctype == "text/html" and not html_body:
                payload = part.get_payload(decode=True)
                if payload:
                    html_body = payload.decode("utf-8", errors="replace")
    else:
        payload = msg.get_payload(decode=True)
        if payload:
            body_text = payload.decode("utf-8", errors="replace")
            if msg.get_content_type() == "text/html":
                html_body = body_text
            else:
                body = body_text
    
    conn.logout()
    return {
        "subject": subject,
        "from": sender,
        "date": date,
        "body": body,
        "html_body": html_body,
    }

def send_email(account, to_addr, subject, body, reply_to_uid=None):
    """Send an email via SMTP."""
    msg = MIMEMultipart("alternative")
    msg["From"] = account["email"]
    msg["To"] = to_addr
    msg["Subject"] = subject
    msg["Date"] = email.utils.formatdate(localtime=True)
    
    if reply_to_uid:
        # Fetch original for threading
        conn = connect_imap(account)
        conn.select("INBOX")
        status, data = conn.fetch(reply_to_uid, "(RFC822)")
        if status == "OK":
            orig = email.message_from_bytes(data[0][1])
            msg_id = orig.get("Message-ID", "")
            references = orig.get("References", "")
            if msg_id:
                msg["In-Reply-To"] = msg_id
                msg["References"] = f"{references} {msg_id}".strip()
        conn.logout()
    
    msg.attach(MIMEText(body, "plain"))
    
    if account["use_ssl"]:
        server = smtplib.SMTP_SSL(account["smtp_server"], account["smtp_port"])
    else:
        server = smtplib.SMTP(account["smtp_server"], account["smtp_port"])
        server.starttls()
    
    server.login(account["username"], account["password"])
    server.sendmail(account["email"], [to_addr], msg.as_string())
    server.quit()
    
    return {"status": "sent", "to": to_addr, "subject": subject}

def bulk_email_action(account, uids, action, folder="INBOX"):
    """Perform bulk action (delete, archive, mark_read) on multiple emails."""
    conn = connect_imap(account)
    conn.select(folder)
    
    uid_set = ",".join(uids)
    
    if action == "delete":
        conn.store(uid_set, "+FLAGS", "\\Deleted")
        conn.expunge()
    elif action == "mark_read":
        conn.store(uid_set, "+FLAGS", "\\Seen")
    elif action == "archive":
        conn.copy(uid_set, "[Gmail]/All Mail")
        conn.store(uid_set, "+FLAGS", "\\Deleted")
        conn.expunge()
    else:
        conn.logout()
        return {"error": f"Unknown action: {action}", "count": 0}
    
    conn.logout()
    return {"status": "done", "action": action, "count": len(uids)}

# ─── Tool Handlers ────────────────────────────────────────────────

TOOLS = {
    "list_email_accounts": {
        "description": "List all configured email accounts",
        "inputSchema": {"type": "object", "properties": {}},
        "handler": lambda args: {"accounts": list_accounts()}
    },
    "add_email_account": {
        "description": "Add a new email account (IMAP/SMTP)",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "string", "description": "Unique account ID"},
                "name": {"type": "string", "description": "Display name"},
                "email": {"type": "string", "description": "Email address"},
                "imap_server": {"type": "string"},
                "imap_port": {"type": "integer", "default": 993},
                "smtp_server": {"type": "string"},
                "smtp_port": {"type": "integer", "default": 587},
                "username": {"type": "string"},
                "password": {"type": "string"},
            },
            "required": ["id", "name", "email", "imap_server", "smtp_server", "username", "password"]
        },
        "handler": lambda args: (
            save_account(
                args["id"], args["name"], args["email"],
                args["imap_server"], args.get("imap_port", 993),
                args["smtp_server"], args.get("smtp_port", 587),
                args["username"], args["password"]
            ),
            {"status": "saved", "id": args["id"], "email": args["email"]}
        )[-1]
    },
    "remove_email_account": {
        "description": "Remove an email account by ID",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "string"}
            },
            "required": ["id"]
        },
        "handler": lambda args: (
            delete_account(args["id"]),
            {"status": "deleted", "id": args["id"]}
        )[-1]
    },
    "list_emails": {
        "description": "List emails from an account inbox",
        "inputSchema": {
            "type": "object",
            "properties": {
                "account_id": {"type": "string"},
                "folder": {"type": "string", "default": "INBOX"},
                "since_days": {"type": "integer", "default": 7},
                "max_results": {"type": "integer", "default": 20},
                "unread_only": {"type": "boolean", "default": False},
            },
            "required": ["account_id"]
        },
        "handler": lambda args: fetch_emails(
            get_account(args["account_id"]),
            folder=args.get("folder", "INBOX"),
            since_days=args.get("since_days", 7),
            max_results=args.get("max_results", 20),
            unread_only=args.get("unread_only", False),
        )
    },
    "read_email": {
        "description": "Read full body of a specific email",
        "inputSchema": {
            "type": "object",
            "properties": {
                "account_id": {"type": "string"},
                "uid": {"type": "string"},
                "folder": {"type": "string", "default": "INBOX"},
            },
            "required": ["account_id", "uid"]
        },
        "handler": lambda args: read_email_body(
            get_account(args["account_id"]),
            args["uid"],
            args.get("folder", "INBOX"),
        )
    },
    "send_email": {
        "description": "Send an email from an account",
        "inputSchema": {
            "type": "object",
            "properties": {
                "account_id": {"type": "string"},
                "to": {"type": "string"},
                "subject": {"type": "string"},
                "body": {"type": "string"},
            },
            "required": ["account_id", "to", "subject", "body"]
        },
        "handler": lambda args: send_email(
            get_account(args["account_id"]),
            args["to"], args["subject"], args["body"],
            args.get("reply_to_uid"),
        )
    },
    "bulk_email": {
        "description": "Bulk action on emails (delete, mark_read, archive)",
        "inputSchema": {
            "type": "object",
            "properties": {
                "account_id": {"type": "string"},
                "uids": {"type": "array", "items": {"type": "string"}},
                "action": {"type": "string", "enum": ["delete", "mark_read", "archive"]},
                "folder": {"type": "string", "default": "INBOX"},
            },
            "required": ["account_id", "uids", "action"]
        },
        "handler": lambda args: bulk_email_action(
            get_account(args["account_id"]),
            args["uids"], args["action"],
            args.get("folder", "INBOX"),
        )
    },
}

# ─── Main Loop ────────────────────────────────────────────────────

def main():
    init_db()
    
    # Announce started
    send_notification("server/started", {"status": "ready"})
    
    while True:
        try:
            req = read_request()
            if req is None:
                break
            
            req_id = req.get("id")
            method = req.get("method")
            params = req.get("params", {})
            
            if method == "tools/list":
                send_response(req_id, {
                    "tools": [
                        {"name": k, "description": v["description"], "inputSchema": v["inputSchema"]}
                        for k, v in TOOLS.items()
                    ]
                })
            
            elif method == "tools/call":
                tool_name = params.get("name")
                arguments = params.get("arguments", {})
                
                if tool_name in TOOLS:
                    try:
                        result = TOOLS[tool_name]["handler"](arguments)
                        send_response(req_id, {
                            "content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}]
                        })
                    except Exception as e:
                        send_response(req_id, error=(-1, str(e)))
                else:
                    send_response(req_id, error=(-32601, f"Tool not found: {tool_name}"))
            
            elif method == "initialize":
                send_response(req_id, {
                    "protocolVersion": "0.1.0",
                    "serverInfo": {"name": "aion-email", "version": "1.0.0"}
                })
            
            else:
                send_response(req_id, error=(-32601, f"Method not found: {method}"))
        
        except json.JSONDecodeError:
            # Invalid JSON — ignore
            pass
        except EOFError:
            break
        except BrokenPipeError:
            break
        except Exception as e:
            try:
                send_notification("server/error", {"message": str(e)})
            except:
                pass

if __name__ == "__main__":
    main()
