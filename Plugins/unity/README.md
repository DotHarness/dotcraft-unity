# Unity

Inspect and automate Unity Editor. Read Editor state, execute C#, and perform editing operations.

Supports Windows x64 Mono Unity Editor.

## Enable

Install **Unity** (`DotCraft.Unity`) from the official DotCraft plugin marketplace, then enable it and trust its binaries. The plugin provides Unity tools and a companion skill. It does not require a UPM package in the Unity project.

## Use

Open a Unity project, then ask DotCraft to connect to its Editor. For example:

> Connect to Unity Editor and tell me its version and whether it is in Play Mode.

Pass inline code or a script path to `unity.execute`. Code can wait for Editor updates with `await ctx.WaitFrame()`. For longer work, set `runInBackground` and use the returned execution ID with `unity.wait`.
