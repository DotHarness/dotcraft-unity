# Console Reading

Use this reference when the user asks to inspect, summarize, search, or diagnose Unity Console logs.

## Run The Bundled Script

Run `scripts/console-read.cs` from this skill directory. `path` is project-relative, so prefix the
script with this skill's directory; pass `args` to size the result:

```json
{ "path": "<skill directory>/scripts/console-read.cs", "args": { "maxRows": 20 } }
```

`maxRows` defaults to 20 and `maxMessageLength` to 500. The script returns the visible row count,
error/warning/log counts, the active filtering text and console flags, then one line per row.

## Rules

- Read Console rows through `UnityEditor.LogEntries` reflection, which is what the script does. This
  works without opening or focusing the Console window.
- Do not call `LogEntries.Clear`, `SetFilteringText`, `SetConsoleFlag`, or otherwise change Console filters, collapse, timestamps, clear-on-play, or log-level buttons by default.
- Treat the reported row count as the current Console *view*: it reflects the user's active search text, log-level buttons, and collapse setting. That is why the script returns `FilteringText` and `ConsoleFlags` — read them before concluding that a log is absent.
- Raise `maxRows` deliberately. `LogEntry.message` can include stack traces, so a large window returns a lot of text.
- If the user wants results that ignore current filters, explain that changing filters is visible Editor state and ask before doing it.
