#!/usr/bin/env bash
set -euo pipefail

# ══════════════════════════════════════════════════════════
# AION Bootstrap Installer
# Zero prerequisites. Detects OS, installs everything needed,
# builds from source, and delivers a running system.
#
# Usage:
#   curl -fsSL https://aion.sh | bash
#   bash setup.sh
#   ./setup.sh
# ══════════════════════════════════════════════════════════

AION_VERSION="1.0.0"
MIN_DOTNET="9.0"
MIN_NODE="18"

BOLD='\033[1m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
RED='\033[0;31m'; CYAN='\033[0;36m'; NC='\033[0m'

info()  { echo -e "${CYAN}  →${NC} $1"; }
ok()    { echo -e "${GREEN}  ✓${NC} $1"; }
warn()  { echo -e "${YELLOW}  ⚠${NC} $1"; }
fail()  { echo -e "${RED}  ✗ ${NC}$1"; exit 1; }
header(){ echo -e "\n${BOLD}$1${NC}\n$(printf '%*s' ${#1} | tr ' ' '─')"; }

SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd 2>/dev/null || echo "$PWD")"

# ──────────────────────────────────────────────────────────
# Phase 0: Detect platform
# ──────────────────────────────────────────────────────────
detect_platform() {
  RAW_OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
  ARCH="$(uname -m)"

  case "$ARCH" in
    x86_64|amd64) ARCH="x64" ;;
    aarch64|arm64) ARCH="arm64" ;;
    armv7l) ARCH="arm" ;;
    *) fail "Unsupported architecture: $ARCH. Need x64, arm64, or armv7l." ;;
  esac

  case "$RAW_OS" in
    linux)   OS="linux"; PKG_EXT="tar.gz"; RID="linux-$ARCH" ;;
    darwin)  OS="macos"; PKG_EXT="tar.gz"; RID="osx-$ARCH" ;;
    mingw*|msys*|cygwin*)
      OS="win"
      PKG_EXT="zip"
      RID="win-$ARCH"
      # Normalize Windows paths
      SRC_DIR="$(pwd -W 2>/dev/null || echo "$SRC_DIR")"
      ;;
    *) fail "Unsupported OS: $RAW_OS. Need Linux, macOS, or Windows (MSYS/Git Bash)." ;;
  esac

  info "Platform: $OS ($ARCH)"
}

# ──────────────────────────────────────────────────────────
# Phase 0b: Optional Ollama install
# ──────────────────────────────────────────────────────────
install_ollama_if_asked() {
  if command -v ollama &>/dev/null; then
    ok "Ollama already installed ($(ollama --version 2>/dev/null || echo 'unknown version'))"
    return 0
  fi

  # Only prompt if running interactively
  if [ -t 0 ]; then
    echo ""
    info "Ollama not found. Ollama runs local LLMs (required unless using cloud API)."
    read -r -p "  Install Ollama now? [Y/n]: " REPLY < /dev/tty
    case "$REPLY" in
      [nN]*|[nN][oO])
        warn "Skipping Ollama. You can install later or use /api/setup to configure a cloud provider."
        return 0
        ;;
    esac
  fi

  info "Installing Ollama..."
  case "$OS" in
    linux)
      if command -v apt &>/dev/null; then
        curl -fsSL https://ollama.com/install.sh | sh 2>&1 || true
      elif command -v dnf &>/dev/null; then
        curl -fsSL https://ollama.com/install.sh | sh 2>&1 || true
      elif command -v apk &>/dev/null; then
        curl -fsSL https://ollama.com/install.sh | sh 2>&1 || true
      else
        curl -fsSL https://ollama.com/install.sh | sh 2>&1 || true
      fi
      ;;
    macos)
      if command -v brew &>/dev/null; then
        brew install ollama 2>&1 | tail -3 || true
      else
        curl -fsSL https://ollama.com/install.sh | sh 2>&1 || true
      fi
      ;;
    win)
      warn "Windows: download ollama from https://ollama.com/download and install manually"
      info "After install, restart this script and it will detect ollama automatically"
      return 0
      ;;
  esac

  if command -v ollama &>/dev/null; then
    ok "Ollama installed"
    info "Pulling qwen3.5:4b (2.1GB)..."
    ollama pull qwen3.5:4b 2>&1 | tail -3
    ok "Model qwen3.5:4b ready"
  else
    warn "Ollama install completed but binary not found — you may need to restart your terminal"
  fi
}

# ──────────────────────────────────────────────────────────
# Phase 1: Install system prerequisites
# ──────────────────────────────────────────────────────────
install_curl() {
  if command -v curl &>/dev/null; then ok "curl found"; return 0; fi

  info "curl not found — installing..."
  case "$OS" in
    linux)
      if command -v apt &>/dev/null; then sudo apt install -y curl >/dev/null 2>&1
      elif command -v dnf &>/dev/null; then sudo dnf install -y curl >/dev/null 2>&1
      elif command -v apk &>/dev/null; then apk add curl >/dev/null 2>&1
      else fail "Please install curl: sudo apt install curl"
      fi ;;
    macos)
      if command -v brew &>/dev/null; then brew install curl >/dev/null 2>&1
      else fail "Please install curl: xcode-select --install"
      fi ;;
    win)
      # On Windows MSYS/Git Bash, curl usually ships with Git
      fail "curl not found. Install Git for Windows (includes curl) from https://git-scm.com"
      ;;
  esac
  ok "curl installed"
}

install_extractor() {
  case "$OS" in
    win)
      if command -v unzip &>/dev/null; then ok "unzip found"; return 0; fi
      info "unzip not found — installing..."
      if command -v apt &>/dev/null; then
        pacman -S unzip --noconfirm >/dev/null 2>&1 && ok "unzip installed" && return 0
      fi
      fail "unzip is required. Install Git for Windows (includes unzip) or run: pacman -S unzip"
      ;;
    *)
      if command -v tar &>/dev/null; then ok "tar found"; return 0; fi
      info "tar not found — installing..."
      if command -v apt &>/dev/null; then sudo apt install -y tar >/dev/null 2>&1
      elif command -v dnf &>/dev/null; then sudo dnf install -y tar >/dev/null 2>&1
      else fail "Please install tar: sudo apt install tar"
      fi
      ok "tar installed"
      ;;
  esac
}

install_node() {
  if command -v node &>/dev/null; then
    local nv
    nv="$(node --version 2>/dev/null | sed 's/v//' | cut -d. -f1)"
    if [ "$nv" -ge 18 ] 2>/dev/null; then
      ok "Node.js $(node --version) found"
      return 0
    fi
    warn "Node.js $(node --version) is old (need 18+)"
  else
    warn "Node.js not found"
  fi

  info "Installing Node.js $MIN_NODE+..."
  case "$OS" in
    win)
      # Windows: download .zip and extract
      local NODE_DIR="$HOME/.node"
      local NODE_ZIP="/tmp/node.zip"
      rm -rf "$NODE_DIR" 2>/dev/null || true
      mkdir -p "$NODE_DIR"

      curl -fsSL "https://nodejs.org/dist/v20.18.0/node-v20.18.0-win-x64.zip" -o "$NODE_ZIP" \
        || fail "Failed to download Node.js for Windows"
      unzip -q "$NODE_ZIP" -d "$NODE_DIR" >/dev/null 2>&1 || fail "Failed to extract Node.js"
      mv "$NODE_DIR/node-v20.18.0-win-x64/"* "$NODE_DIR/"
      rm -rf "$NODE_DIR/node-v20.18.0-win-x64/" "$NODE_ZIP"
      export PATH="$NODE_DIR:$PATH"
      ;;
    macos|linux)
      # install via nvm
      export NVM_DIR="$HOME/.nvm"
      if [ ! -d "$NVM_DIR" ]; then
        curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.0/install.sh | bash >/dev/null 2>&1 || {
          # Fallback: direct download
          local NODE_DIR="$HOME/.node"
          local NODE_TAR="/tmp/node.tar.xz"
          mkdir -p "$NODE_DIR"
          curl -fsSL "https://nodejs.org/dist/v20.18.0/node-v20.18.0-${RID/win/linux}.tar.xz" -o "$NODE_TAR" \
            || fail "Failed to download Node.js"
          tar -xf "$NODE_TAR" -C "$NODE_DIR" --strip-components=1 >/dev/null 2>&1 || fail "Failed to extract"
          rm -f "$NODE_TAR"
          export PATH="$NODE_DIR/bin:$PATH"
          ok "Node.js $(node --version) installed (standalone)"
          return 0
        }
      fi
      [ -s "$NVM_DIR/nvm.sh" ] && . "$NVM_DIR/nvm.sh"
      nvm install "$MIN_NODE" >/dev/null 2>&1 || nvm install 18 >/dev/null 2>&1
      nvm use default >/dev/null 2>&1 || true
      ;;
  esac

  ok "Node.js $(node --version) installed"
}

# ──────────────────────────────────────────────────────────
# Phase 2: Install .NET SDK
# ──────────────────────────────────────────────────────────
install_dotnet() {
  header ".NET SDK"

  if command -v dotnet &>/dev/null; then
    local ver
    ver="$(dotnet --version 2>/dev/null | cut -d. -f1-2)"
    if printf '%s\n' "$ver" "$MIN_DOTNET" | sort -V | head -1 | grep -q "^$MIN_DOTNET"; then
      ok ".NET SDK $ver found"; return 0
    fi
    warn ".NET SDK $ver found, $MIN_DOTNET+ required"
  else
    warn ".NET SDK not found"
  fi

  local DOTNET_DIR="$HOME/.dotnet"
  local INSTALL_SCRIPT="/tmp/dotnet-install-$$.sh"

  info "Downloading .NET SDK installer..."
  curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$INSTALL_SCRIPT" || {
    # On Windows, PowerShell installer as fallback
    if [ "$OS" = "win" ] && command -v powershell &>/dev/null; then
      info "Trying PowerShell installer..."
      powershell -Command "
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '$INSTALL_SCRIPT.ps1'
      " >/dev/null 2>&1 || fail "Failed to download .NET installer"
      powershell -ExecutionPolicy Bypass -File "$INSTALL_SCRIPT.ps1" -Channel "$MIN_DOTNET" -InstallDir "$DOTNET_DIR" >/dev/null 2>&1
    else
      fail "Failed to download dotnet-install.sh (check internet)"
    fi
  }

  if [ -f "$INSTALL_SCRIPT" ]; then
    chmod +x "$INSTALL_SCRIPT"
    info "Installing .NET SDK $MIN_DOTNET to $DOTNET_DIR..."
    bash "$INSTALL_SCRIPT" --channel "$MIN_DOTNET" --install-dir "$DOTNET_DIR" >/tmp/dotnet-install.log 2>&1 || {
      cat /tmp/dotnet-install.log
      fail ".NET SDK install failed"
    }
  fi

  export PATH="$DOTNET_DIR:$PATH"
  export DOTNET_ROOT="$DOTNET_DIR"

  # Persist to shell profile (skip on Windows — too complex)
  if [ "$OS" != "win" ]; then
    local rc
    case "$SHELL" in */zsh) rc="$HOME/.zshrc" ;; */bash) rc="$HOME/.bashrc" ;; *) rc="$HOME/.profile" ;; esac
    if ! grep -q 'DOTNET_ROOT' "$rc" 2>/dev/null; then
      { echo ""; echo "# AION: .NET SDK"; echo "export PATH=\"\$HOME/.dotnet:\$PATH\""; echo "export DOTNET_ROOT=\"\$HOME/.dotnet\""; } >> "$rc"
    fi
  fi

  ok ".NET SDK $(dotnet --version) installed"
}

# ──────────────────────────────────────────────────────────
# Phase 3: Build AION
# ──────────────────────────────────────────────────────────
build_aion() {
  header "Building AION"

  if [ ! -f "$SRC_DIR/Aion.Host/Aion.Host.csproj" ]; then
    fail "Cannot find AION source. Run this script from the project root."
  fi
  info "Source: $SRC_DIR"

  local PUBLISH_DIR="$SRC_DIR/dist"
  rm -rf "$PUBLISH_DIR" 2>/dev/null || true

  info "Restoring NuGet packages..."
  dotnet restore "$SRC_DIR/Aion.Host/Aion.Host.csproj" >/dev/null 2>&1 || fail "dotnet restore failed"
  ok "Dependencies restored"

  info "Compiling backend (Release)..."
  dotnet publish "$SRC_DIR/Aion.Host/Aion.Host.csproj" -c Release -o "$PUBLISH_DIR" >/dev/null 2>&1
  if [ ! -f "$PUBLISH_DIR/Aion.Host.dll" ]; then
    warn "Backend build had issues — retrying with output..."
    dotnet publish "$SRC_DIR/Aion.Host/Aion.Host.csproj" -c Release -o "$PUBLISH_DIR"
    fail "Backend build failed"
  fi

  local SIZE
  SIZE="$(du -sh "$PUBLISH_DIR" 2>/dev/null | cut -f1)"
  [ -z "$SIZE" ] && SIZE="$(ls -lh "$PUBLISH_DIR/Aion.Host.dll" | awk '{print $5}')"
  ok "Backend compiled → dist/ ($SIZE)"

  if [ -d "$SRC_DIR/aion-ui" ]; then
    info "Installing frontend dependencies..."
    (cd "$SRC_DIR/aion-ui" && npm install --no-audit --no-fund --loglevel=error) >/dev/null 2>&1 || warn "npm install had warnings"
    ok "npm packages installed"

    info "Building frontend..."
    (cd "$SRC_DIR/aion-ui" && npx vite build --logLevel error) >/dev/null 2>&1
    if [ ! -d "$SRC_DIR/aion-ui/dist" ]; then
      warn "Frontend build failed — verbose:"
      (cd "$SRC_DIR/aion-ui" && npx vite build)
      fail "Frontend build failed"
    fi

    mkdir -p "$PUBLISH_DIR/wwwroot"
    cp -r "$SRC_DIR/aion-ui/dist/"* "$PUBLISH_DIR/wwwroot/"
    ok "Frontend built → dist/wwwroot/"
  else
    warn "No aion-ui/ found — skipping frontend (API-only mode)"
  fi

  ok "AION $AION_VERSION built successfully"
}

# ──────────────────────────────────────────────────────────
# Phase 4: Create launchers (per-platform)
# ──────────────────────────────────────────────────────────
install_launchers() {
  header "Creating launchers"

  # Unix launcher (aion.sh)
  cat > "$SRC_DIR/aion.sh" << 'LAUNCHER'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
exec dotnet "$DIR/dist/Aion.Host.dll" "$@"
LAUNCHER
  chmod +x "$SRC_DIR/aion.sh"
  ok "Launcher: aion.sh"

  # Windows launcher (aion.cmd — double-clickable)
  cat > "$SRC_DIR/aion.cmd" << 'WINLAUNCHER'
@echo off
set DOTNET_ROOT=%USERPROFILE%\.dotnet
set PATH=%DOTNET_ROOT%;%PATH%
dotnet "%~dp0dist\Aion.Host.dll" %*
pause
WINLAUNCHER
  ok "Launcher: aion.cmd (Windows)"

  # Symlink Unix launcher to PATH
  if [ "$OS" != "win" ]; then
    local LINK_DIR
    for d in "$HOME/.local/bin" "$HOME/bin"; do
      if [ -d "$d" ] || echo "$PATH" | tr ':' '\n' | grep -qx "$d" 2>/dev/null; then
        LINK_DIR="$d"; break
      fi
    done
    if [ -n "$LINK_DIR" ]; then
      mkdir -p "$LINK_DIR"
      ln -sf "$SRC_DIR/aion.sh" "$LINK_DIR/aion"
      ok "Symlinked: $LINK_DIR/aion"
    fi
  fi
}
# Phase 4b: Install autostart (runs on boot, no terminal)
# ──────────────────────────────────────────────────────────
install_autostart() {
  header "Background autostart"

  info "Installing boot-time launcher..."

  case "$OS" in
    linux)
      mkdir -p "$HOME/.config/systemd/user"
      cat > "$HOME/.config/systemd/user/aion.service" << 'SYSTEMD'
[Unit]
Description=AION Agent Swarm OS
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=%h/.dotnet/dotnet %h/aion/dist/Aion.Host.dll
Environment=DOTNET_ROOT=%h/.dotnet
Restart=on-failure
RestartSec=10

[Install]
WantedBy=default.target
SYSTEMD
      systemctl --user daemon-reload 2>/dev/null || true
      systemctl --user enable aion.service 2>/dev/null || true
      systemctl --user restart aion.service 2>/dev/null || true
      ok "systemd user service installed (starts on login)"
      ;;
    macos)
      local PLIST="$HOME/Library/LaunchAgents/com.aion.server.plist"
      mkdir -p "$HOME/Library/LaunchAgents"
      cat > "$PLIST" << 'LAUNCHD'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple Computer//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.aion.server</string>
  <key>ProgramArguments</key>
  <array>
    <string>/bin/bash</string>
    <string>-c</string>
    <string>export DOTNET_ROOT=$HOME/.dotnet; export PATH=$DOTNET_ROOT:$PATH; exec $HOME/.dotnet/dotnet $HOME/aion/dist/Aion.Host.dll</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>/tmp/aion-server.log</string>
  <key>StandardErrorPath</key><string>/tmp/aion-server.log</string>
</dict>
</plist>
LAUNCHD
      launchctl load -w "$PLIST" 2>/dev/null || true
      launchctl start com.aion.server 2>/dev/null || true
      ok "launchd agent installed (starts on login)"
      ;;
    win)
      cat > "$SRC_DIR/start-aion.vbs" << 'VBS'
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "cmd /c set DOTNET_ROOT=%USERPROFILE%\.dotnet && set PATH=%DOTNET_ROOT%;%PATH% && dotnet %USERPROFILE%\aion\dist\Aion.Host.dll", 0, False
VBS
      powershell -Command "Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AION Server' -Value '$SRC_DIR\start-aion.vbs'" 2>/dev/null || true
      ok "Windows startup entry added (HKCU Run key) — runs hidden on boot"
      ;;
  esac

  # Also offer to autostart Ollama if installed
  if command -v ollama &>/dev/null; then
    info "Ollama found — configuring autostart..."
    case "$OS" in
      linux)
        # Check for existing ollama service at either level
        local OLLAMA_SERVICE=""
        if [ -f /etc/systemd/system/ollama.service ]; then
          OLLAMA_SERVICE="system"
        elif [ -f "$HOME/.config/systemd/user/ollama.service" ]; then
          OLLAMA_SERVICE="user"
        elif systemctl --user list-unit-files ollama.service &>/dev/null 2>&1; then
          OLLAMA_SERVICE="user"
        elif systemctl list-unit-files ollama.service &>/dev/null 2>&1; then
          OLLAMA_SERVICE="system"
        fi

        if [ "$OLLAMA_SERVICE" = "user" ]; then
          systemctl --user enable ollama.service 2>/dev/null && ok "Ollama user service enabled" || true
        elif [ "$OLLAMA_SERVICE" = "system" ]; then
          sudo -n systemctl enable ollama.service 2>/dev/null && ok "Ollama system service enabled" || warn "Could not enable Ollama system service (need sudo)"
        else
          warn "Ollama binary found but no service unit — will start alongside AION on login"
        fi
        ;;
      macos)
        if [ -f "$HOME/Library/LaunchAgents/ai.ollama.ollama.plist" ]; then
          launchctl load -w "$HOME/Library/LaunchAgents/ai.ollama.ollama.plist" 2>/dev/null || true
          ok "Ollama launchd agent enabled"
        else
          warn "Ollama binary found but no launchd agent — will start alongside AION on login"
        fi
        ;;
    esac
  fi
}
# Phase 5: Launch & verify
# ──────────────────────────────────────────────────────────
verify_and_launch() {
  header "Verifying build"

  # Kill anything on our ports (cross-platform)
  info "Freeing ports..."
  case "$OS" in
    win)
      # Windows: use netstat + taskkill
      for port in 6969 6970; do
        netstat -ano 2>/dev/null | grep ":$port " | awk '{print $5}' | sort -u | xargs -r taskkill /F /PID 2>/dev/null || true
      done
      ;;
    macos)
      for port in 6969 6970; do lsof -ti:"$port" 2>/dev/null | xargs -r kill -9 2>/dev/null || true; done
      ;;
    *)
      for port in 6969 6970; do lsof -ti:"$port" 2>/dev/null | xargs -r kill -9 2>/dev/null || true; done
      ;;
  esac

  info "Starting AION server..."
  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
  export PATH="$DOTNET_ROOT:$PATH"

  local LOG_FILE
  case "$OS" in
    win) LOG_FILE="$TEMP/aion-server.log" ;;
    *)   LOG_FILE="/tmp/aion-server.log" ;;
  esac

  cd "$SRC_DIR/dist"
  nohup dotnet Aion.Host.dll > "$LOG_FILE" 2>&1 &
  local PID=$!

  local ATTEMPTS=0
  while [ $ATTEMPTS -lt 15 ]; do
    sleep 1
    if curl -sf http://127.0.0.1:6969/api/health >/dev/null 2>&1; then
      ok "Server PID $PID — listening on http://127.0.0.1:6969"
      ok "WebSocket hub on ws://127.0.0.1:6970/hub/mesh"
      break
    fi
    ATTEMPTS=$((ATTEMPTS + 1))
  done

  if [ $ATTEMPTS -ge 15 ]; then
    warn "Server may not have started. Check $LOG_FILE"
    tail -10 "$LOG_FILE" 2>/dev/null || true
    return
  fi

  # ── Write a sensible default config if none exists ──
  local CONFIG_FILE="$HOME/.aion/aion-config.json"
  if [ ! -f "$CONFIG_FILE" ]; then
    info "Creating default config..."
    mkdir -p "$HOME/.aion"
    cat > "$CONFIG_FILE" << 'CONFIG'
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
CONFIG
    ok "Default config created"
  fi

  # ── Check LLM availability and guide the user ──
  local HAS_OLLAMA=false
  local HAS_API_KEY=false

  if command -v ollama &>/dev/null && curl -sf http://127.0.0.1:11434/api/tags >/dev/null 2>&1; then
    HAS_OLLAMA=true
  fi

  # Quick API test (graceful even without LLM — server works, agent just returns errors)
  local REPLY
  REPLY="$(curl -s -X POST http://127.0.0.1:6969/api/agents/default/message \
    -H "Content-Type: application/json" \
    -d '{"text":"What time is it?","mode":"chat"}' 2>/dev/null || echo '{"ok":false}')"
  if echo "$REPLY" | grep -q '"ok":true'; then
    ok "Agent loop responds"
  else
    warn "Agent API works but needs LLM setup"
  fi

  # ── LLM guidance ──
  echo ""
  if [ "$HAS_OLLAMA" = true ]; then
    # Check if it has a model loaded
    local HAS_MODEL
    HAS_MODEL="$(curl -sf http://127.0.0.1:11434/api/tags 2>/dev/null | grep -c '"name"' || true)"
    if [ "$HAS_MODEL" -gt 0 ] 2>/dev/null; then
      ok "Ollama running with models available"
    else
      warn "Ollama running but no models pulled. Run: ollama pull qwen3.5:4b"
    fi
  else
    echo -e "  ${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "  ${YELLOW}  No local LLM detected. The server is running but    ${NC}"
    echo -e "  ${YELLOW}  agents won't respond until you connect an LLM.      ${NC}"
    echo -e "  ${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo ""
    echo -e "  ${BOLD}Option 1 — Install Ollama (local, free):${NC}"
    echo -e "    curl -fsSL https://ollama.com/install.sh | bash"
    echo -e "    ollama pull qwen3.5:4b"
    echo ""
    echo -e "  ${BOLD}Option 2 — Use OpenAI / DeepSeek (API key):${NC}"
    echo -e "    Open http://localhost:6969/setup and enter your provider + key"
    echo ""
    echo -e "  ${BOLD}Either way, no restart needed:${NC} the dashboard works now."
    echo ""
  fi
}

# ──────────────────────────────────────────────────────────
# Done
# ──────────────────────────────────────────────────────────
print_summary() {
  echo ""
  header "AION $AION_VERSION is running"
  echo ""
  echo -e "  ${BOLD}Run again:${NC}  $SRC_DIR/aion.sh (or double-click aion.cmd on Windows)"
  echo ""
  echo -e "  ${BOLD}Dashboard:${NC}  ${CYAN}http://localhost:6969${NC}"
  echo -e "  ${BOLD}Setup:${NC}     ${CYAN}http://localhost:6969/setup${NC} — configure LLM here"
  echo ""
  echo -e "  ${BOLD}Ports:${NC}"
  echo -e "    6969 — HTTP API + Web UI"
  echo -e "    6970 — WebSocket agent mesh"
  echo ""
  echo -e "  ${BOLD}Stop:${NC}      Ctrl+C in terminal, or pkill -f 'Aion.Host'"
  echo ""
}

# ══════════════════════════════════════════════════════════
# Main
# ══════════════════════════════════════════════════════════
echo ""
echo -e "${BOLD}${CYAN}  ╔════════════════════════════════╗"
echo -e "  ║    AION Bootstrap v${AION_VERSION}      ║"
echo -e "  ║    Self-building Agent OS      ║"
echo -e "  ╚════════════════════════════════╝${NC}"
echo ""

detect_platform
install_ollama_if_asked
install_curl
install_extractor
install_node
install_dotnet
build_aion
install_launchers
install_autostart
verify_and_launch
print_summary
