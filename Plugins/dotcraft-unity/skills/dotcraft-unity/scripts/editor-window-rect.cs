// Returns an open Editor window's position in Editor coordinates, for cropping a window capture.
// Args: windowType (full type name, default "UnityEditor.GameView").

var typeName = (string)Args["windowType"] ?? "UnityEditor.GameView";
var type = Dcu.Type(typeName, throwIfMissing: false);
if (type == null) return $"{typeName} is not present on this Editor.";
var window = Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault();
if (window == null) return $"No {typeName} window is open.";
var position = window.position;
return new { windowType = typeName, x = position.x, y = position.y, width = position.width, height = position.height };
