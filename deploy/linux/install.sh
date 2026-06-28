#!/usr/bin/env bash
#
# TimeGrapher — Linux (linux-x64 / linux-arm64) installer.
#
# Run from inside the extracted release folder:
#     ./install.sh
#
# What it does:
#   1. Installs Linux runtime, live-audio CLI, and credential-store packages
#   2. Marks the TimeGrapher.App launcher executable.
#   3. Installs the taskbar icon and a desktop entry whose Exec/Icon point at THIS
#      folder (no hardcoded paths), so the app shows up in the menu/taskbar.
#   4. Writes a TimeGrapher.desktop launcher next to the binary and on the Desktop
#      (replacing any existing one) — a Linux ELF has no embedded icon, so these
#      give the file manager/desktop a branded double-clickable item.
#
# Re-runnable (idempotent). ICU (libicu) is intentionally NOT a dependency: the app
# is built with InvariantGlobalization, so .NET does not require system ICU.
set -euo pipefail

APP_NAME="TimeGrapher.App"
ICON_SRC="AppIcon-256.png"
INSTALL_DEPS=1

usage() {
  cat <<'USAGE'
Usage: ./install.sh [--no-deps] [--help]

  --no-deps   Skip the apt package install (use if the runtime, live-audio,
              and credential-store dependencies are already present, or on a
              non-Debian/non-Pi system).
  --help      Show this help.
USAGE
}

for arg in "$@"; do
  case "$arg" in
    --no-deps) INSTALL_DEPS=0 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; usage >&2; exit 2 ;;
  esac
done

# Resolve the folder this script (and the app) live in, as an absolute path.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAUNCHER="$SCRIPT_DIR/$APP_NAME"

if [ ! -f "$LAUNCHER" ]; then
  echo "error: $APP_NAME not found next to this script ($SCRIPT_DIR)." >&2
  echo "       Run install.sh from inside the extracted release folder." >&2
  exit 1
fi

# --- 1. Runtime dependencies -------------------------------------------------
if [ "$INSTALL_DEPS" -eq 1 ]; then
  if command -v apt-get >/dev/null 2>&1; then
    SUDO=""
    if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi
    echo "==> Installing runtime libraries (X11, fontconfig, XWayland, live-audio CLI, credential store)..."
    $SUDO apt-get update
    $SUDO apt-get install -y libx11-6 libice6 libsm6 libfontconfig1 xwayland \
      pipewire pipewire-bin wireplumber alsa-utils \
      gnome-keyring libsecret-tools
  else
    echo "==> apt-get not found; skipping dependency install."
    echo "    Install the equivalents of: libx11-6 libice6 libsm6 libfontconfig1 xwayland"
    echo "    live-audio CLI tools: pipewire pipewire-bin wireplumber alsa-utils"
    echo "    and credential-store tools: gnome-keyring libsecret-tools"
    echo "    (provide X11/XWayland, wpctl/pw-record/arecord, and Secret Service/secret-tool)."
  fi
else
  echo "==> Skipping dependency install (--no-deps)."
fi

# --- 2. Executable bit -------------------------------------------------------
echo "==> Marking $APP_NAME executable..."
chmod +x "$LAUNCHER"

# --- 3. Desktop integration (icon + .desktop) --------------------------------
DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}"
ICON_DIR="$DATA_DIR/icons"
APPS_DIR="$DATA_DIR/applications"
mkdir -p "$ICON_DIR" "$APPS_DIR"

ICON_DEST=""
if [ -f "$SCRIPT_DIR/$ICON_SRC" ]; then
  ICON_DEST="$ICON_DIR/timegrapher.png"
  cp "$SCRIPT_DIR/$ICON_SRC" "$ICON_DEST"
  echo "==> Installed icon -> $ICON_DEST"
else
  echo "==> $ICON_SRC not found; desktop entry will have no icon."
fi

# Generate every desktop entry fresh so Exec/Icon point at the real install
# location (no hardcoded paths).
write_desktop_entry() {
  {
    echo "[Desktop Entry]"
    echo "Type=Application"
    echo "Name=TimeGrapher"
    # Quote Exec per the Desktop Entry spec so an install path with spaces still launches.
    printf 'Exec="%s"\n' "$LAUNCHER"
    if [ -n "$ICON_DEST" ]; then echo "Icon=$ICON_DEST"; fi
    echo "StartupWMClass=$APP_NAME"
    echo "Categories=Utility;"
  } > "$1"
}

# Menu/taskbar entry. The filename must equal the app-id (StartupWMClass) so the
# Pi panel matches the running window to this entry's icon.
DESKTOP_DEST="$APPS_DIR/$APP_NAME.desktop"
echo "==> Writing desktop entry -> $DESKTOP_DEST"
write_desktop_entry "$DESKTOP_DEST"
chmod 644 "$DESKTOP_DEST"

# Folder launcher next to the binary: a Linux ELF has no embedded icon (unlike a
# Windows .exe), so the file manager shows the raw binary with a generic icon.
# This launcher is the branded double-clickable item; +x so pcmanfm treats it as
# a trusted launcher.
FOLDER_LAUNCHER="$SCRIPT_DIR/TimeGrapher.desktop"
echo "==> Writing folder launcher -> $FOLDER_LAUNCHER"
rm -f "$FOLDER_LAUNCHER"
write_desktop_entry "$FOLDER_LAUNCHER"
chmod 755 "$FOLDER_LAUNCHER"

# Desktop shortcut, replacing any existing one. xdg-user-dir falls back to $HOME
# when no DESKTOP dir is configured (e.g. headless) — skip rather than litter $HOME.
DESKTOP_DIR="$(xdg-user-dir DESKTOP 2>/dev/null || echo "$HOME/Desktop")"
if [ -d "$DESKTOP_DIR" ] && [ "$DESKTOP_DIR" != "$HOME" ]; then
  SHORTCUT_DEST="$DESKTOP_DIR/TimeGrapher.desktop"
  echo "==> Writing desktop shortcut -> $SHORTCUT_DEST"
  rm -f "$SHORTCUT_DEST"
  write_desktop_entry "$SHORTCUT_DEST"
  chmod 755 "$SHORTCUT_DEST"
else
  echo "==> No desktop folder ($DESKTOP_DIR); skipping the desktop shortcut."
fi

# Best-effort cache refresh so the entry/icon appear without a re-login.
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f "$ICON_DIR" >/dev/null 2>&1 || true
fi

echo
echo "Done. Launch 'TimeGrapher' from the menu, or run:"
echo "    $LAUNCHER"
echo "Headless self-check (no GUI):"
echo "    $LAUNCHER --smoke"
