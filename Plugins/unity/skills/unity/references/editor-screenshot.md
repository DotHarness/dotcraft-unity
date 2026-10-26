# Editor window screenshots

Use this reference when the user asks to capture, compare, inspect, or save what a Unity Editor
window looks like — GameView, SceneView, or the Editor as a whole.

## Capture from outside the process

Capture the Editor window at the OS level so the operation does not focus, show, or lay out Unity
windows or force another render. Run the bundled `scripts/Capture-EditorWindow.ps1`:

```powershell
pwsh -File <skill-directory>/scripts/Capture-EditorWindow.ps1 -TargetPid <pid> -Out <absolute .png>
```

Use the PID selected through `unity.list` and `unity.connect` so the right instance is captured
when several Editors are running. The result reports `PrintWindow=False` when it fell back to
copying the screen, which is only correct for an unoccluded window; treat that image as
lower-confidence evidence.

## Crop to one pane

When a single pane is genuinely needed, run `scripts/editor-window-rect.cs` with `unity.execute`
(`windowType`, default `UnityEditor.GameView`) to read its rectangle, then pass
`-Crop "x,y,width,height"` to the capture script. The rectangle is in the Editor's coordinate
space; window decoration, docking and display scaling offset it on the bitmap. Calibrate the offset
once by capturing, cropping and checking the result; it then stays stable for that layout.

## Render a camera viewpoint

When the user needs a specific viewpoint rather than what the window shows, run
`scripts/camera-render.cs` with `out` (absolute `.png` path) and optionally `camera`, `width`,
`height` and `frames`. It renders at the Game view size, waits out temporal history, disables
asynchronous shader compilation while rendering, and restores the camera afterwards. It forces
extra renders, so do not use it as evidence for a performance or memory measurement.

## Read the result

Check the image rather than assuming success. A blank or black capture is a signal worth acting
on, not something to retry: a minimised or long-backgrounded Editor may not be redrawing at all,
which also invalidates any measurement taken at that moment.

A uniform or unexpected image can be the real view: compare it with a window capture before
concluding that the render failed. If no camera is rendering, the capture faithfully shows that;
check `Camera.allCamerasCount`.
