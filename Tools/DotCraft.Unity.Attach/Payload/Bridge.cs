using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Unity
{
    public static class Bridge
    {
        sealed class Work
        {
            public JObject Request;
            public string State = "queued";
            public string Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim();
        }

        sealed class Execution
        {
            public string Id;
            public string AssemblyPath;
            public JObject Args;
            public string State = "queued";
            public string Result;
            public string Error;
            public Task<object> Task;
            public bool CancellationRequested;
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly ManualResetEventSlim Changed = new ManualResetEventSlim();
            public readonly DateTime StartedUtc = DateTime.UtcNow;
        }

        static readonly object Sync = new object();
        static readonly Queue<Work> Queue = new Queue<Work>();
        static readonly Queue<Action> MainThreadActions = new Queue<Action>();
        static readonly Dictionary<string, Work> Requests = new Dictionary<string, Work>();
        sealed class ClientLease { public int Pid; public DateTime StartUtc; }
        static readonly Dictionary<string, ClientLease> Clients = new Dictionary<string, ClientLease>();
        static string leasePath;
        static double nextLeaseCheck;
        [DllImport("DotCraft.Unity.Native.dll")] static extern void SetRecoveryEnabled(int enabled);
        [DllImport("DotCraft.Unity.Native.dll")] static extern ulong GetDomainEpoch();
        static readonly Dictionary<string, Execution> Executions = new Dictionary<string, Execution>();
        static UnityContinuationScheduler Scheduler = new UnityContinuationScheduler(() => EditorApplication.timeSinceStartup);
        static TcpListener listener;
        static volatile bool active;
        static string token, generation;
        static int mainThread, activeExecutionCount;
        static long updateTick;
        static bool savedRunInBackground;
        static readonly JsonSerializerSettings ResponseJson = new JsonSerializerSettings
        {
            Converters = { new UnityJsonConverter() }
        };

        public static void Start(string path, uint bootstrapThread, uint nativeMain)
        {
            if (active) return;
            lock (Sync)
            {
                Queue.Clear();
                MainThreadActions.Clear();
                Requests.Clear();
                Executions.Clear();
                Scheduler.Invalidate();
            }
            leasePath = path + ".leases";
            Clients.Clear();
            if (File.Exists(leasePath)) { try { foreach (var pair in JsonConvert.DeserializeObject<Dictionary<string, ClientLease>>(File.ReadAllText(leasePath))) Clients[pair.Key] = pair.Value; } catch { } }
            mainThread = Thread.CurrentThread.ManagedThreadId;
            token = Guid.NewGuid().ToString("N");
            generation = Guid.NewGuid().ToString("N");
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            active = true;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(new {
                protocol = 1, pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                startUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                port = ((IPEndPoint)listener.LocalEndpoint).Port, token, generation,
                bridgeSession = generation, domainEpoch = GetDomainEpoch(), runtimeIdentity = RuntimeIdentity.Value, runtimeVersion = RuntimeIdentity.Version,
                project = Path.GetDirectoryName(Application.dataPath), version = Application.unityVersion,
                nativeMain, bootstrapThread, mainThread,
                context = SynchronizationContext.Current.GetType().FullName
            }));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporaryPath, path);
            new Thread(Accept) { IsBackground = true }.Start();
        }

        static void Accept()
        {
            while (active)
            {
                try { var client = listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(_ => Handle(client)); }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        static void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = client.SendTimeout = 35000;
                    var stream = client.GetStream();
                    var reader = new BinaryReader(stream, Encoding.UTF8, true);
                    var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                    int length = reader.ReadInt32();
                    if (length < 1 || length > 4 * 1024 * 1024) return;
                    var bytes = reader.ReadBytes(length);
                    if (bytes.Length != length) return;
                    var request = JObject.Parse(Encoding.UTF8.GetString(bytes));
                    if ((int?)request["protocol"] != 1) return;
                    if ((string)request["token"] != token || (string)request["generation"] != generation) return;
                    var id = (string)request["id"];
                    if (string.IsNullOrEmpty(id)) return;

                    string result;
                    switch ((string)request["command"])
                    {
                        case "execute_start": result = StartExecution(request); break;
                        case "execute_wait": result = WaitExecution(request); break;
                        default: result = RunWork(request); break;
                    }
                    var output = Encoding.UTF8.GetBytes(result);
                    writer.Write(output.Length);
                    writer.Write(output);
                    writer.Flush();
                }
                catch (Exception) { }
            }
        }

        static string StartExecution(JObject request)
        {
            var executionId = (string)request["executionId"];
            var assemblyPath = (string)request["assembly"];
            if (string.IsNullOrEmpty(executionId) || string.IsNullOrEmpty(assemblyPath))
                return Serialize(new { state = "failed", generation, executionId, error = "Invalid execution request." });

            Execution execution;
            lock (Sync)
            {
                if (Executions.TryGetValue(executionId, out execution)) return Snapshot(execution);
                foreach (var expired in Executions.Values.Where(e => IsTerminal(e.State) && (DateTime.UtcNow - e.StartedUtc).TotalMinutes > 10).Select(e => e.Id).ToArray())
                    Executions.Remove(expired);
                var terminal = Executions.Values.Where(e => IsTerminal(e.State)).OrderBy(e => e.StartedUtc).ToArray();
                foreach (var retired in terminal.Take(Math.Max(0, terminal.Length - 1023))) Executions.Remove(retired.Id);
                if (!active || Executions.Values.Count(e => !IsTerminal(e.State)) >= 2048)
                    return Serialize(new { state = "failed", generation, executionId, error = "The execution queue is unavailable." });
                execution = new Execution
                {
                    Id = executionId,
                    AssemblyPath = assemblyPath,
                    Args = request["args"] as JObject ?? new JObject()
                };
                Executions.Add(executionId, execution);
                MainThreadActions.Enqueue(() => BeginExecution(execution));
            }
            RequestPump();
            return Snapshot(execution);
        }

        static string WaitExecution(JObject request)
        {
            var executionId = (string)request["executionId"];
            Execution execution;
            lock (Sync)
            {
                if (string.IsNullOrEmpty(executionId) || !Executions.TryGetValue(executionId, out execution))
                    return Serialize(new { state = "lost", generation, executionId });
                if ((bool?)request["terminate"] == true) CancelExecution(execution);
                execution.Changed.Reset();
            }

            var waitMs = Math.Max(0, Math.Min(30000, (int?)request["waitMs"] ?? 0));
            var deadline = Environment.TickCount + waitMs;
            while (!IsTerminal(execution.State) && waitMs > 0)
            {
                execution.Changed.Wait(waitMs);
                execution.Changed.Reset();
                waitMs = Math.Max(0, deadline - Environment.TickCount);
            }
            return Snapshot(execution);
        }

        static string RunWork(JObject request)
        {
            var id = (string)request["id"];
            Work work;
            lock (Sync)
            {
                if (!Requests.TryGetValue(id, out work))
                {
                    if (!active || Queue.Count >= 2048)
                        return Serialize(new { state = "failed", generation, error = "The bridge queue is unavailable." });
                    work = new Work { Request = request };
                    Requests.Add(id, work);
                    Queue.Enqueue(work);
                }
            }
            RequestPump();
            work.Done.Wait(10000);
            lock (Sync)
            {
                if (work.State == "queued") work.State = "cancelled";
                return work.Result ?? Serialize(new { state = work.State == "running" ? "unknown" : work.State, generation });
            }
        }

        static void Pump()
        {
            if (!active) return;
            updateTick++;
            if (EditorApplication.timeSinceStartup >= nextLeaseCheck) { PruneClients(); nextLeaseCheck = EditorApplication.timeSinceStartup + 5; }
            Scheduler.Pump();

            Action[] actions;
            lock (Sync)
            {
                actions = MainThreadActions.ToArray();
                MainThreadActions.Clear();
            }
            foreach (var action in actions)
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            PumpExecutionCompletions();

            Work work = null;
            lock (Sync)
            {
                if (Queue.Count > 0)
                {
                    work = Queue.Dequeue();
                    if (work.State == "queued") work.State = "running";
                }
            }
            if (work != null) CompleteWork(work);
            if (activeExecutionCount > 0) RequestPump();
        }

        static void CompleteWork(Work work)
        {
            if (work.State != "running")
            {
                lock (Sync) Requests.Remove((string)work.Request["id"]);
                work.Done.Set();
                return;
            }
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("Main thread changed.");
                object result;
                switch ((string)work.Request["command"])
                {
                    case "metadata":
                        result = new { version = Application.unityVersion, project = Path.GetDirectoryName(Application.dataPath),
                            playing = EditorApplication.isPlaying, compiling = EditorApplication.isCompiling,
                            updating = EditorApplication.isUpdating, debug = UnityEditor.Compilation.CompilationPipeline.codeOptimization.ToString(),
                            mainThread, generation, bridgeSession = generation, domainEpoch = GetDomainEpoch(), runtimeIdentity = RuntimeIdentity.Value, runtimeVersion = RuntimeIdentity.Version, asyncExecution = true, bridge = typeof(Bridge).Assembly.Location,
                            references = AppDomain.CurrentDomain.GetAssemblies().Select(a => {
                                try { return a.Location; } catch { return ""; }
                            }).Where(File.Exists).Distinct().ToArray() };
                        break;
                    case "lease": Clients[(string)work.Request["clientId"]] = new ClientLease { Pid = (int)work.Request["hostPid"], StartUtc = (DateTime)work.Request["hostStartUtc"] }; SaveClients(); result = new { attached = true }; break;
                    case "detach": Clients.Remove((string)work.Request["clientId"]); SaveClients(); result = new { detached = true }; break;
                    case "stop": result = "stopped"; Stop(); break;
                    default: throw new InvalidOperationException("Unknown command.");
                }
                work.Result = Serialize(new { state = "completed", generation, result });
            }
            catch (Exception e)
            {
                work.Result = Serialize(new { state = "failed", generation, error = (e.InnerException ?? e).ToString() });
            }
            finally { lock (Sync) { work.State = "completed"; work.Done.Set(); Requests.Remove((string)work.Request["id"]); } }
        }

        static void BeginExecution(Execution execution)
        {
            lock (Sync)
            {
                if (execution.State != "queued") return;
                if (execution.CancellationRequested)
                {
                    CompleteExecution(execution, "cancelled", null, null);
                    return;
                }
                execution.State = "running";
                execution.Changed.Set();
            }

            BeginExecutionRuntime();
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("Main thread changed.");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Editor is busy.");
                var assembly = Assembly.LoadFrom(execution.AssemblyPath);
                var method = assembly.GetType("DotCraft.Unity.Snippet").GetMethod("Run");
                var context = new UnityExecutionContext(execution.Cancellation.Token, ScheduleContinuation);
                execution.Task = (Task<object>)method.Invoke(null, new object[] { execution.Args, context, execution.Cancellation.Token });
            }
            catch (Exception e)
            {
                CompleteExecution(execution, "failed", null, (e.InnerException ?? e).ToString());
            }
        }

        static void FinishExecution(Execution execution, Task<object> task)
        {
            if (task.IsCanceled || execution.Cancellation.IsCancellationRequested)
                CompleteExecution(execution, "cancelled", null, null);
            else if (task.IsFaulted)
                CompleteExecution(execution, "failed", null, (task.Exception.InnerException ?? task.Exception).ToString());
            else
                CompleteExecution(execution, "completed", task.Result, null);
        }

        static void PumpExecutionCompletions()
        {
            Execution[] completed;
            lock (Sync)
            {
                completed = Executions.Values
                    .Where(item => item.State == "running" && item.Task != null && item.Task.IsCompleted)
                    .ToArray();
            }
            foreach (var execution in completed) FinishExecution(execution, execution.Task);
        }

        static void CompleteExecution(Execution execution, string state, object result, string error)
        {
            string serializedResult = null;
            if (state == "completed" && result != null)
            {
                try { serializedResult = JsonConvert.SerializeObject(UnityValueNormalizer.Normalize(result), ResponseJson); }
                catch (Exception e) { state = "failed"; error = e.ToString(); }
            }
            lock (Sync)
            {
                execution.State = state;
                execution.Result = serializedResult;
                execution.Error = error;
                execution.Changed.Set();
            }
            EndExecutionRuntime();
        }

        static void CancelExecution(Execution execution)
        {
            if (execution.CancellationRequested || IsTerminal(execution.State)) return;
            execution.CancellationRequested = true;
            if (Thread.CurrentThread.ManagedThreadId == mainThread) execution.Cancellation.Cancel();
            else MainThreadActions.Enqueue(() => execution.Cancellation.Cancel());
            if (execution.State == "queued") execution.State = "cancelled";
            execution.Changed.Set();
            RequestPump();
        }

        static string Snapshot(Execution execution)
        {
            lock (Sync)
            {
                if (execution.State == "completed")
                {
                    var result = execution.Result == null ? null : JToken.Parse(execution.Result);
                    return Serialize(new { state = execution.State, generation, executionId = execution.Id,
                        elapsedMs = ElapsedMilliseconds(execution), result });
                }
                return Serialize(new { state = execution.State, generation, executionId = execution.Id,
                    elapsedMs = ElapsedMilliseconds(execution), cancellationRequested = execution.CancellationRequested,
                    error = execution.Error });
            }
        }

        static long ElapsedMilliseconds(Execution execution)
        {
            return Math.Max(0, (long)(DateTime.UtcNow - execution.StartedUtc).TotalMilliseconds);
        }

        static bool IsTerminal(string state)
        {
            return state == "completed" || state == "failed" || state == "cancelled" || state == "lost";
        }

        static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, ResponseJson);
        }

        static void BeginExecutionRuntime()
        {
            if (activeExecutionCount++ != 0) return;
            savedRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
        }

        static void EndExecutionRuntime()
        {
            if (activeExecutionCount > 0) activeExecutionCount--;
            if (activeExecutionCount == 0) Application.runInBackground = savedRunInBackground;
        }

        internal static void ScheduleContinuation(UnityExecutionContext context, Action continuation,
            int frames, double seconds, Func<bool> predicate)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("Unity continuation scheduling requires the Editor main thread.");
            Scheduler.Schedule(context, continuation, frames, seconds, predicate);
            RequestPump();
        }
        static void PruneClients()
        {
            foreach (var pair in Clients.ToArray())
            {
                try
                {
                    using (var process = System.Diagnostics.Process.GetProcessById(pair.Value.Pid))
                        if (!process.HasExited && process.StartTime.ToUniversalTime() == pair.Value.StartUtc) continue;
                }
                catch { }
                Clients.Remove(pair.Key);
            }
            SaveClients();
        }

        static void SaveClients()
        {
            File.WriteAllText(leasePath, JsonConvert.SerializeObject(Clients));
            SetRecoveryEnabled(Clients.Count > 0 ? 1 : 0);
        }

        static void RequestPump()
        {
            try { EditorApplication.QueuePlayerLoopUpdate(); }
            catch { }
        }

        public static void Stop()
        {
            active = false;
            EditorApplication.update -= Pump;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.quitting -= Stop;
            if (listener != null) listener.Stop();
            lock (Sync)
            {
                foreach (var execution in Executions.Values) CancelExecution(execution);
                foreach (var work in Queue) { work.State = "cancelled"; work.Done.Set(); }
                Queue.Clear();
                MainThreadActions.Clear();
                Scheduler.Invalidate();
            }
            if (activeExecutionCount > 0)
            {
                activeExecutionCount = 0;
                Application.runInBackground = savedRunInBackground;
            }
        }
    }

}
