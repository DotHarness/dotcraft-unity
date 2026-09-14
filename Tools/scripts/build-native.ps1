$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Visual Studio C++ build tools are required.' }
$vcvars = Join-Path $visualStudio 'VC/Auxiliary/Build/vcvars64.bat'
$native = Join-Path $PSScriptRoot '../src/DotCraft.Unity.Attach/Native'
New-Item -ItemType Directory -Force (Join-Path $native 'obj') | Out-Null
$command = 'call "{0}" >nul && cl /nologo /LD /EHsc /MT /O2 "{1}" /Fo"{2}" /link /OUT:"{3}" /IMPLIB:"{4}"' -f $vcvars,(Join-Path $native 'Bootstrap.cpp'),(Join-Path $native 'obj/Bootstrap.obj'),(Join-Path $native 'DotCraft.Unity.Native.dll'),(Join-Path $native 'obj/DotCraft.Unity.Native.lib')
& cmd.exe /d /c $command
if ($LASTEXITCODE -ne 0) { throw 'Native Attach build failed.' }
