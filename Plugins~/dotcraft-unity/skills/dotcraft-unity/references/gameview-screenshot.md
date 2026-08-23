# Editor Window Screenshots

Use this reference when the user asks to capture, compare, inspect, or save what a Unity Editor
window looks like — GameView, SceneView, or the Editor as a whole.

## Capture From Outside The Process

Unity exposes no public API for capturing an arbitrary `EditorWindow`. The entry points that
appear to do it are internal, carry no compatibility guarantee, and are not present on every
Editor — so a snippet built on one fails as a bare compile error on the next machine.

Capture the Editor's window at the OS level instead. This needs no Unity API at all, so it cannot
drift with the Editor version, and it has two properties that matter for this skill:

- **It does not disturb the Editor.** No window is focused, shown, or re-laid-out, which keeps
  faith with the default behaviour rules in `SKILL.md`.
- **It does not perturb what it documents.** Every in-Editor route allocates a render target and
  forces an extra render. When the screenshot exists to document a memory or GPU-memory
  measurement, that changes the number the picture is supposed to explain.

The tradeoff is honest to state: this captures the whole Editor window, not a single pane. See
[Cropping to one pane](#cropping-to-one-pane) if a single pane is required.

## Windows

`PrintWindow` with `PW_RENDERFULLCONTENT` (flag `2`) — the flag is required for
hardware-accelerated windows, which the Editor is.

```powershell
param([int]$TargetPid, [string]$Out)
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinCap {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
$h = (Get-Process -Id $TargetPid).MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw "no main window" }
if ([WinCap]::IsIconic($h)) { throw "window is minimised - capture would be blank" }

$r = New-Object WinCap+RECT
[void][WinCap]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $gfx.GetHdc()
try { $ok = [WinCap]::PrintWindow($h, $hdc, 2) } finally { $gfx.ReleaseHdc($hdc) }
if (-not $ok) { $gfx.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $ht)) }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$gfx.Dispose(); $bmp.Dispose()
"saved $Out (${w}x${ht}, PrintWindow=$ok)"
```

Get the process id from the Editor itself so the right instance is captured when several are
running:

```csharp
return System.Diagnostics.Process.GetCurrentProcess().Id;
```

The `CopyFromScreen` fallback only produces a correct image if the window is unoccluded, so treat
a `PrintWindow=False` result as lower-confidence evidence.

## macOS

`screencapture -x -l <windowID> out.png` captures a single window without the shutter sound.
Resolve the window id via `CGWindowListCopyWindowInfo` (for example through a small Swift or
Python helper) filtered by the Unity process id.

## Cropping To One Pane

When a single pane is genuinely needed, read its rectangle from the Editor and crop the full-window
capture to it. `EditorWindow.position` is public API:

```csharp
using System.Linq;
var t = Dcu.Type("UnityEditor.SceneView", throwIfMissing:false);   // or the window type you want
if (t == null) return "window type not present";
var win = Resources.FindObjectsOfTypeAll(t).OfType<EditorWindow>().FirstOrDefault();
return win == null ? "not open" : win.position.ToString();
```

Verify the coordinate mapping once on the target setup before relying on it: the rect is reported
in the Editor's own coordinate space, and how that maps onto the captured bitmap depends on window
decoration, docking and display scaling. Capture once, crop, and eyeball the result — after that
the offset is stable for that layout.

## Reading The Result

Check the image rather than assuming success. A blank or black capture is a signal worth acting
on, not something to retry: a minimised or long-backgrounded Editor may not be redrawing at all,
which also invalidates any measurement taken at that moment.

The output should show the Editor window as it appears on screen. If the user asked specifically
about GameView content and no camera is rendering, the capture will faithfully show that — check
`Camera.allCamerasCount` before concluding the capture failed.
