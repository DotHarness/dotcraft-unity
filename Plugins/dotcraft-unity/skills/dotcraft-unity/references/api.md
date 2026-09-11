# Unity API helpers

Use these helpers inside `unity_execute_csharp` when they replace long boilerplate. Gateway snippets import `DotCraft.Editor`; Attach snippets import `DotCraft.Unity`. Both expose the same `Dcu` helpers.

Do not use these helpers for simple Unity calls such as `GameObject.Find`, `AssetDatabase.GetAssetPath`, or one-off property reads.

## Type and component lookup

```csharp
var type = Dcu.Type("UnityEngine.BoxCollider");
var colliders = Dcu.Components(type);
return colliders.Select(c => c.name).Take(20).ToArray();
```

`Dcu.Type(name, throwIfMissing:false)` returns `null` instead of throwing. Short type names must be unique across loaded assemblies.

## Reflection

```csharp
var comp = Dcu.Components("UnityEngine.BoxCollider").FirstOrDefault();
if (comp == null) return "No BoxCollider components found.";

var size = Dcu.Get(comp, "size");
var members = Dcu.Members(comp, "size");
return new {
  size,
  members = members.Select(m => $"{m.Kind} {m.Name}: {m.TypeName}").ToArray()
};
```

Use `Dcu.Get`, `Dcu.Set`, and `Dcu.Call` for fields, properties, and methods on custom or third-party types when direct API access would require repetitive reflection code.
