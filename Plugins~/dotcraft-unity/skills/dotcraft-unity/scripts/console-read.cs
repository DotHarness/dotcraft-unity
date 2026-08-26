// Reads the latest visible Unity Console rows without opening or focusing the Console window.
// Args: maxRows (int, default 20), maxMessageLength (int, default 500).

var maxRows = (int?)Args["maxRows"] ?? 20;
var maxMessageLength = (int?)Args["maxMessageLength"] ?? 500;

var asm = typeof(UnityEditor.Editor).Assembly;
var logEntriesType = asm.GetType("UnityEditor.LogEntries");
var logEntryType = asm.GetType("UnityEditor.LogEntry");
if (logEntriesType == null || logEntryType == null)
    return "UnityEditor.LogEntries or UnityEditor.LogEntry not found.";

var bindingFlags =
    System.Reflection.BindingFlags.Public |
    System.Reflection.BindingFlags.NonPublic |
    System.Reflection.BindingFlags.Static |
    System.Reflection.BindingFlags.Instance;

var getCount = logEntriesType.GetMethod("GetCount", bindingFlags);
var getCountsByType = logEntriesType.GetMethod("GetCountsByType", bindingFlags);
var getFilteringText = logEntriesType.GetMethod("GetFilteringText", bindingFlags);
var consoleFlagsProperty = logEntriesType.GetProperty("consoleFlags", bindingFlags);
var start = logEntriesType.GetMethod("StartGettingEntries", bindingFlags);
var end = logEntriesType.GetMethod("EndGettingEntries", bindingFlags);
var getEntry = logEntriesType.GetMethod(
    "GetEntryInternal",
    bindingFlags,
    null,
    new[] { typeof(int), logEntryType },
    null);
var getEntryCount = logEntriesType.GetMethod(
    "GetEntryCount",
    bindingFlags,
    null,
    new[] { typeof(int) },
    null);

if (getCount == null || getEntry == null)
    return "Required LogEntries methods were not found.";

var fields = logEntryType.GetFields(bindingFlags).ToDictionary(field => field.Name, field => field);
var visibleRows = (int)getCount.Invoke(null, null);
var firstRow = System.Math.Max(0, visibleRows - System.Math.Max(1, maxRows));

object[] countArgs = new object[] { 0, 0, 0 };
getCountsByType?.Invoke(null, countArgs);

var sb = new System.Text.StringBuilder();
sb.AppendLine($"VisibleRows={visibleRows}; ReturnedRows={visibleRows - firstRow}");
sb.AppendLine($"Counts errors={countArgs[0]}, warnings={countArgs[1]}, logs={countArgs[2]}");
sb.AppendLine($"FilteringText={getFilteringText?.Invoke(null, null) ?? ""}");
sb.AppendLine($"ConsoleFlags={consoleFlagsProperty?.GetValue(null, null) ?? ""}");

try
{
    start?.Invoke(null, null);
    for (var row = firstRow; row < visibleRows; row++)
    {
        var entry = System.Activator.CreateInstance(logEntryType);
        var okObject = getEntry.Invoke(null, new object[] { row, entry });
        if (okObject is bool ok && !ok) continue;

        var message = fields.TryGetValue("message", out var messageField)
            ? (string)(messageField.GetValue(entry) ?? "")
            : "";
        var file = fields.TryGetValue("file", out var fileField)
            ? (string)(fileField.GetValue(entry) ?? "")
            : "";
        var line = fields.TryGetValue("line", out var lineField)
            ? (int)lineField.GetValue(entry)
            : 0;
        var mode = fields.TryGetValue("mode", out var modeField)
            ? (int)modeField.GetValue(entry)
            : 0;
        var repeats = getEntryCount == null
            ? 1
            : (int)getEntryCount.Invoke(null, new object[] { row });

        var severity = (mode & (1 | 2 | 16 | 64 | 256 | 2048 | 1048576 | 4194304)) != 0
            ? "error"
            : ((mode & (128 | 512 | 4096)) != 0 ? "warning" : "log");

        message = message.Replace("\r", "\\r").Replace("\n", "\\n");
        if (message.Length > maxMessageLength)
            message = message.Substring(0, maxMessageLength) + "...";

        sb.AppendLine($"[{row}] {severity} x{repeats} {file}:{line} {message}");
    }
}
finally
{
    end?.Invoke(null, null);
}

return sb.ToString();
