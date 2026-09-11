using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace DotCraft.Editor
{
    /// <summary>
    /// Persists automation progress outside the managed domain so external processes can
    /// observe work while Unity recompiles scripts and restarts the tool gateway. Callers may
    /// provide an operation ID before starting work so monitoring never depends on the reply.
    /// </summary>
    public static class DcuLongRunningOperation
    {
        private const int SchemaVersion = 1;
        private const string StatusRunning = "running";
        private const string StatusSucceeded = "succeeded";
        private const string StatusFailed = "failed";
        private const string KindManual = "manual";
        private const string KindScriptCompilation = "script-compilation";
        private const string KindDomainReload = "domain-reload";

        private static readonly object Gate = new();

        /// <summary>
        /// Directory containing operation state files for the current Unity project.
        /// </summary>
        public static string StateDirectory
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                    ?? throw new InvalidOperationException("Unity project root could not be resolved.");
                return Path.Combine(projectRoot, "UserSettings", "DotCraft", "operations");
            }
        }

        /// <summary>
        /// Creates a durable manual operation. Call <see cref="Report"/>,
        /// <see cref="Complete"/>, or <see cref="Fail"/> as work advances.
        /// </summary>
        public static DcuLongRunningOperationState Begin(string name)
        {
            return Create(KindManual, name);
        }

        /// <summary>
        /// Records a durable checkpoint for a running operation.
        /// </summary>
        public static DcuLongRunningOperationState Report(string id, string phase, string message = null)
        {
            return Update(id, state =>
            {
                EnsureRunning(state);
                state.Phase = RequiredText(phase, nameof(phase));
                state.Message = message;
            });
        }

        /// <summary>
        /// Marks a durable operation as successful.
        /// </summary>
        public static DcuLongRunningOperationState Complete(string id, string message = null)
        {
            return Update(id, state =>
            {
                EnsureRunning(state);
                state.Status = StatusSucceeded;
                state.Phase = "completed";
                state.Message = message;
            });
        }

        /// <summary>
        /// Marks a durable operation as failed.
        /// </summary>
        public static DcuLongRunningOperationState Fail(string id, string message)
        {
            return Update(id, state =>
            {
                EnsureRunning(state);
                state.Status = StatusFailed;
                state.Phase = "failed";
                state.Message = RequiredText(message, nameof(message));
            });
        }

        /// <summary>
        /// Creates an operation and asynchronously asks Unity to compile changed scripts.
        /// A successful compilation completes only after the following domain reload.
        /// </summary>
        public static DcuLongRunningOperationState RequestScriptCompilation(
            string name = "script-compilation",
            bool cleanBuildCache = false,
            string id = null)
        {
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity is already compiling scripts.");

            var state = Create(KindScriptCompilation, name, id);
            try
            {
                state = Report(state.Id, "compilation-requested");
                var options = cleanBuildCache
                    ? RequestScriptCompilationOptions.CleanBuildCache
                    : RequestScriptCompilationOptions.None;
                CompilationPipeline.RequestScriptCompilation(options);
                return state;
            }
            catch (Exception ex)
            {
                FailIfRunning(state.Id, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Creates an operation and asynchronously asks Unity to reload script assemblies
        /// on the next Editor frame without compiling changed code.
        /// </summary>
        public static DcuLongRunningOperationState RequestDomainReload(
            string name = "domain-reload",
            string id = null)
        {
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity is compiling scripts; wait before requesting a reload.");

            var state = Create(KindDomainReload, name, id);
            try
            {
                state = Report(state.Id, "domain-reload-requested");
                EditorUtility.RequestScriptReload();
                return state;
            }
            catch (Exception ex)
            {
                FailIfRunning(state.Id, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Reads the latest durable state for an operation.
        /// </summary>
        public static DcuLongRunningOperationState Read(string id)
        {
            lock (Gate)
                return ReadFile(StatePath(id));
        }

        /// <summary>
        /// Gets the durable JSON path for an operation.
        /// </summary>
        public static string GetStatePath(string id)
        {
            return StatePath(id);
        }

        internal static IReadOnlyList<DcuLongRunningOperationState> ReadRunning()
        {
            lock (Gate)
            {
                if (!Directory.Exists(StateDirectory))
                    return Array.Empty<DcuLongRunningOperationState>();

                return Directory.EnumerateFiles(StateDirectory, "*.json", SearchOption.TopDirectoryOnly)
                    .Select(ReadFile)
                    .Where(state => state != null && state.Status == StatusRunning)
                    .ToArray();
            }
        }

        internal static void MarkCompilationStarted()
        {
            UpdateMatching(
                state => state.Kind == KindScriptCompilation && state.Phase == "compilation-requested",
                state => state.Phase = "compiling");
        }

        internal static void MarkCompilationFinished(IReadOnlyList<string> errors)
        {
            UpdateMatching(
                state => state.Kind == KindScriptCompilation && state.Status == StatusRunning,
                state =>
                {
                    if (errors.Count > 0)
                    {
                        state.Status = StatusFailed;
                        state.Phase = "compilation-failed";
                        state.Message = string.Join("\n", errors.Take(20));
                    }
                    else
                    {
                        state.Phase = "compiled-awaiting-domain-reload";
                    }
                });
        }

        internal static void MarkBeforeDomainReload()
        {
            UpdateMatching(
                state => state.Status == StatusRunning && ExpectsDomainReload(state),
                state =>
                {
                    state.PhaseBeforeReload = state.Phase;
                    state.Phase = "before-domain-reload";
                    state.ReloadCount++;
                });
        }

        private static bool ExpectsDomainReload(DcuLongRunningOperationState state)
        {
            if (state.Kind == KindManual)
                return true;
            if (state.Kind == KindDomainReload)
                return state.Phase == "domain-reload-requested";
            if (state.Kind == KindScriptCompilation)
                return state.Phase == "compiling" || state.Phase == "compiled-awaiting-domain-reload";
            return false;
        }

        internal static void MarkAfterDomainReload()
        {
            UpdateMatching(
                state => state.Status == StatusRunning && state.Phase == "before-domain-reload",
                state =>
                {
                    if (state.Kind == KindScriptCompilation || state.Kind == KindDomainReload)
                    {
                        state.Status = StatusSucceeded;
                        state.Phase = "after-domain-reload";
                    }
                    else
                    {
                        state.Phase = "after-domain-reload";
                    }
                });
        }

        private static DcuLongRunningOperationState Create(string kind, string name, string id = null)
        {
            var now = DateTime.UtcNow.ToString("O");
            var state = new DcuLongRunningOperationState
            {
                SchemaVersion = SchemaVersion,
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                Name = RequiredText(name, nameof(name)),
                Kind = kind,
                Status = StatusRunning,
                Phase = "created",
                Revision = 1,
                ReloadCount = 0,
                EditorProcessId = Process.GetCurrentProcess().Id,
                StartedAtUtc = now,
                UpdatedAtUtc = now
            };

            lock (Gate)
            {
                if (File.Exists(StatePath(state.Id)))
                    throw new InvalidOperationException($"DotCraft operation already exists: {state.Id}");
                WriteFile(state);
            }
            return state;
        }

        private static DcuLongRunningOperationState Update(
            string id,
            Action<DcuLongRunningOperationState> change)
        {
            lock (Gate)
            {
                var state = ReadFile(StatePath(id))
                    ?? throw new InvalidOperationException($"DotCraft operation not found: {id}");
                change(state);
                Touch(state);
                WriteFile(state);
                return state;
            }
        }

        private static void UpdateMatching(
            Func<DcuLongRunningOperationState, bool> predicate,
            Action<DcuLongRunningOperationState> change)
        {
            lock (Gate)
            {
                foreach (var state in ReadRunning().Where(predicate))
                {
                    change(state);
                    Touch(state);
                    WriteFile(state);
                }
            }
        }

        private static void FailIfRunning(string id, string message)
        {
            var state = Read(id);
            if (state?.Status == StatusRunning)
                Fail(id, message);
        }

        private static void EnsureRunning(DcuLongRunningOperationState state)
        {
            if (state.Status != StatusRunning)
                throw new InvalidOperationException($"DotCraft operation is already {state.Status}: {state.Id}");
        }

        private static void Touch(DcuLongRunningOperationState state)
        {
            state.Revision++;
            state.EditorProcessId = Process.GetCurrentProcess().Id;
            state.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        }

        private static string StatePath(string id)
        {
            if (string.IsNullOrWhiteSpace(id)
                || id.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            {
                throw new ArgumentException("Operation id contains unsupported characters.", nameof(id));
            }

            return Path.Combine(StateDirectory, id + ".json");
        }

        private static DcuLongRunningOperationState ReadFile(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                return DotCraftJson.Deserialize<DcuLongRunningOperationState>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return null;
            }
        }

        private static void WriteFile(DcuLongRunningOperationState state)
        {
            Directory.CreateDirectory(StateDirectory);
            var path = StatePath(state.Id);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(
                temporaryPath,
                DotCraftJson.SerializeIndented(state),
                new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static string RequiredText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value cannot be empty.", parameterName);
            return value.Trim();
        }
    }

    /// <summary>
    /// Serializable snapshot of one durable Unity Editor automation operation.
    /// </summary>
    public sealed class DcuLongRunningOperationState
    {
        public int SchemaVersion { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public string Phase { get; set; }
        public string PhaseBeforeReload { get; set; }
        public string Message { get; set; }
        public int Revision { get; set; }
        public int ReloadCount { get; set; }
        public int EditorProcessId { get; set; }
        public string StartedAtUtc { get; set; }
        public string UpdatedAtUtc { get; set; }
    }

    [InitializeOnLoad]
    internal static class DcuLongRunningOperationHost
    {
        private static readonly List<string> CompilationErrors = new();

        static DcuLongRunningOperationHost()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            AssemblyReloadEvents.beforeAssemblyReload += DcuLongRunningOperation.MarkBeforeDomainReload;
            DcuLongRunningOperation.MarkAfterDomainReload();
        }

        private static void OnCompilationStarted(object context)
        {
            CompilationErrors.Clear();
            DcuLongRunningOperation.MarkCompilationStarted();
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var message in messages.Where(message => message.type == CompilerMessageType.Error))
                CompilationErrors.Add($"{message.file}:{message.line}:{message.column}: {message.message}");
        }

        private static void OnCompilationFinished(object context)
        {
            DcuLongRunningOperation.MarkCompilationFinished(CompilationErrors);
        }
    }
}
