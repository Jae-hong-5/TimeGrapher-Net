<#
  capture-screenshots.ps1
  ------------------------------------------------------------------
  Launches TimeGrapher in **Simulation** input mode (no microphone or
  watch needed), drives the UI through every graph tab via Windows UI
  Automation, and saves a screenshot of each tab into manual/img/.

  Captures only the TimeGrapher window region (not the whole desktop).

  Usage (from the repo root, after a Release build):
      powershell -ExecutionPolicy Bypass -File manual\capture-screenshots.ps1

  Re-run it any time the UI changes to refresh the manual's images.
  Each image's expected filename is shown in the manual when missing.
#>

[CmdletBinding()]
param(
  [string]$ExePath  = "$PSScriptRoot\..\src\TimeGrapher.App\bin\Release\net8.0\TimeGrapher.App.exe",
  [string]$OutDir   = "$PSScriptRoot\img",
  [int]$WarmupMs    = 6000,   # wait after pressing Play before first capture
  [int]$PerTabMs    = 1800    # settle time after switching tabs
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

# --- win32 helpers (foreground + maximize + window rect) ---
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$SW_MAXIMIZE = 3

if (-not (Test-Path $ExePath)) { throw "App not found: $ExePath  (build it first: dotnet build src/TimeGrapher.App -c Release)" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
$IdProp   = [System.Windows.Automation.AutomationElement]::AutomationIdProperty
$NameProp = [System.Windows.Automation.AutomationElement]::NameProperty
$Subtree  = [System.Windows.Automation.TreeScope]::Subtree
$Descend  = [System.Windows.Automation.TreeScope]::Descendants

function NewProp($prop, $val) {
  New-Object System.Windows.Automation.PropertyCondition($prop, $val)
}
function Find-ById($root, $id) {
  $root.FindFirst($Subtree, (NewProp $IdProp $id))
}
function Find-ByName($root, $name) {
  $root.FindFirst($Subtree, (NewProp $NameProp $name))
}
function Get-Pattern($el, $patternId) { $el.GetCurrentPattern($patternId) }

# --- clean up any leftover instances from a previous run ---
Get-Process -Name "TimeGrapher.App" -ErrorAction SilentlyContinue | ForEach-Object {
  try { $_.Kill() } catch {}
}
Start-Sleep -Milliseconds 500

# --- launch ---
Write-Host "Launching $ExePath ..."
$proc = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Milliseconds 3500

# find the real main window: a top-level window that contains the graph TabControl
# (title is e.g. "TimeGrapher v0.6.2"; a splash window may also be present briefly).
$root = $AE::RootElement
$win = $null
for ($i = 0; $i -lt 40 -and -not $win; $i++) {
  $kids = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($k in $kids) {
    $n = $k.Current.Name
    if ($n -and $n -like "TimeGrapher*") {
      if (Find-ById $k "GraphicsTabWidget") { $win = $k; break }
    }
  }
  if (-not $win) { Start-Sleep -Milliseconds 500 }
}
if (-not $win) { throw "TimeGrapher main window (with GraphicsTabWidget) not found via UI Automation." }
$hwnd = [IntPtr]$win.Current.NativeWindowHandle
Write-Host "Window found (hwnd=$hwnd). Maximizing..."
[Win32]::ShowWindow($hwnd, $SW_MAXIMIZE) | Out-Null
Start-Sleep -Milliseconds 1200

# --- capture helper: screenshot the window's on-screen rectangle ---
function Capture($file) {
  [Win32]::SetForegroundWindow($hwnd) | Out-Null
  Start-Sleep -Milliseconds 350
  $r = New-Object Win32+RECT
  [Win32]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  if ($w -le 0 -or $h -le 0) { Write-Warning "bad rect for $file"; return }
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $g.Dispose()
  $path = Join-Path $OutDir $file
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Host "  saved $file  (${w}x${h})"
}

# --- pattern ids ---
$Invoke = [System.Windows.Automation.InvokePattern]::Pattern
$Expand = [System.Windows.Automation.ExpandCollapsePattern]::Pattern
$SelItem= [System.Windows.Automation.SelectionItemPattern]::Pattern
$Toggle = [System.Windows.Automation.TogglePattern]::Pattern

# --- select Simulation input ---
Write-Host "Selecting 'Simulation' input..."
$combo = Find-ById $win "InputDeviceComboBox"
if (-not $combo) { $combo = Find-ByName $win "Input" }
if ($combo) {
  try { (Get-Pattern $combo $Expand).Expand(); Start-Sleep -Milliseconds 600 } catch {}
  $sim = Find-ByName $win "Simulation"
  if (-not $sim) { $sim = $combo.FindFirst($Descend, (NewProp $NameProp "Simulation")) }
  if ($sim) { try { (Get-Pattern $sim $SelItem).Select() } catch { Write-Warning "select Simulation: $_" } }
  else { Write-Warning "Simulation item not found." }
  try { (Get-Pattern $combo $Expand).Collapse() } catch {}
} else { Write-Warning "Input combo not found." }
Start-Sleep -Milliseconds 800

# --- enable Realistic ---
$realistic = Find-ById $win "RealisticCheckBox"
if ($realistic) {
  try {
    $tp = Get-Pattern $realistic $Toggle
    if ($tp.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $tp.Toggle() }
  } catch { Write-Warning "toggle Realistic: $_" }
}

# --- press Play ---
Write-Host "Pressing Play..."
$play = Find-ById $win "PlayPausePushButton"
if ($play) { try { (Get-Pattern $play $Invoke).Invoke() } catch { Write-Warning "invoke Play: $_" } }
else { Write-Warning "Play button not found." }

# Simulation/Live runs ask "Do you want to record this session?" — answer No so the
# run starts without recording (otherwise the modal blocks every graph).
function Dismiss-RecordDialog {
  # The dialog is a separate Avalonia window (its UIA Name may be empty), so
  # locate it by the Yes/No button pair rather than by title.
  for ($i = 0; $i -lt 24; $i++) {
    $kids = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
              [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($k in $kids) {
      $no  = Find-ByName $k "No"
      $yes = Find-ByName $k "Yes"
      if ($no -and $yes) {
        try { (Get-Pattern $no $Invoke).Invoke(); Write-Host "  dismissed Record dialog (No)"; return $true } catch {}
      }
    }
    Start-Sleep -Milliseconds 250
  }
  Write-Warning "Record dialog not seen (continuing)."
  return $false
}
Dismiss-RecordDialog | Out-Null

Write-Host "Warming up ($WarmupMs ms)..."
Start-Sleep -Milliseconds $WarmupMs

# overview = current (default) tab, full window
Capture "overview.png"

# left-panel crop from the overview
try {
  $ov = [System.Drawing.Image]::FromFile((Join-Path $OutDir "overview.png"))
  $cw = [Math]::Min([int][Math]::Ceiling($ov.Width * (360.0 / 1280.0)), $ov.Width); $ch = $ov.Height
  $crop = New-Object System.Drawing.Bitmap($cw, $ch)
  $cg = [System.Drawing.Graphics]::FromImage($crop)
  $cg.DrawImage($ov, (New-Object System.Drawing.Rectangle(0,0,$cw,$ch)),
                (New-Object System.Drawing.Rectangle(0,0,$cw,$ch)), [System.Drawing.GraphicsUnit]::Pixel)
  $cg.Dispose(); $crop.Save((Join-Path $OutDir "left-panel.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $crop.Dispose(); $ov.Dispose(); Write-Host "  saved left-panel.png (crop)"
} catch { Write-Warning "left-panel crop: $_" }

# --- iterate the graph tabs ---
$tabMap = @{
  "Rate/Scope" = "tab-rate-scope.png"
  "Sound Print"  = "tab-sound-print.png"
  "Trace"        = "tab-trace.png"
  "Sweep"        = "tab-sweep.png"
  "Vario"        = "tab-vario.png"
  "Beat Error"   = "tab-beat-error.png"
  "Filter Scope" = "tab-filter-scope.png"
  "Long-Term"    = "tab-long-term.png"
  "Positions"    = "tab-positions.png"
  "Health"       = "tab-health.png"
  "Beat Noise"   = "tab-beat-noise.png"
  "Escapement"   = "tab-escapement.png"
  "Waveforms"    = "tab-waveforms.png"
  "Spectrogram"  = "tab-spectrogram.png"
}

$tabControl = Find-ById $win "GraphicsTabWidget"
if (-not $tabControl) { throw "GraphicsTabWidget not found." }
$tabCond = NewProp ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) ([System.Windows.Automation.ControlType]::TabItem)
$tabs = $tabControl.FindAll($Descend, $tabCond)
Write-Host "Found $($tabs.Count) tab items."

foreach ($t in $tabs) {
  $title = $t.Current.Name
  $file = $tabMap[$title]
  if (-not $file) { Write-Warning "no mapping for tab '$title' — skipping"; continue }
  try { (Get-Pattern $t $SelItem).Select() } catch { Write-Warning "select tab '$title': $_" }
  Start-Sleep -Milliseconds $PerTabMs
  Capture $file
}

Write-Host "Done. Closing app..."
try { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800 } catch {}
try { if (-not $proc.HasExited) { $proc.Kill() } } catch {}
Write-Host "Screenshots in: $OutDir"
