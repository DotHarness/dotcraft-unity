// Renders one camera's view into a PNG and restores the camera, so the user's view is unchanged.
// Args: out (absolute .png path, required), camera (GameObject name, default Camera.main),
//       width / height (default: the main Game view size), frames (default 8).

var outPath = (string)Args["out"];
if (string.IsNullOrWhiteSpace(outPath)) return "out is required: an absolute .png path.";
var cameraName = (string)Args["camera"];
var cam = cameraName == null ? Camera.main : GameObject.Find(cameraName)?.GetComponent<Camera>();
if (cam == null) return cameraName == null ? "No main camera." : $"No camera on '{cameraName}'.";

var gameView = Handles.GetMainGameViewSize();
var width = (int?)Args["width"] ?? (int)gameView.x;
var height = (int?)Args["height"] ?? (int)gameView.y;
var frames = Math.Max(1, (int?)Args["frames"] ?? 8);

var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
var previousTarget = cam.targetTexture;
var previousActive = RenderTexture.active;
var asyncCompilation = ShaderUtil.allowAsyncCompilation;
ShaderUtil.allowAsyncCompilation = false;
try
{
    cam.targetTexture = target;
    for (var i = 0; i < frames; i++) cam.Render();
    RenderTexture.active = target;
    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
    System.IO.File.WriteAllBytes(outPath, pixels.EncodeToPNG());
}
finally
{
    cam.targetTexture = previousTarget;
    RenderTexture.active = previousActive;
    ShaderUtil.allowAsyncCompilation = asyncCompilation;
    UnityEngine.Object.DestroyImmediate(pixels);
    target.Release();
    UnityEngine.Object.DestroyImmediate(target);
}
return new { saved = outPath, camera = cam.name, width, height, frames };
