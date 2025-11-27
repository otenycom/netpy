# Pythonic Record Creation API

## The Problem

```csharp
// BEFORE: Confusing for Python developers
env.Columns.BulkLoad(modelSchema.Token, new FieldHandle(1), 
    new Dictionary<int, string> { { 1, "Test Partner" } });
```

## The Solution

```csharp
// AFTER: Pythonic style
var partner = env["res.partner"].Create(new { name = "Test Partner" });

// Or with generated type safety
var partner = env.Create(new PartnerValues { Name = "Test Partner" });
```

---

## Implementation

### New Files

| File | Purpose |
|------|---------|
| [`IdGenerator.cs`](../src/Odoo.Core/IdGenerator.cs) | Thread-safe ID generation per model |
| [`ModelProxy.cs`](../src/Odoo.Core/ModelProxy.cs) | `env["model"]` indexer + dynamic Create |
| [`FieldNameResolver.cs`](../src/Odoo.Core/Modules/FieldNameResolver.cs) | Field name → token lookup |

### Modified Files

| File | Changes |
|------|---------|
| [`OdooEnvironment.cs`](../src/Odoo.Core/OdooEnvironment.cs) | Added `IdGenerator`, `this[string]` indexer |
| [`OdooModelGenerator.cs`](../src/Odoo.SourceGenerator/OdooModelGenerator.cs) | Generates `*Values` structs, `Create()` methods, stable hash tokens |
| [`RegistryBuilder.cs`](../src/Odoo.Core/Modules/RegistryBuilder.cs) | Uses same stable hash algorithm |

---

## Generated Code

From `IPartnerBase`:

```csharp
// PartnerBaseValues.g.cs
public struct PartnerBaseValues
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public bool? IsCompany { get; init; }
}

// OdooEnvironmentExtensions.g.cs
public static IPartnerBase Create(this IEnvironment env, PartnerBaseValues values)
{
    var newId = env.IdGenerator.NextId("res.partner");
    if (values.Name is not null)
        env.Columns.SetValue(ModelSchema.PartnerBase.ModelToken, newId, 
                            ModelSchema.PartnerBase.Name, values.Name);
    // ...
    return new PartnerBaseRecord(env, newId);
}
```

---

## Key Fix: Cross-Assembly Token Collision

**Problem**: Each assembly's source generator assigned sequential tokens (1, 2, 3...) causing collisions when merging modules.

**Solution**: Both generator and runtime use deterministic hash codes:

```csharp
private static int GetStableHashCode(string str)
{
    unchecked
    {
        int hash = 23;
        foreach (char c in str) hash = hash * 31 + c;
        return hash;
    }
}

// Example: "res.partner.name" → Field(2129067158)
```

---

## Usage

```csharp
// Dynamic (for runtime-loaded modules)
var partner = env["res.partner"].Create(new { 
    name = "Test Partner",
    email = "test@example.com" 
});

// Type-safe (for compile-time known models)
var partner = env.Create(new PartnerBaseValues { 
    Name = "Test Partner",
    Email = "test@example.com" 
});

// Access immediately
Console.WriteLine(partner.Name);  // "Test Partner"
partner.Email = "updated@test.com";
```

---

## Status: ✅ Implemented

Demo output:
```
[Base] Partner Test Partner verified.
✓ Pipeline executed successfully!