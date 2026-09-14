# Tools

This directory contains the standalone CLI, Attach runtime, DotCraft plugin, and release scripts. Unity does not import it.

## Release build

Install the .NET 10 SDK and Visual Studio C++ x64 build tools, then run:

```powershell
pwsh -File Tools/scripts/build.ps1 -OutputDirectory .artifacts/release
```

## Test

```powershell
dotnet test Tools/tests/DotCraft.Unity.Cli.Tests/DotCraft.Unity.Cli.Tests.csproj -c Release
dotnet test Tools/tests/DotCraft.Unity.Attach.Tests/DotCraft.Unity.Attach.Tests.csproj -c Release
```
