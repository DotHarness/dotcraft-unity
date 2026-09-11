using System;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.Execution
{
    [InitializeOnLoad]
    internal static class EditorExecutionScheduler
    {
        static readonly UnityContinuationScheduler Scheduler = new UnityContinuationScheduler(() => EditorApplication.timeSinceStartup);
        static int active;
        static bool previousRunInBackground;

        static EditorExecutionScheduler()
        {
            EditorApplication.update += Scheduler.Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        static void Stop()
        {
            Scheduler.Invalidate();
            if (active == 0) return;
            Application.runInBackground = previousRunInBackground;
            active = 0;
        }

        internal static UnityExecutionContext CreateContext(CancellationToken token) => new UnityExecutionContext(token, Scheduler.Schedule);

        internal static IDisposable Begin()
        {
            if (active++ == 0) { previousRunInBackground = Application.runInBackground; Application.runInBackground = true; }
            return new Scope();
        }

        sealed class Scope : IDisposable
        {
            bool disposed;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (active > 0 && --active == 0) Application.runInBackground = previousRunInBackground;
            }
        }
    }
}
