#!/usr/bin/env bash
# ══════════════════════════════════════════════════════════
# AION Bootstrap Installer v1.0.0
# Zero prerequisites. Detects OS, installs everything needed,
# builds from source, and delivers a running system.
#
# Usage:
#   git clone https://github.com/DS-Forgeworks/aion.git
#   cd aion && bash setup.sh
# ══════════════════════════════════════════════════════════
set -euo pipefail

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

# Normalise SRC_DIR — handles both `bash setup.sh` from project root AND `curl ... | bash`
# Must use ${X:-} defaults because set -u makes unset vars fatal
SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$PWD}")" && pwd 2>/dev/null || echo "$PWD")"

# ── Checks if we're running interactively (has a real terminal) ──
INTERACTIVE=false
[ -t 0 ] && INTERACTIVE=true

# ── Helper: safe sudo (non-interactive, gracefully handles missing sudo) ──
safe_sudo() {
  if command -v sudo &>/dev/null; then
    sudo -n "$@" 2>/dev/null || warn "sudo $* failed (non-interactive) — continuing anyway"
  else
    warn "sudo not available — skipping: $*"
  fi
}

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
      OS="win"; PKG_EXT="zip"; RID="win-$ARCH"
      SRC_DIR="$(pwd -W 2>/dev/null || echo "$SRC_DIR")"
      ;;
    *) fail "Unsupported OS: $RAW_OS. Need Linux, macOS, or Windows (MSYS/Git Bash)." ;;
  esac

  info "Platform: $OS ($ARCH)"
}

# ──────────────────────────────────────────────────────────
# Phase 0b: Setup directory check
# ──────────────────────────────────────────────────────────
check_source() {
  if [ -f "$SRC_DIR/Aion.Host/Aion.Host.csproj" ]; then
    ok "Source found: $SRC_DIR"
    return 0
  fi

  # If running from pipe, clone the repo
  if [ -z "$SRC_DIR" ] || [ ! -d "$SRC_DIR/Aion.Host" ]; then
    local TARGET="${PWD}/aion"
    warn "Source not found. Attempting to clone to $TARGET..."
    command -v git &>/dev/null || install_git || fail "git not found. Install git first."
    if git clone https://github.com/DS-Forgeworks/aion.git "$TARGET" 2>&1 | tail -3; then
      SRC_DIR="$TARGET"
      ok "Source cloned to $SRC_DIR"
    else
      warn "Clone failed — the repo may be private."
      warn "Make sure you have authenticated with GitHub first:"
      warn "  git clone https://github.com/DS-Forgeworks/aion.git"
      warn "  cd aion && bash setup.sh"
      fail "Use the two-step clone method above."
    fi
  fi
}

# ──────────────────────────────────────────────────────────
# Phase 1: Install system prerequisites
# ──────────────────────────────────────────────────────────
install_curl() {
  command -v curl &>/dev/null && { ok "curl found"; return 0; }
  info "Installing curl..."
  case "$OS" in
    linux)
      command -v apt &>/dev/null && safe_sudo apt install -y curl
      command -v dnf &>/dev/null && safe_sudo dnf install -y curl
      command -v apk &>/dev/null && apk add curl
      ;;
    macos) command -v brew &>/dev/null && brew install curl ;;
    win) fail "curl not found. Install Git for Windows (includes curl) from https://git-scm.com" ;;
  esac
  command -v curl &>/dev/null || fail "curl still missing after install attempt."
  ok "curl installed"
}

install_extractor() {
  if [ "$OS" = "win" ]; then
    command -v unzip &>/dev/null && { ok "unzip found"; return 0; }
    info "Installing unzip..."
    command -v pacman &>/dev/null && pacman -S unzip --noconfirm
    command -v unzip &>/dev/null || fail "unzip required. Run: pacman -S unzip"
    ok "unzip installed"
  else
    command -v tar &>/dev/null && { ok "tar found"; return 0; }
    info "Installing tar..."
    command -v apt &>/dev/null && safe_sudo apt install -y tar
    command -v dnf &>/dev/null && safe_sudo dnf install -y tar
    command -v tar &>/dev/null || fail "tar still missing. Install: sudo apt install tar"
    ok "tar installed"
  fi
}

install_node() {
  if command -v node &>/dev/null; then
    local nv; nv="$(node --version 2>/dev/null | sed 's/v//' | cut -d. -f1)"
    if [ "$nv" -ge 18 ] 2>/dev/null; then
      ok "Node.js $(node --version) found"
      return 0
    fi
    warn "Node.js $(node --version) is old (need 18+)"
  fi

  info "Installing Node.js 20..."

  local NODE_DIR="$HOME/.node"
  rm -rf "$NODE_DIR" 2>/dev/null || true
  mkdir -p "$NODE_DIR"

  case "$OS" in
    win)
      local NODE_ZIP="/tmp/node.zip"
      curl -fsSL "https://nodejs.org/dist/v20.18.0/node-v20.18.0-win-x64.zip" -o "$NODE_ZIP" \
        || fail "Failed to download Node.js"
      unzip -q "$NODE_ZIP" -d "$NODE_DIR" >/dev/null 2>&1 || fail "Failed to extract Node.js"
      mv "$NODE_DIR/node-v20.18.0-win-x64/"* "$NODE_DIR/"
      rm -rf "$NODE_DIR/node-v20.18.0-win-x64/" "$NODE_ZIP"
      export PATH="$NODE_DIR:$PATH"
      ;;
    *)
      local NODE_TAR="/tmp/node.tar.xz"
      curl -fsSL "https://nodejs.org/dist/v20.18.0/node-v20.18.0-${RID/win/linux}.tar.xz" -o "$NODE_TAR" \
        || fail "Failed to download Node.js"
      tar -xf "$NODE_TAR" -C "$NODE_DIR" --strip-components=1 >/dev/null 2>&1 || fail "Failed to extract"
      rm -f "$NODE_TAR"
      export PATH="$NODE_DIR/bin:$PATH"
      ;;
  esac

  ok "Node.js $(node --version) installed (standalone at $NODE_DIR)"
}

# ──────────────────────────────────────────────────────────
# Phase 2: Install .NET SDK
# ──────────────────────────────────────────────────────────
install_dotnet() {
  header ".NET SDK"

  if command -v dotnet &>/dev/null; then
    local ver; ver="$(dotnet --version 2>/dev/null | cut -d. -f1-2)"
    if printf '%s\n' "$ver" "$MIN_DOTNET" | sort -V | head -1 | grep -q "^$MIN_DOTNET"; then
      ok ".NET SDK $ver found"; return 0
    fi
    warn ".NET SDK $ver found, $MIN_DOTNET+ required"
  fi

  local DOTNET_DIR="$HOME/.dotnet"
  local INSTALL_SCRIPT="/tmp/dotnet-install-$$.sh"

  info "Downloading .NET SDK installer..."
  curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$INSTALL_SCRIPT" || {
    if [ "$OS" = "win" ] && command -v powershell &>/dev/null; then
      powershell -Command "
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '$INSTALL_SCRIPT.ps1'
      " >/dev/null 2>&1 || fail "Failed to download .NET installer (check internet)"
      powershell -ExecutionPolicy Bypass -File "$INSTALL_SCRIPT.ps1" -Channel "$MIN_DOTNET" -InstallDir "$DOTNET_DIR" >/dev/null 2>&1
      export PATH="$DOTNET_DIR:$PATH"
      export DOTNET_ROOT="$DOTNET_DIR"
      ok ".NET SDK $(dotnet --version) installed"
      return 0
    fi
    fail "Failed to download dotnet-install.sh (check internet)"
  }

  chmod +x "$INSTALL_SCRIPT"
  info "Installing .NET SDK $MIN_DOTNET..."
  bash "$INSTALL_SCRIPT" --channel "$MIN_DOTNET" --install-dir "$DOTNET_DIR" >/tmp/dotnet-install.log 2>&1 || {
    cat /tmp/dotnet-install.log
    fail ".NET SDK install failed"
  }

  export PATH="$DOTNET_DIR:$PATH"
  export DOTNET_ROOT="$DOTNET_DIR"

  # Persist to shell profile
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

  local PUBLISH_DIR="$SRC_DIR/dist"
  rm -rf "$PUBLISH_DIR" 2>/dev/null || true

  info "Restoring NuGet packages..."
  dotnet restore "$SRC_DIR/Aion.Host/Aion.Host.csproj" >/dev/null 2>&1 || fail "dotnet restore failed"
  ok "Dependencies restored"

  info "Compiling backend (Release)..."
  dotnet publish "$SRC_DIR/Aion.Host/Aion.Host.csproj" -c Release -o "$PUBLISH_DIR" >/tmp/dotnet-publish.log 2>&1
  if [ ! -f "$PUBLISH_DIR/Aion.Host.dll" ]; then
    cat /tmp/dotnet-publish.log
    fail "Backend build failed — see /tmp/dotnet-publish.log"
  fi

  local SIZE; SIZE="$(du -sh "$PUBLISH_DIR" 2>/dev/null | cut -f1)"
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
    # Copy standalone login page + favicon
    cp "$SRC_DIR/aion-ui/public/login.html" "$PUBLISH_DIR/wwwroot/" 2>/dev/null || true
    cp "$SRC_DIR/aion-ui/public/favicon.svg" "$PUBLISH_DIR/wwwroot/" 2>/dev/null || true
    ok "Frontend built → dist/wwwroot/"
  else
    warn "No aion-ui/ found — skipping frontend (API-only mode)"
  fi

  ok "AION $AION_VERSION built successfully"
}

# ──────────────────────────────────────────────────────────
# Phase 4: Create launchers
# ──────────────────────────────────────────────────────────
install_launchers() {
  header "Creating launchers"

  # Unix launcher
  cat > "$SRC_DIR/aion.sh" << 'LAUNCHER'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$PWD}")" && pwd 2>/dev/null || echo "$PWD")"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
exec dotnet "$DIR/dist/Aion.Host.dll" "$@"
LAUNCHER
  chmod +x "$SRC_DIR/aion.sh"
  ok "Launcher: aion.sh"

  # Windows launcher (double-clickable)
  cat > "$SRC_DIR/aion.cmd" << 'WINLAUNCHER'
@echo off
set DOTNET_ROOT=%USERPROFILE%\.dotnet
set PATH=%DOTNET_ROOT%;%PATH%
dotnet "%~dp0dist\Aion.Host.dll" %*
pause
WINLAUNCHER
  ok "Launcher: aion.cmd (Windows)"

  # Symlink to PATH
  if [ "$OS" != "win" ]; then
    local LINK_DIR=""
    for d in "$HOME/.local/bin" "$HOME/bin" "/usr/local/bin"; do
      if [ -d "$d" ] || echo "$PATH" | tr ':' '\n' | grep -qx "$d" 2>/dev/null; then
        LINK_DIR="$d"; break
      fi
    done
    # Fallback: create one
    if [ -z "$LINK_DIR" ]; then
      LINK_DIR="$HOME/.local/bin"
      mkdir -p "$LINK_DIR"
    fi
    ln -sf "$SRC_DIR/aion.sh" "$LINK_DIR/aion"
    # Ensure it's on PATH
    case "$SHELL" in */zsh) rc="$HOME/.zshrc" ;; */bash) rc="$HOME/.bashrc" ;; *) rc="$HOME/.profile" ;; esac
    if ! grep -q "$LINK_DIR" "$rc" 2>/dev/null; then
      echo "export PATH=\"\$PATH:$LINK_DIR\"" >> "$rc"
    fi
    ok "Command symlinked: $LINK_DIR/aion"
  fi
}

# ──────────────────────────────────────────────────────────
# Phase 5: Start server
# ──────────────────────────────────────────────────────────
start_server() {
  header "Starting AION"

  # Kill existing on our ports
  info "Freeing ports 6969, 6970..."
  case "$OS" in
    win)
      for port in 6969 6970; do
        netstat -ano 2>/dev/null | grep ":$port " | awk '{print $5}' | sort -u | xargs -r taskkill /F /PID 2>/dev/null || true
      done
      ;;
    *)
      for port in 6969 6970; do lsof -ti:"$port" 2>/dev/null | xargs -r kill -9 2>/dev/null || true; done
      ;;
  esac

  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
  export PATH="$DOTNET_ROOT:$PATH"

  local LOG_FILE="/tmp/aion-server.log"
  cd "$SRC_DIR/dist"
  nohup dotnet Aion.Host.dll > "$LOG_FILE" 2>&1 &
  local PID=$!

  info "Waiting for server (up to 15s)..."
  local ATTEMPTS=0
  while [ $ATTEMPTS -lt 15 ]; do
    sleep 1
    if curl -sf http://127.0.0.1:6969/api/health >/dev/null 2>&1; then
      ok "Server PID $PID — http://localhost:6969"
      ok "WebSocket mesh: ws://127.0.0.1:6970/hub/mesh"
      break
    fi
    ATTEMPTS=$((ATTEMPTS + 1))
  done

  if [ $ATTEMPTS -ge 15 ]; then
    warn "Server didn't respond within 15s. Check $LOG_FILE:"
    tail -5 "$LOG_FILE" 2>/dev/null || true
    return
  fi
}

# ──────────────────────────────────────────────────────────
# Phase 6: Create default config
# ──────────────────────────────────────────────────────────
create_config() {
  local CONFIG_FILE="$HOME/.aion/aion-config.json"
  mkdir -p "$HOME/.aion"

  cat > "$CONFIG_FILE" << 'CONFIG'
{
  "Version": 1,
  "Workspace": "~/.aion/workspace",
  "Language": "en",
  "Llm": {
    "Provider": "ollama",
    "Model": null,
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
  ok "Config created at $CONFIG_FILE (no default model — select from available models in UI)"
}

# ──────────────────────────────────────────────────────────
# Phase 7: Ollama setup guidance
# ──────────────────────────────────────────────────────────
setup_ollama() {
  if command -v ollama &>/dev/null; then
    ok "Ollama found"
    if curl -sf http://127.0.0.1:11434/api/tags >/dev/null 2>&1; then
      local COUNT; COUNT="$(curl -sf http://127.0.0.1:11434/api/tags 2>/dev/null | grep -c '"name"' || true)"
      if [ "$COUNT" -gt 0 ] 2>/dev/null; then
        ok "Ollama running with $COUNT model(s)"
      else
        warn "Ollama running but no models. Run: ollama pull qwen3.5:4b"
      fi
    else
      if [ "$INTERACTIVE" = true ]; then
        echo ""
        info "Ollama is installed but not running."
        read -r -p "  Start Ollama now? [Y/n]: " REPLY
        case "$REPLY" in [nN]*|[nN][oO]) ;; *)
          ollama serve > /dev/null 2>&1 &
          sleep 3
          info "Pull qwen3.5:4b (2.1GB)? This is the default model [Y/n]: "
          read -r -p "  " REPLY2
          case "$REPLY2" in [nN]*|[nN][oO]) ;; *)
            ollama pull qwen3.5:4b
            ok "Model qwen3.5:4b ready"
            ;;
          esac
          ;;
        esac
      else
        warn "Ollama installed but not running. Start it: ollama serve &"
        warn "Then pull a model: ollama pull qwen3.5:4b"
      fi
    fi
  else
    if [ "$INTERACTIVE" = true ]; then
      echo ""
      echo -e "  ${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
      echo -e "  ${YELLOW}  Ollama not found — needed for local AI           ${NC}"
      echo -e "  ${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
      echo ""
      read -r -p "  Install Ollama now? [Y/n]: " REPLY
      case "$REPLY" in [nN]*|[nN][oO])
        warn "Skipping Ollama. The dashboard works but agents need an LLM."
        warn "Configure one at http://localhost:6969/setup"
        return 0
        ;;
      esac
    fi

    info "Installing Ollama..."
    curl -fsSL https://ollama.com/install.sh | sh 2>&1 || warn "Ollama install script had issues"
    if command -v ollama &>/dev/null; then
      ok "Ollama installed"
      ollama serve > /dev/null 2>&1 &
      sleep 3
      if [ "$INTERACTIVE" = true ]; then
        read -r -p "  Pull default model (qwen3.5:4b, 2.1GB)? [Y/n]: " REPLY2
        case "$REPLY2" in [nN]*|[nN][oO]) ;; *)
          ollama pull qwen3.5:4b
          ok "Model ready"
          ;;
        esac
      fi
    else
      warn "Ollama install completed but binary not found (may need restart)"
    fi
  fi
}

# ──────────────────────────────────────────────────────────
# Phase 8: Verify the agent loop responds
# ──────────────────────────────────────────────────────────
verify_agent() {
  header "Verifying"

  local REPLY
  REPLY="$(curl -s -X POST http://127.0.0.1:6969/api/agents/default/message \
    -H "Content-Type: application/json" \
    -d '{"text":"Hello, are you alive?","mode":"chat"}' 2>/dev/null || echo '{"ok":false}')"
  if echo "$REPLY" | grep -q '"ok":true'; then
    ok "Agent loop responds"
    echo ""
    echo -e "  ${GREEN}${BOLD}AION is fully operational${NC}"
  else
    warn "Agent API works but LLM is not responding"
    echo ""
    echo -e "  ${YELLOW}  The server is running, just needs an LLM.${NC}"
    echo -e "  ${YELLOW}  Open http://localhost:6969/setup to configure one.${NC}"
  fi
}

# ──────────────────────────────────────────────────────────
# Final: Summary
# ──────────────────────────────────────────────────────────
print_summary() {
  echo ""
  header "AION $AION_VERSION is ready"
  echo ""
  echo -e "  ${BOLD}Dashboard:${NC}  ${CYAN}http://localhost:6969${NC}"
  echo -e "  ${BOLD}Setup:${NC}      ${CYAN}http://localhost:6969/setup${NC}"
  echo -e "  ${BOLD}Run again:${NC}  ./aion.sh (or double-click aion.cmd on Windows)"
  echo ""
  echo -e "  ${BOLD}Ports:${NC}"
  echo -e "    6969 — HTTP API + Web UI"
  echo -e "    6970 — WebSocket agent mesh"
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
check_source
install_curl
install_extractor
install_node
install_dotnet
build_aion
install_launchers
create_config
start_server
setup_ollama
verify_agent
print_summary
