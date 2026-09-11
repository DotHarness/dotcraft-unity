using System.IO;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using UnityEditor.PackageManager;
using UnityEngine;

namespace DotCraft.Unity.Development
{
    public static class DevelopmentTools
    {
        [AgentTool(
            Name = "dotcraft_unity_development_status",
            Description = "Report the status of the dotcraft-unity development project.",
            Kind = AcpToolKind.Read)]
        public static object GetStatus()
        {
            var package = PackageInfo.FindForAssembly(typeof(AgentToolAttribute).Assembly);
            return new
            {
                unityVersion = Application.unityVersion,
                projectPath = Directory.GetParent(Application.dataPath)?.FullName,
                packageName = package?.name,
                packageVersion = package?.version
            };
        }
    }
}
