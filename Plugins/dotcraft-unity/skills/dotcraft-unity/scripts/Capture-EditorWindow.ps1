# Captures a Unity Editor window at the OS level, without focusing it or forcing a render.
# -Crop takes "x,y,width,height" in captured-bitmap pixels to keep one pane.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [int]$TargetPid,

    [Parameter(Mandatory = $true)]
    [string]$Out,

    [string]$Crop
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class DcuWindowCapture {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$handle = (Get-Process -Id $TargetPid).MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw "Process $TargetPid has no main window." }
if ([DcuWindowCapture]::IsIconic($handle)) { throw "The window is minimised; the capture would be blank." }

$rect = New-Object DcuWindowCapture+RECT
[void][DcuWindowCapture]::GetWindowRect($handle, [ref]$rect)
$width = $rect.R - $rect.L
$height = $rect.B - $rect.T
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
try { $printed = [DcuWindowCapture]::PrintWindow($handle, $hdc, 2) } finally { $graphics.ReleaseHdc($hdc) }
if (-not $printed) {
    $graphics.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size $width, $height))
}
$graphics.Dispose()

if ($Crop) {
    $x, $y, $w, $h = $Crop.Split(',') | ForEach-Object { [int]$_ }
    $cropped = $bitmap.Clone((New-Object System.Drawing.Rectangle $x, $y, $w, $h), $bitmap.PixelFormat)
    $bitmap.Dispose()
    $bitmap = $cropped
}

$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out ($($bitmap.Width)x$($bitmap.Height), PrintWindow=$printed)"
$bitmap.Dispose()
