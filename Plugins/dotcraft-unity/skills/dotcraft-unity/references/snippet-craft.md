# Snippet Failure Modes

`references/api.md` covers the `Dcu.*` helpers. This file covers the ways a snippet fails in
practice: it times out, it buries the answer in stack traces, or it fails to compile. These are ordered by how often they bite.

## Long Operations Outlive The Call

Long synchronous work blocks Unity's main thread and can exceed the caller's deadline. Gateway
uses a 65-second HTTP deadline; Attach does not apply that same overall execution deadline. A
timeout or disconnect can leave work running or already completed, so inspect state before
choosing the next action. Never replay an uncertain mutation automatically.

For script compilation, Domain Reload, or any workflow that must survive a Tool Gateway restart,
use the backend-specific workflow in `references/compilation-and-reload.md`. Gateway durable
operation files survive the managed domain; Attach recovers its bridge without resuming the snippet.

For bounded asynchronous work that cannot reload assemblies, await the next editor frame or a
readiness condition. These awaits resume on Unity's main thread:

```csharp
await ctx.WaitFrames(2);
cancellationToken.ThrowIfCancellationRequested();
return EditorApplication.isUpdating;
```

A synchronous loop must call `ctx.ThrowIfCancellationRequested()` explicitly. Stopping the caller's
wait is not proof that Unity stopped; inspect state after an uncertain transport outcome and do not
replay automatically. On Gateway, use a durable operation file for work that must survive Domain Reload. Attach-only
Editors require a persistent project integration or an external workflow for that purpose.

## Logs Return With Full Stack Traces

Gateway attaches logs emitted **while the snippet runs**, with stack traces. Attach
does not provide the same per-execution log capture; read the Console when logs are needed. One warning is fine. A loop over 120 items that warns once per item returns tens of
thousands of lines and buries the actual return value.

Keep diagnostics compact instead of moving work to an untracked callback. During a cross-frame
await other editor activity can also emit logs, so verify attribution before treating a log as a
failure caused by the snippet.

When the logs *are* the point, read them deliberately — see `references/console-reading.md`.

## Reading Compile Failures

A compile failure returns `errorCode: "CompilationFailed"` with a `diagnostics` list: each entry
carries the Roslyn id, message, line, and column. Line numbers refer to the snippet as written
(after the leading `using` directives). Read the diagnostics before changing anything; two causes
account for most of them.

**Ambiguous type names.** `System.Object` and `UnityEngine.Object` are both in scope, so bare
`Object` fails. Write `UnityEngine.Object.DestroyImmediate(...)`. The same applies to `Random`
and `Debug`.

**A type or overload absent on this Editor.** Internal API carries no compatibility guarantee and
differs between Unity versions. A direct reference to a missing method fails at compile time and
takes the whole snippet with it, including the diagnostics you wrote to find out what happened.

Two habits avoid this. Use `Dcu.Type(name, throwIfMissing:false)` and branch on `null` rather
than letting a lookup throw mid-diagnosis. And when a call fails to compile, enumerate the type
instead of guessing:

```csharp
using System.Linq;
using System.Reflection;

const string TYPE = "UnityEditor.SceneManagement.EditorSceneManager";  // the type you are about to call
const string NEEDLE = "Save";                                          // part of the method name

var t = Dcu.Type(TYPE, throwIfMissing:false);
if (t == null) return TYPE + " is not present on this Editor.";
var found = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Static | BindingFlags.Instance)
    .Where(m => m.Name.Contains(NEEDLE))
    .Select(m => m.Name + "(" + string.Join(", ", m.GetParameters()
        .Select(p => p.ParameterType.Name + " " + p.Name)) + ") -> " + m.ReturnType.Name)
    .ToArray();
return found.Length == 0
    ? "no method matching '" + NEEDLE + "' on " + TYPE
    : string.Join("\n", found);
```

This is usually faster than reading engine source, and it gives a definite answer: a documented
API that enumerates to an empty list is genuinely absent on this Editor, not being called wrongly.

## Reaching Internal API

`Dcu.Get`, `Dcu.Set` and `Dcu.Call` cover most member access. Two shapes still need raw
reflection.

Generic methods on an object obtained by reflection — bind the type argument first:

```csharp
var mi = obj.GetType().GetMethods()
    .First(m => m.Name == "GetService" && m.IsGenericMethodDefinition);
var service = mi.MakeGenericMethod(wantedType).Invoke(obj, null);
```

`out` parameters — pass an `object[]` and read the slot back after invoking:

```csharp
var args = new object[] { null };
bool ok = (bool)method.Invoke(null, args);
var result = args[0];
```

A method whose name promises one thing can implement another. Before building on a boolean
accessor, read what it actually returns; a `TryGet...` that ends in `&& someOtherCondition`
reports failure in situations where the thing being fetched exists perfectly well.

## State That Lands On A Later Tick

Several Editor subsystems queue work rather than applying it immediately:

- A window's context initialises after the window opens, not during the call that opened it.
- `SceneView.pivot` and `.size` are consumed on the next repaint, so the camera transform read
  back in the same snippet is still the old one. `LookAt(..., instant:true)` then read on a
  later call.
- Asset import, GPU upload and streaming settle over multiple frames.

Await a bounded readiness condition with `ctx` before reading state that settles on a later tick,
or apply in one call and verify in the next. Prefer the actual Editor state over a fixed delay.
Do not carry an execution context across Domain Reload; use the backend-specific reload workflow.

## Return Shape

For inspection, a `StringBuilder` with one labelled fact per line reads well and diffs well
across calls. `loaded=12 loading=0` stays unambiguous three calls later in a way that two bare
numbers do not. Where a value has an expectation, state it inline — `loaded=0 <- expect 0` makes
a wrong result obvious at a glance instead of something to remember to check.
