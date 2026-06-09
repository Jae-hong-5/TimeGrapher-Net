# Linux (Raspberry Pi 5) — install · run · desktop integration

**English** · [한국어](README.ko.md)

The release's `TimeGrapher-<version>-linux-arm64.tar.gz` ships this README, the app
binary, the icon (`AppIcon-256.png`), the desktop entry (`TimeGrapher.App.desktop`),
and the installer script (`install.sh`).

## 1. Quick install (recommended)

Extract and run `install.sh` once — it installs dependencies, sets the executable bit,
and registers the icon/desktop entry. The desktop entry's `Exec`/`Icon` paths are **set
automatically to the extract location**.

```bash
mkdir -p ~/timegrapher
tar -xzf TimeGrapher-*-linux-arm64.tar.gz -C ~/timegrapher
cd ~/timegrapher
./install.sh           # apt deps + chmod + icon/.desktop install (skip deps: --no-deps)
./TimeGrapher.App      # or 'TimeGrapher' from the menu/taskbar
```

- `install.sh` is idempotent (safe to re-run). It installs dependencies only when
  `apt-get` is present, and uses `sudo` when not root.
- Headless/SSH check: `./TimeGrapher.App --smoke` (self-check without a GUI; exit code 0 on success).

Being a self-contained build, **no .NET runtime install is needed.** Sections 2 and 3
below are for doing what `install.sh` automates by hand, or for troubleshooting.

## 2. Runtime dependencies (manual — once on a fresh Pi OS)

The self-contained bundle includes the .NET runtime but not the system X11/font
libraries. If no window appears or fonts are missing, install them:

```bash
sudo apt update
sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1 xwayland
```

- `libx11-6 libice6 libsm6` — Avalonia's X11 backend. Without them the windowing backend fails to initialize.
- `libfontconfig1` — fonts. Without it text rendering / startup fails.
- `xwayland` — Pi OS defaults to a Wayland session, but Avalonia uses the X11 backend and
  runs via XWayland. If no window appears, suspect this first.
- (Optional) For direct DRM/KMS fullscreen, you also need `libgbm1 libdrm2 libinput10`.

> ICU (`libicu`) is **not needed.** The app is built in invariant globalization mode
> (`InvariantGlobalization=true`), so .NET does not require system ICU.

> A 64-bit userland is required: `dpkg --print-architecture` must report `arm64`
> (an armhf userland cannot run this arm64 build).

On headless/SSH, check via the CLI smoke flag instead of the GUI:

```bash
./TimeGrapher.App --smoke   # headless self-check, exit code 0 on success
```

## 3. Desktop integration (manual — taskbar icon)

> `install.sh` handles this automatically (including path setup). The below is for manual
> install or for understanding how it works.

The Pi OS taskbar (wf-panel-pi, Wayland) does not use the icon the app window provides
(`_NET_WM_ICON`); it only uses the `Icon=` from the `.desktop` file matched to the window's
app-id (under XWayland, `WM_CLASS` = the entry assembly name `TimeGrapher.App`). So the
taskbar icon appears only after you install the two files below on the Pi.

```bash
# 1) icon (AppIcon-256.png shipped in the tarball — repo path: src/TimeGrapher.App/Assets/App/AppIcon-256.png)
mkdir -p ~/.local/share/icons
cp AppIcon-256.png ~/.local/share/icons/timegrapher.png

# 2) desktop entry (edit Exec/Icon paths to match your install location)
mkdir -p ~/.local/share/applications
cp TimeGrapher.App.desktop ~/.local/share/applications/
```

- The default `Exec`/`Icon` paths target the team Pi (`team5`), so edit them for other setups.
  (e.g. if you extracted to `~/timegrapher` above, `Exec=/home/<user>/timegrapher/TimeGrapher.App`)
- Keep the filename `TimeGrapher.App.desktop` (matching the app-id) so the panel matches it
  (`StartupWMClass` is set too).
- If it doesn't show up, restart the app; if it still doesn't, `pkill wf-panel-pi` (the panel auto-restarts).
