# Editor Window Screenshots

Use this reference when the user asks to capture, compare, inspect, or save what a Unity Editor
window looks like — GameView, SceneView, or the Editor as a whole.

## Capture From Outside The Process

Capture the Editor window at the OS level, not through Unity. Nothing is focused, shown or
re-laid-out, and no extra render is forced, which matters when the screenshot documents a memory
or GPU measurement it would otherwise perturb.

On Windows, run the bundled `scripts/Capture-EditorWindow.ps1` from the agent process:

```powershell
pwsh -File <skill>/scripts/Capture-EditorWindow.ps1 -TargetPid <editor pid> -Out <absolute .png>
```

Take the PID from the selected Editor (`status --pid`, or `System.Diagnostics.Process.GetCurrentProcess().Id`
in a snippet) so the right instance is captured when several are running. The result reports
`PrintWindow=False` when it fell back to copying the screen, which is only correct for an
unoccluded window; treat that image as lower-confidence evidence.

On macOS, `screencapture -x -l <windowID> out.png` captures one window; resolve the window ID with
`CGWindowListCopyWindowInfo` filtered by the Unity process ID.

## Cropping To One Pane

When a single pane is genuinely needed, run `scripts/editor-window-rect.cs` (`windowType`,
default `UnityEditor.GameView`) to read the pane's rectangle, then pass
`-Crop "x,y,width,height"` to the capture script. The rectangle is in the Editor's coordinate
space; window decoration, docking and display scaling offset it on the bitmap. Calibrate the
offset once by capturing, cropping and checking the result; it then stays stable for that layout.

## Rendering A Camera Viewpoint

When the user needs a specific viewpoint rather than what the window shows, run
`scripts/camera-render.cs` with `out` (absolute `.png` path) and optionally `camera`, `width`,
`height` and `frames`. It renders at the Game view size, waits out temporal history, disables
asynchronous shader compilation while rendering, and restores the camera afterwards. Position the
camera in a separate call first if needed. It forces extra renders, so do not use it as evidence
for a performance or memory measurement.

## Reading The Result

Check the image rather than assuming success. A blank or black capture is a signal worth acting
on, not something to retry: a minimised or long-backgrounded Editor may not be redrawing at all,
which also invalidates any measurement taken at that moment.

A uniform or unexpected image can be the real view: compare it with a window capture before
concluding that the render failed. If no camera is rendering, the capture faithfully shows that;
check `Camera.allCamerasCount`.
