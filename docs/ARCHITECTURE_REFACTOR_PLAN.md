# Odoo.NET Architecture Refactoring Plan

## Problem Analysis

The current implementation faces a fundamental conflict between **Modularity** (dynamic extensions) and **Performance** (struct-based, zero-allocation). Additionally, the requirement for deep extensibility means that **every** interaction (property access, method call) must be interceptable by modules, which the current direct-access approach prevents.

1.  **The "Struct Problem"**: A struct defined in `Odoo.Base` cannot implement interfaces from `Odoo.Sale`, preventing a single record type from "having all interfaces".
2.  **Boxing**: Reliance on `IOdooRecord` forces structs onto the heap.
3.  **Closed Property Access**: Properties currently read/write directly to the cache. This prevents modules from adding validation, computing values on the fly, or triggering side effects (onchange) when fields are accessed.
4.  **Slow Pipelines**: `Delegate.DynamicInvoke` is too slow for high-frequency property access.

## Proposed Solution: The "Universal Handle" & "Everything is a Pipeline" Architecture

We will decouple **Identity** (Handle) from **Behavior** (Pipelines). Every property access and method call will be routed through a compiled, type-safe pipeline.

### 1. The Universal `RecordHandle`
A single, lightweight struct in `Odoo.Core` representing the identity of a record.

```csharp
// src/Odoo.Core/RecordHandle.cs
public readonly struct RecordHandle : IEquatable<RecordHandle>
{
    public readonly IEnvironment Env;
    public readonly int Id;
    public readonly ModelHandle Model; 

    // Zero-cost casting to any interface wrapper
    public T As<T>() where T : struct, IRecordWrapper 
    {
        return new T { Handle = this };
    }
}
```

### 2. Property Pipelines (The "Open" Accessor Pattern)
Properties in wrappers will **not** access the cache directly. Instead, they invoke a generated pipeline. This allows any module to override `get_name` or `set_name`.

**Generated Wrapper (in `Odoo.Base`):**
```csharp
public readonly struct PartnerBaseWrapper : IPartnerBase, IRecordWrapper
{
    public RecordHandle Handle { get; init; }

    public string Name 
    {
        // Properties now delegate to the pipeline extension methods
        get => Handle.Get_Name(); 
        set => Handle.Set_Name(value);
    }
}
```

**Generated Pipeline Entry Points:**
```csharp
public static class PartnerPipelineExtensions
{
    // 1. GETTER PIPELINE
    public static string Get_Name(this RecordHandle handle)
    {
        // Retrieve compiled delegate (Func<RecordHandle, string>) from Env cache
        var pipeline = handle.Env.GetPipeline<Func<RecordHandle, string>>(
            ModelSchema.ResPartner.ModelName, 
            "get_name"
        );
        return pipeline(handle);
    }

    // 2. SETTER PIPELINE
    public static void Set_Name(this RecordHandle handle, string value)
    {
        // Retrieve compiled delegate (Action<RecordHandle, string>)
        var pipeline = handle.Env.GetPipeline<Action<RecordHandle, string>>(
            ModelSchema.ResPartner.ModelName, 
            "set_name"
        );
        pipeline(handle, value);
    }
}
```

### 3. Default Pipeline Implementations
The generator will produce the "Base" implementation for these pipelines, which performs the actual Cache I/O.

```csharp
public static class PartnerDefaults
{
    // Registered as Priority = 0 (Base)
    public static string GetName_Base(RecordHandle handle)
    {
        return handle.Env.Columns.GetValue<string>(...);
    }

    // Registered as Priority = 0 (Base)
    public static void SetName_Base(RecordHandle handle, string value)
    {
        handle.Env.Columns.SetValue(..., value);
        handle.Env.Columns.MarkDirty(...);
    }
}
```

### 4. Overriding Logic (The Goal)
A developer in `Odoo.Sale` can now override the setter validation logic easily:

```csharp
public class PartnerLogic
{
    // Registered as Priority = 10 (Override)
    [OdooOverride("res.partner", "set_name")]
    public static void SetName_Override(RecordHandle handle, string value, Action<RecordHandle, string> super)
    {
        if (string.IsNullOrEmpty(value)) 
            throw new Exception("Name cannot be empty!");
            
        // Call next implementation (eventually reaching Base)
        super(handle, value);
    }
}
```

### 5. Optimized `RecordSet`
The `RecordSet<T>` will also be refactored to be a lightweight wrapper around `int[]` or `RecordHandle[]`, with all its methods (Where, Select, Write) also being pipelines.

```csharp
public static void Write(this RecordSet<IPartner> records, PartnerValues values)
{
    // Pipeline: "write"
    // Allows intercepting bulk updates
}
```

## Implementation Steps

### Phase 1: Core Refactoring
1.  **`RecordHandle`**: Create the universal struct.
2.  **`PipelineRegistry`**: Optimize for generic delegate retrieval (avoiding boxing/casting overhead where possible).
3.  **`IEnvironment`**: Expose fast pipeline lookup.

### Phase 2: Generator Overhaul (Properties)
1.  **Wrappers**: Generate structs that call extension methods.
2.  **Extensions**: Generate `Get_X` and `Set_X` extension methods that invoke pipelines.
3.  **Defaults**: Generate static methods for Cache I/O and register them as pipeline bases.

### Phase 3: Generator Overhaul (Methods)
1.  Ensure all logic methods generate typed pipeline extensions.
2.  Support `RecordSet` pipeline generation.

### Phase 4: Migration & Verification
1.  Update `Odoo.Base` / `Odoo.Sale`.
2.  Create a test case proving that an override in `Odoo.Sale` intercepts a property set in `Odoo.Base`.
