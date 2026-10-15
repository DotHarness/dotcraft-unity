# Unity integration

This specification defines the architecture and runtime invariants shared by the
DotCraft Unity integrations. It covers connection, execution, cancellation and
recovery.

## Components and responsibilities

The UPM integration runs inside the Unity Editor. It owns the in-Editor DotCraft
experience, the project tool registry and the HTTP Gateway that exposes enabled
Unity tools. It also owns main-thread dispatch and durable operation state for
work that intentionally survives a Gateway restart, such as compilation and
Domain Reload operations. C# source compilation runs outside Unity through the
global `dotcraft-unity` CLI; the Editor only loads the compiled assembly and
executes its entry point.

Attach is the host-side connection for a local Windows x64 Mono Editor. It has no
dependency on DotCraft Core, MCP or UnityEngine. It injects a small target-compiled
payload through a native bootstrap and exposes C# execution without requiring the
UPM integration.

The standalone CLI and MCP server provide the external tool interface. They select
either Gateway or Attach before dispatch. Gateway exposes the UPM integration's
enabled tool registry; Attach exposes only `unity_execute_csharp`.

The native DotCraft plugin has the stable source ID `DotCraft.Unity`. It exposes
`unity.list`, `unity.connect`, `unity.status`, `unity.execute`, `unity.recompile`,
`unity.wait` and `unity.disconnect` through Attach, and contributes the Editor
world-state section described below. Activation only registers these contributions
and must not attach to an Editor. List, status and non-terminating wait are
available in Plan mode; connect, execute, recompile and disconnect require approval
and are unavailable there.

The native plugin implements these tools as generated, strongly typed methods behind
an `AIFunctionToolSource`. Model arguments are bound from the generated schema, while
`ToolInvocationContext` and `CancellationToken` are injected by the Host and never
appear in that schema. Planning mode is frozen with each tool snapshot; live task,
Turn and call identity always comes from the invocation context. Tool methods return
`ToolExecutionResult` when they need to preserve Unity error codes, structured state
or an uncertain execution outcome.

The Agent plugin supplies instructions, reference material and reusable scripts
for agents using the CLI, MCP or `unity_execute_csharp`. It does not implement a
transport, inject into Unity or register native DotCraft tools.

## Backend selection and connection

Automatic selection chooses Gateway when either Gateway discovery or manifest
state exists for the project. A stale, incompatible or mismatched Gateway record
is an error on that backend; it must not fall through to Attach. With neither
record, automatic selection chooses Attach, which requires exactly one Editor
matching the project unless the caller supplies a PID. UPM-generated client
configuration explicitly selects Gateway.

Selection is frozen before the first possible remote dispatch. A failed,
disconnected, timed-out or otherwise uncertain operation must never be retried on
the other backend automatically because Unity may already have applied it.
Discovery, tool listing and Attach target listing must not inject or connect.

Gateway authenticates each request with the project discovery token and validates
the recorded process and protocol version. Product versions are informational and
must not participate in backend or compiler compatibility decisions. Gateway state
is scoped to the resolved project. Attach binds a selection to process ID, process
start time and project identity so PID reuse cannot silently change the target.

## Execution and result semantics

Unity owns execution dispatch. Unity API work starts on the Editor main thread,
and continuation helpers resume on that thread across frames, elapsed time or a
bounded predicate wait. Callers must not block the main thread while awaiting a
continuation.

The UPM integration invokes the Windows x64 CLI from the stable per-user location
`~/.craft/bin/dotcraft-unity.exe`. Compilation uses a versioned request/response
protocol over standard input and output. The CLI compiles against the Editor's
current assembly references and returns assembly bytes plus structured diagnostics.
Missing executables, unsupported platforms and protocol mismatches fail explicitly;
the UPM integration must not load an in-process compiler or search another path.

Cancellation is cooperative. A terminate request asks the target execution to
cancel and does not imply that arbitrary Unity work can be rolled back. Cancelling
an MCP or CLI await, losing a socket, or reaching a client timeout only stops the
wait; it does not prove whether dispatched work ran. Such an outcome is unknown
and must not be replayed automatically.

Attach execution progresses through `queued` and `running` to `completed`,
`failed` or `cancelled`. `lost` means the execution record can no longer be
identified in the selected process and domain. `unknown` means the client cannot
determine the outcome after dispatch. `unity.wait` may observe these states and
may request cooperative termination. Terminal records have bounded retention;
an expired execution ID returns `lost` and never starts the work again.

The Attach host compiles each snippet with a fully qualified entry type and sends
that type with the assembly path. The payload loads exactly that type and invokes
its public static `Run` method. It must not scan the assembly or fall back to a
hard-coded type name.

The CLI and MCP server await normal calls rather than creating an independent job
broker. The native DotCraft plugin may return an execution ID and use
`unity.wait` for bounded background waiting, but ownership remains with the same
DotCraft task and selected Editor.

## Attach lifecycle and recovery

Attach uses a stable per-user rendezvous keyed by Unity process ID and process
start time. Its metadata identifies the project, bridge protocol, runtime identity,
domain epoch, bridge session and generation. Requests must match the rendezvous
token and generation; protocol, runtime, process, project or generation mismatches
are rejected before new work is dispatched.

Bootstrap dispatch is serialized across clients and recorded in a dispatch journal
before injection. If dispatch has no proven outcome, clients observe the existing
journal and wait for a valid handshake for up to 60 seconds; cancellation and
target exit end that wait earlier. A timeout leaves the journal intact, and no
wait path injects again. A bootstrap thread that returns success is a proven
outcome: its only remaining effect is a queued main-thread bridge start, which is
idempotent within a domain, so its journal permits a later dispatch.

A bridge that answers from the current generation without completing metadata has
a busy Editor main thread. Clients wait up to 60 seconds for its handshake and then
report the Editor as busy; they never dispatch a bootstrap to a live bridge.

Each execution also carries an ID and domain generation. Duplicate starts return
the existing record, while missing or expired records return `lost` rather than
replaying work.

Each Attach service instance owns an independent client lease. Connecting adds or
refreshes that lease; disconnecting or disposing releases only that client after
its own task selections no longer need the bridge. Other live clients keep the
bridge eligible for lifecycle recovery.

Domain Reload invalidates the old managed bridge, generation and execution
contexts. A native lifecycle observer survives the managed domain and coordinates
at most one bootstrap dispatch for each observed domain epoch. Native callbacks
only capture lifecycle evidence; managed and Unity calls occur outside those
callbacks. Recovery is complete only after a fresh main-thread handshake returns
matching metadata. A disconnect, log silence or fixed delay is not evidence of a
new domain, and a compilation failure without reload is not a recovery event.
Neither recovery path replays user operations.

## Editor state and notifications

The Attach bridge publishes the Editor state the agent needs between calls in a
state record next to its rendezvous: bridge generation, domain epoch, play mode
(edit, playing or paused), whether scripts are compiling, whether a Domain Reload
has begun, and the latest script compilation. The bridge rewrites the record from
the Editor main thread when one of those values changes, and only while it is
active, so the record never claims more than the bridge observed. A compilation
carries a sequence number, its outcome and a bounded list of compiler errors. A
fresh bridge carries the previous compilation forward, because the compilation
that triggered a reload finished in the domain that reload discarded.

The native plugin turns that record into one world-state section for each task
that has selected an Editor. The section names the project, Editor version and
PID, the connection (ready, reloading scripts, or no longer running), play mode,
compilation state and the errors of a failed compilation. It is read from the
record and never contacts the Editor, so it is cheap at every sampling boundary
and silent while nothing changes. When a task disconnects or its Editor exits,
the section says so once and then stays silent.

`unity.recompile` imports changed assets and requests one script compilation, then
waits for that request's compilation, identified by its sequence number, rather
than one that was already running. A failed compilation returns its errors. A
successful one waits for the resulting Domain Reload to finish recovery, or for
the Editor to settle without a reload. The request is sent once; a wait that ends
before the outcome returns the current state and leaves the outcome to the
world-state section.

A background execution that finishes after its initial wait notifies its task once,
unless the task already observed the terminal state through `unity.wait`. A running
Turn receives the result as guidance; an idle task receives it as queued input from
the Unity app, which starts a Turn. Consecutive Unity-started Turns are bounded so
that executions which keep finishing cannot keep a task running on their own.
Notifications report outcomes only; they never replay or resume work.

## Compatibility and safety invariants

- The bridge protocol is explicitly versioned. Unsupported protocol versions fail
  closed rather than attempting a best-effort call.
- Gateway discovery, tool manifests and external compilation are compatible by
  protocol version, never by matching product release versions.
- The UPM integration must not ship or load Roslyn compiler assemblies. Roslyn is
  private to the external CLI, while assembly loading and Unity API execution stay
  inside the Editor process.
- Attach runtime identity covers the host runtime, injected payload and native
  bootstrap. A different identity requires a fresh Editor process; the client must
  not replace a live bridge in place.
- Domain generation and bridge session identity prevent handles from crossing a
  reload or attaching to a replacement bridge.
- The native DotCraft plugin uses DotCraft contracts supplied by its host. It must
  not bundle or shadow host runtime assemblies; private implementation dependencies
  remain isolated from those host identities.
- Unsupported platform or native lifecycle capabilities return an explicit error.
  They must not degrade into an unverified injection or recovery path.
- No transport failure, timeout, cancellation, reload or recovery condition
  authorizes automatic replay of a user operation.
