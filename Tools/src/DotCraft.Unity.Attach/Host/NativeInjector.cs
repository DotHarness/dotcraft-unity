using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DotCraft.Unity;

internal static class NativeInjector
{
    internal readonly record struct Module(string Name, string Path, nint Base, int Size);

    // Windows x64 ABI: preserve RBX, reserve shadow space, align RSP before calls.
    // RCX -> { LoadLibraryW, GetLastError, path, HMODULE, DWORD error, DWORD complete }.
    // Position independent; calls only validated target function addresses.
    private static readonly byte[] LoaderThunk =
    [
        0x53,                               // push rbx
        0x48, 0x83, 0xEC, 0x20,             // sub rsp, 32
        0x48, 0x89, 0xCB,                   // mov rbx, rcx
        0x48, 0x8B, 0x4B, 0x10,             // mov rcx, [rbx+16]
        0xFF, 0x13,                         // call [rbx]
        0x48, 0x89, 0x43, 0x18,             // mov [rbx+24], rax
        0xFF, 0x53, 0x08,                   // call [rbx+8] (GetLastError)
        0x89, 0x43, 0x20,                   // mov [rbx+32], eax
        0xC7, 0x43, 0x24, 1, 0, 0, 0,      // mov dword ptr [rbx+36], 1
        0x31, 0xC0,                         // xor eax, eax
        0x48, 0x83, 0xC4, 0x20,             // add rsp, 32
        0x5B, 0xC3                          // pop rbx; ret
    ];

    public static void Inject(int pid, string nativePath, string payloadPath, string config, AttachAttempt attempt)
    {
        nativePath = AttachStorage.ResolveFile(nativePath, "native-dll", attempt);
        payloadPath = AttachStorage.ResolveFile(payloadPath, "payload", attempt);
        attempt.Step("preflight", new { nativePath, payloadPath });
        AttachStorage.ValidateMonoPath(payloadPath, "payload");
        AttachStorage.ValidateMonoPath(nativePath, "native-dll");
        using var target = Process.GetProcessById(pid);
        var handle = OpenTarget(pid, attempt);
        try
        {
            attempt.Step("bootstrap-preflight");
            var local = NativeLibrary.Load(nativePath);
            try
            {
                var offset = NativeLibrary.GetExport(local, "Bootstrap") - local;
                using var self = Process.GetCurrentProcess();
                var localModule = Unique(Modules(self).Where(m => m.Base == local), "HostBootstrapModule", attempt);
                if (offset < 0 || offset >= localModule.Size)
                    throw new UnityTargetException("UnityAttachBootstrapExportInvalid", "Bootstrap export is outside the loaded module.");
                var remoteBase = LoadRemoteLibrary(target, handle, nativePath, attempt);
                attempt.Step("bootstrap-module", new { moduleBase = Hex(remoteBase) });
                target.Refresh();
                var remote = Unique(Modules(target).Where(m => m.Base == remoteBase), "InjectedBootstrapModule", attempt);
                if (remote.Size != localModule.Size || !SameFile(remote.Path, nativePath))
                    throw new UnityTargetException("UnityAttachBootstrapIdentityMismatch", "Loaded bootstrap file identity or size differs from the requested DLL.");
                var code = CallBootstrap(handle, remote.Base + offset, payloadPath + "\n" + config, attempt);
                attempt.Step("bootstrap-result", new { code });
                if (code == 9) throw new UnityTargetException("UnityAutomaticRecoveryUnsupported", "This Mono runtime does not export the required domain lifecycle observer capabilities.");
                if (code != 0) throw new UnityTargetException("UnityAttachBootstrapFailed", $"Native Bootstrap returned stage {code}.");
            }
            finally { NativeLibrary.Free(local); }
        }
        finally { CloseHandle(handle); }
    }

    internal static nint OpenTarget(int pid, AttachAttempt attempt)
    {
        attempt.Step("open-process");
        var handle = OpenProcess(0x043A, false, pid);
        if (handle == 0) throw Win32("OpenProcess", attempt);
        try
        {
            if (!IsWow64Process2(handle, out var machine, out var nativeMachine)) throw Win32("IsWow64Process2", attempt);
            if (machine != 0 || nativeMachine != 0x8664 || !Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("Attach requires Windows x64 host and target processes.");
            return handle;
        }
        catch { CloseHandle(handle); throw; }
    }

    internal static nint LoadRemoteLibrary(Process target, nint handle, string nativePath, AttachAttempt attempt, uint timeoutMs = 10000)
    {
        attempt.Step("resolve-loader");
        var library = NativeLibrary.Load("kernel32.dll");
        try
        {
            using var self = Process.GetCurrentProcess();
            var hostModules = Modules(self);
            var targetModules = Modules(target);
            var load = ResolveExport(NativeLibrary.GetExport(library, "LoadLibraryW"), hostModules, targetModules, attempt);
            var getError = ResolveExport(NativeLibrary.GetExport(library, "GetLastError"), hostModules, targetModules, attempt);
            var pathBytes = Encoding.Unicode.GetBytes(Path.GetFullPath(nativePath) + "\0");
            var data = new byte[40 + pathBytes.Length];
            var memory = Allocate(handle, data.Length, attempt);
            nint code = 0;
            var canFree = true;
            try
            {
                BitConverter.GetBytes((long)load).CopyTo(data, 0);
                BitConverter.GetBytes((long)getError).CopyTo(data, 8);
                BitConverter.GetBytes((long)(memory + 40)).CopyTo(data, 16);
                pathBytes.CopyTo(data, 40);
                Write(handle, memory, data, attempt);
                code = Allocate(handle, LoaderThunk.Length, attempt);
                Write(handle, code, LoaderThunk, attempt);
                if (!VirtualProtectEx(handle, code, (nuint)LoaderThunk.Length, 0x20, out _)) throw Win32("VirtualProtectEx", attempt);
                if (!FlushInstructionCache(handle, code, (nuint)LoaderThunk.Length)) throw Win32("FlushInstructionCache", attempt);
                var exit = RunThread(handle, code, memory, attempt, "load-library", timeoutMs, ref canFree);
                var result = new byte[40];
                if (!ReadProcessMemory(handle, memory, result, (nuint)result.Length, out var read) || read != (nuint)result.Length)
                    throw Win32("ReadProcessMemory", attempt);
                var module = (nint)BitConverter.ToInt64(result, 24);
                var error = BitConverter.ToUInt32(result, 32);
                var completed = BitConverter.ToUInt32(result, 36);
                attempt.Step("load-result", new { nativePath = Path.GetFullPath(nativePath), threadExit = exit,
                    moduleBase = Hex(module), win32Error = module == 0 ? error : 0, completed });
                if (exit != 0 || completed != 1)
                    throw new UnityTargetException("UnityAttachLoaderIncomplete", $"Remote loader did not complete normally (thread exit 0x{exit:X8}, marker {completed}). Do not replay.");
                if (module == 0)
                {
                    attempt.NoRemoteEffect();
                    throw new UnityTargetException("UnityAttachLoadLibraryFailed", $"Remote LoadLibraryW failed: Win32 {error} ({new Win32Exception((int)error).Message}).");
                }
                return module;
            }
            finally
            {
                if (canFree)
                {
                    if (code != 0) VirtualFreeEx(handle, code, 0, 0x8000);
                    VirtualFreeEx(handle, memory, 0, 0x8000);
                }
            }
        }
        finally { NativeLibrary.Free(library); }
    }

    internal static Module Unique(IEnumerable<Module> candidates, string role, AttachAttempt attempt)
    {
        var found = candidates.ToArray();
        attempt.Log("module-match", new { role, count = found.Length,
            candidates = found.Select(m => new { m.Name, m.Path, address = Hex(m.Base), m.Size }) });
        if (found.Length != 1)
            throw new UnityTargetException("UnityAttach" + role + (found.Length == 0 ? "NotFound" : "Ambiguous"),
                $"{attempt.Stage}: {role} matched {found.Length} modules; expected exactly one.");
        return found[0];
    }

    internal static nint ResolveExport(nint address, Module[] local, Module[] target, AttachAttempt attempt)
    {
        attempt.Log("resolve-export", new { address = Hex(address), hostModuleCount = local.Length,
            targetModuleCount = target.Length });
        var owner = Unique(local.Where(m => address >= m.Base && address - m.Base < m.Size), "HostExportOwner", attempt);
        var remote = Unique(target.Where(m => string.Equals(m.Name, owner.Name, StringComparison.OrdinalIgnoreCase)), "TargetSystemModule", attempt);
        var offset = address - owner.Base;
        if (owner.Size != remote.Size || offset < 0 || offset >= remote.Size || !SameFile(owner.Path, remote.Path))
            throw new UnityTargetException("UnityAttachSystemModuleMismatch", $"Cannot safely map {owner.Name} export to target module.");
        return remote.Base + offset;
    }

    internal static Module[] Modules(Process process) => process.Modules.Cast<ProcessModule>()
        .Select(m => new Module(m.ModuleName, m.FileName, m.BaseAddress, m.ModuleMemorySize)).ToArray();

    internal static bool SameFile(string first, string second)
    {
        using var a = File.OpenHandle(Path.GetFullPath(first), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var b = File.OpenHandle(Path.GetFullPath(second), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(a, out var x) || !GetFileInformationByHandle(b, out var y)) throw new Win32Exception();
        return x.Volume == y.Volume && x.IndexHigh == y.IndexHigh && x.IndexLow == y.IndexLow;
    }

    private static uint CallBootstrap(nint process, nint entry, string argument, AttachAttempt attempt)
    {
        var bytes = Encoding.Unicode.GetBytes(argument + "\0");
        var memory = Allocate(process, bytes.Length, attempt);
        var canFree = true;
        try
        {
            Write(process, memory, bytes, attempt);
            return RunThread(process, entry, memory, attempt, "bootstrap", 10000, ref canFree);
        }
        finally { if (canFree) VirtualFreeEx(process, memory, 0, 0x8000); }
    }

    internal static uint RunThread(nint process, nint entry, nint argument, AttachAttempt attempt,
        string stage, uint timeoutMs, ref bool canFree)
    {
        var safeBeforeDispatch = attempt.RetrySafe;
        attempt.Dispatch(stage);
        var thread = CreateRemoteThread(process, 0, 0, entry, argument, 0, out _);
        if (thread == 0)
        {
            var error = Win32("CreateRemoteThread", attempt);
            if (safeBeforeDispatch) attempt.NoRemoteEffect();
            throw error;
        }
        canFree = false;
        try
        {
            var wait = WaitForSingleObject(thread, timeoutMs);
            if (wait != 0)
            {
                if (wait == 0xFFFFFFFF) throw Win32("WaitForSingleObject", attempt);
                throw new UnityTargetException("UnityAttachRemoteTimeout", $"{stage}: remote thread did not finish in {timeoutMs} ms. Outcome unknown; do not replay.");
            }
            canFree = true;
            if (!GetExitCodeThread(thread, out var result)) throw Win32("GetExitCodeThread", attempt);
            return result;
        }
        finally { CloseHandle(thread); }
    }

    private static nint Allocate(nint process, int size, AttachAttempt attempt)
    {
        var memory = VirtualAllocEx(process, 0, (nuint)size, 0x3000, 4);
        if (memory == 0) throw Win32("VirtualAllocEx", attempt);
        return memory;
    }
    private static void Write(nint process, nint memory, byte[] bytes, AttachAttempt attempt)
    {
        if (!WriteProcessMemory(process, memory, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length)
            throw Win32("WriteProcessMemory", attempt);
    }
    private static UnityTargetException Win32(string api, AttachAttempt attempt)
    {
        var error = Marshal.GetLastWin32Error();
        attempt.Log("win32-failure", new { api, win32Error = error });
        return new UnityTargetException("UnityAttach" + api + "Failed", $"{attempt.Stage}: {api} failed: Win32 {error} ({new Win32Exception(error).Message}).");
    }
    private static string Hex(nint value) => $"0x{value:X}";
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoNative
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
    [DllImport("kernel32", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32", SetLastError = true)] private static extern bool IsWow64Process2(nint process, out ushort machine, out ushort nativeMachine);
    [DllImport("kernel32", SetLastError = true)] private static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32", SetLastError = true)] private static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint type);
    [DllImport("kernel32", SetLastError = true)] private static extern bool VirtualProtectEx(nint process, nint address, nuint size, uint protection, out uint previous);
    [DllImport("kernel32", SetLastError = true)] private static extern bool FlushInstructionCache(nint process, nint address, nuint size);
    [DllImport("kernel32", SetLastError = true)] private static extern bool WriteProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint read);
    [DllImport("kernel32", SetLastError = true)] private static extern nint CreateRemoteThread(nint process, nint attributes, nuint stackSize, nint start, nint parameter, uint flags, out uint id);
    [DllImport("kernel32", SetLastError = true)] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetExitCodeThread(nint thread, out uint code);
    [DllImport("kernel32")] internal static extern bool CloseHandle(nint handle);
}
