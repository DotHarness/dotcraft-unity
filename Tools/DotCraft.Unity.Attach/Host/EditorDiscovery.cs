using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DotCraft.Unity;

internal static class EditorDiscovery
{
    internal static string? Project(Process process)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            NtQueryInformationProcess(process.Handle, 60, 0, 0, out var length);
            if (length == 0 || length > 1024 * 1024) return null;
            var buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (NtQueryInformationProcess(process.Handle, 60, buffer, length, out _) != 0) return null;
                var text = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, IntPtr.Size), Marshal.ReadInt16(buffer) / 2);
                var match = Regex.Match(text ?? "", "(?:^|\\s)-projectPath\\s+(?:\"(?<path>[^\"]+)\"|(?<path>\\S+))", RegexOptions.IgnoreCase);
                return match.Success ? Path.GetFullPath(match.Groups["path"].Value) : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { return null; }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(nint process, int informationClass, nint information, uint length, out uint returnedLength);
}
