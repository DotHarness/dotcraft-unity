# Snippet Failure Modes

`references/api.md` covers the `Dcu.*` helpers. This file covers the ways a snippet fails in
practice: it times out, its effect is not visible yet, or it fails to compile. These are ordered by how often they bite.

## Long Operations Outlive The Call

Long synchronous work blocks Unity's main thread and can exceed the caller's deadline. A
timeout or disconnect can leave work running or already completed, so inspect state before
choosing the next action. Never replay an uncertain mutation automatically.

For script compilation or Domain Reload, use the workflow in
`references/compilation-and-reload.md`. Attach recovers its bridge without resuming the snippet.

For bounded asynchronous work that cannot reload assemblies, await the next editor frame or a
readiness condition. These awaits resume on Unity's main thread:

```csharp
await ctx.WaitFrames(2);
cancellationToken.ThrowIfCancellationRequested();
return EditorApplication.isUpdating;
```

A synchronous loop must call `ctx.ThrowIfCancellationRequested()` explicitly. Stopping the caller's
wait is not proof that Unity stopped; inspect state after an uncertain transport outcome and do not
replay automatically. Work that must survive Domain Reload needs an external workflow.

Project work that finishes after the snippet returns, such as loading a scene, is best awaited in
the same call with a bounded `ctx.WaitUntil`. If it must outlive the call, return an operation ID
and a status probe with one explicit terminal field, such as `settled: true`, and poll on that
field. A poll that waits for a guessed state name keeps running after the work has finished.

## Logs Are Not Returned

A call does not return the logs its snippet emits. When the logs *are* the point, read them
deliberately — see `references/console-reading.md`.

## Reading Compile Failures

A compile failure returns `errorCode: "CompilationFailed"` with a `diagnostics` list: each entry
carries the Roslyn id, message, line, and column. Line numbers refer to the snippet as written
(after the leading `using` directives). Read the diagnostics before changing anything; two causes
account for most of them.

**Ambiguous type names.** `System.Object` and `UnityEngine.Object` are both in scope, so bare
`Object` fails. Write `UnityEngine.Object.DestroyImmediate(...)`. The same applies to `Random`
and `Debug`.

**Project types without their namespace.** Snippets import only System, Unity, Newtonsoft and
DotCraft namespaces, so qualify project types fully; short names such as `Singleton<T>` often
match several types.

**A type or overload absent on this Editor.** Internal API carries no compatibility guarantee and
differs between Unity versions. A direct reference to a missing method fails at compile time and
takes the whole snippet with it, including the diagnostics you wrote to find out what happened.

Use `Dcu.Type(name, throwIfMissing:false)` and branch on `null` rather than letting a lookup throw
mid-diagnosis. When a call fails to compile, enumerate the type instead of guessing:

```csharp
return Dcu.Members("UnityEditor.SceneManagement.EditorSceneManager", "Save", true, 40);
```

An empty list is a definite answer: the member is absent on this Editor, not being called wrongly.

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
Do not carry an execution context across Domain Reload. Use the reload workflow.

## Return Shape

For inspection, a `StringBuilder` with one labelled fact per line reads well and diffs well
across calls. `loaded=12 loading=0` stays unambiguous three calls later in a way that two bare
numbers do not. Where a value has an expectation, state it inline — `loaded=0 <- expect 0` makes
a wrong result obvious at a glance instead of something to remember to check.
