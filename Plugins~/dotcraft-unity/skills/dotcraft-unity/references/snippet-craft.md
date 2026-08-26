# Snippet Failure Modes

`references/api.md` covers the `Dcu.*` helpers. This file covers the ways a snippet fails in
practice: it times out, it buries the answer in stack traces, or it refuses to compile with no
detail. These are ordered by how often they bite.

## Long Operations Time The Call Out

Anything that blocks the main thread for more than a few seconds — capturing a memory snapshot,
writing a large file, loading a lot of scene content, a heavy asset operation — exceeds the tool
timeout. The operation usually completes anyway, so the result is an error message attached to
work that actually succeeded, which is a confusing state to debug from.

Schedule it and return immediately:

```csharp
EditorApplication.delayCall += () => DoTheExpensiveThing();
return "scheduled";
```

Observe completion from outside the snippet: poll the output file until its size stops changing,
or re-query Editor state on a later call. Never sit in a polling loop inside a snippet — the loop
holds the main thread, so the Editor cannot make the progress being waited for.

## Logs Return With Full Stack Traces

Every log emitted **while the snippet runs** is attached to the response, each with a complete
stack trace. One warning is fine. A loop over 120 items that warns once per item returns tens of
thousands of lines and buries the actual return value.

`delayCall` solves this as well: work that runs after the snippet returns sends its logs to the
Console instead of the response. Use it for anything that emits per-item diagnostics, even when
the operation is fast enough that timeout is not a concern.

When the logs *are* the point, read them deliberately — see `references/console-reading.md`.

## Compile Failures Report No Detail

A compile error comes back as a bare failure message. Two causes account for most of them.

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

The pattern that works is **apply in one call, verify in the next**. Verify by reading the thing
that matters rather than assuming a fixed delay, and prefer Editor state over an external
observer — an outside sampler may still be showing the previous value when the change has
already landed, or the reverse.

This is also why chaining "do it and check it" into a single snippet is a false economy: when it
fails there is no way to tell which half failed.

## Return Shape

For inspection, a `StringBuilder` with one labelled fact per line reads well and diffs well
across calls. `loaded=12 loading=0` stays unambiguous three calls later in a way that two bare
numbers do not. Where a value has an expectation, state it inline — `loaded=0 <- expect 0` makes
a wrong result obvious at a glance instead of something to remember to check.
