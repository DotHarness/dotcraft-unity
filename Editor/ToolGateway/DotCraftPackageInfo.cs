using System;
using UnityEditor.PackageManager;

namespace DotCraft.Editor.ToolGateway
{
    internal static class DotCraftPackageInfo
    {
        public const string PackageId = "com.dotcraft.unity";

        public static string Version
        {
            get
            {
                var package = PackageInfo.FindForAssetPath($"Packages/{PackageId}");
                if (!string.IsNullOrWhiteSpace(package?.version))
                    return package.version;

                throw new InvalidOperationException($"Package metadata for {PackageId} is unavailable.");
            }
        }
    }
}
