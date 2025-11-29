# Modularity Architecture

## Overview

NetPy implements an Odoo-style modular addon system with:
- **Module discovery**: Scan `addons/` for `manifest.json` files
- **Dependency resolution**: Topological sort by `depends` field
- **Model composition**: Multiple interfaces merged into unified schema
- **Method pipelines**: Compiled delegate chains with `super` pattern
- **Cross-assembly compatibility**: Non-generic `IRecordValues` base + generated Values interfaces
- **Type-safe overrides**: Use generated Values interfaces (e.g., `IPartnerSaleExtensionValues`) directly in method signatures

## Directory Structure

```
addons/
├── base/
│   ├── manifest.json           # {"name": "base", "depends": []}
│   ├── Odoo.Base.csproj
│   ├── Models/Partner.cs       # IPartnerBase interface
│   └── Logic/PartnerLogic.cs   # Base implementations
│
└── sale/
    ├── manifest.json           # {"name": "sale", "depends": ["base"]}
    ├── Odoo.Sale.csproj
    ├── Models/PartnerExtension.cs  # IPartnerSaleExtension interface
    └── Logic/PartnerLogic.cs       # Overrides with super
```

## Model Interfaces

**Base module** (`addons/base/Models/Partner.cs`):
```csharp
[OdooModel("res.partner")]
public interface IPartnerBase : IOdooRecord
{
    string Name { get; set; }
    string? Email { get; set; }
}
```

**Sale module** (`addons/sale/Models/PartnerExtension.cs`):
```csharp
[OdooModel("res.partner")]
public interface IPartnerSaleExtension : IOdooRecord
{
    bool IsCustomer { get; set; }
    decimal CreditLimit { get; set; }
}
```

**Generated unified interface** (Source Generator):
```csharp
public interface IPartner : IPartnerBase, IPartnerSaleExtension, IOdooRecord { }
```

## Pipeline Architecture

### IRecordValues Hierarchy

The pipeline uses a two-level interface hierarchy for cross-assembly compatibility:

```csharp
// Base interface for pipeline compatibility (non-generic)
public interface IRecordValues
{
    string ModelName { get; }
    Dictionary<string, object?> ToDictionary();
    IEnumerable<string> GetSetFields();
}

// Generic interface for type safety
public interface IRecordValues<TRecord> : IRecordValues
    where TRecord : IOdooRecord
{ }
```

### Generated Values Interfaces

For each model interface, the Source Generator creates a **Values interface** with `RecordValueField<T>` properties:

```csharp
// Generated: IPartnerBaseValues.g.cs
public interface IPartnerBaseValues : IRecordValues<IPartnerBase>
{
    RecordValueField<string> Name { get; set; }
    RecordValueField<string?> Email { get; set; }
}

// Generated: IPartnerSaleExtensionValues.g.cs
public interface IPartnerSaleExtensionValues : IRecordValues<IPartnerSaleExtension>, IPartnerBaseValues
{
    RecordValueField<bool> IsCustomer { get; set; }
    RecordValueField<decimal> CreditLimit { get; set; }
}
```

This allows type-safe property access with `.IsSet` tracking:
```csharp
if (vals.CreditLimit.IsSet && vals.CreditLimit.Value < 0)
    throw new Exception("Credit limit cannot be negative!");
```

### OdooLogic Attribute

**Base implementation** (uses `IRecordValues` directly):
```csharp
[OdooLogic("res.partner", "write")]
public static void Write_BaseImpl(RecordHandle handle, IRecordValues vals) { }
```

**Override with type-safe Values interface** (recommended pattern):
```csharp
[OdooLogic("res.partner", "write")]
public static void Write_SaleOverride(
    RecordHandle handle,
    IPartnerSaleExtensionValues vals,  // Type-safe Values interface!
    Action<RecordHandle, IRecordValues> super)
{
    // Full IntelliSense - vals.CreditLimit, vals.IsCustomer, etc.
    if (vals.CreditLimit.IsSet && vals.CreditLimit.Value < 0)
        throw new Exception("Credit limit cannot be negative!");
    
    // Log which fields are being modified
    foreach (var field in vals.GetSetFields())
        Console.WriteLine($"  Field being written: {field}");
    
    super(handle, vals);  // Call base
}
```

The source generator automatically wraps the override with a cast:
```csharp
// Generated registration in ModuleRegistrar.g.cs:
builder.RegisterOverride("res.partner", "write", 10,
    (Action<RecordHandle, IRecordValues, Action<RecordHandle, IRecordValues>>)((handle, vals, super) =>
        PartnerLogic.Write_SaleOverride(handle, (IPartnerSaleExtensionValues)vals, super)));
```

### Pipeline Signatures

| Method | Base Signature | Override Signature |
|--------|---------------|-------------------|
| `write` | `(RecordHandle, IRecordValues)` | `+ Action<RecordHandle, IRecordValues> super` |
| `create` | `(IEnvironment, IRecordValues) → IOdooRecord` | `+ Func<IEnvironment, IRecordValues, IOdooRecord> super` |

### Generated Values Classes

The Source Generator creates concrete Values classes implementing ALL visible interfaces:

```csharp
// Generated: ResPartnerValues.g.cs
public sealed class ResPartnerValues :
    IRecordValues<IPartner>,
    IRecordValues<IPartnerBase>,
    IRecordValues<IPartnerSaleExtension>,
    IPartnerBaseValues,
    IPartnerSaleExtensionValues
{
    public RecordValueField<string> Name { get; set; } = new();
    public RecordValueField<string?> Email { get; set; } = new();
    public RecordValueField<bool> IsCustomer { get; set; } = new();
    public RecordValueField<decimal> CreditLimit { get; set; } = new();
    
    public string ModelName => "res.partner";
    
    public Dictionary<string, object?> ToDictionary() { ... }
    public IEnumerable<string> GetSetFields() { ... }
    public static ResPartnerValues FromDictionary(Dictionary<string, object?> dict) { ... }
}
```

### Type-Safe Usage in Overrides

Because Values interfaces extend `IRecordValues<T>` which extends `IRecordValues`, you can:

1. **Use Values interfaces directly in method signatures** - full IntelliSense
2. **Access `RecordValueField<T>.IsSet`** - check if field was explicitly set
3. **Access `RecordValueField<T>.Value`** - get the typed value
4. **Call `vals.ToDictionary()`** - for interop with dynamic code
5. **Pass vals to super** - Values class implements both interfaces

```csharp
// Example: Validate before calling super
if (vals.Email.IsSet && !vals.Email.Value?.Contains("@"))
    throw new ValidationException("Invalid email format");

// Can pass to super directly - Values class implements IRecordValues
super(handle, vals);
```

## Generated Components

The Source Generator produces:

| File | Purpose |
|------|---------|
| `IPartner.g.cs` | Unified model interface |
| `ResPartnerValues.g.cs` | Multi-interface Values class |
| `ResPartnerValuesHandler.g.cs` | `IRecordValuesHandler` for field writes |
| `ResPartnerPipelines.g.cs` | Pipeline compilation (write/create) |
| `ModuleRegistrar.g.cs` | `IModuleRegistrar` impl for registration |

## Runtime Flow

```
1. ModuleLoader.LoadModules()
   ├── Discover addons/ directories
   ├── Parse manifest.json files
   ├── Topological sort by dependencies
   └── Load DLLs via AssemblyLoadContext

2. OdooFramework.RegisterModule(assembly)
   ├── Find IModuleRegistrar implementation
   └── Call registrar.Register(pipelineBuilder)

3. Pipeline Compilation
   ├── Collect all [OdooLogic] methods
   ├── Sort by module dependency order
   └── Compile delegate chain

4. Runtime Execution
   └── partner.Write(vals) → compiled pipeline
```

## Key Design Decisions

**Why `IRecordValues` (non-generic) in pipeline signatures?**
Pipeline composition uses Expression trees requiring exact type matches. Using a generic `IRecordValues<T>` directly would cause:
```
ParameterExpression of type 'IRecordValues<IPartnerSaleExtension>' cannot be used for delegate parameter of type 'IRecordValues'
```
The solution: pipeline uses `IRecordValues` (non-generic), but methods can declare Values interface parameters (e.g., `IPartnerSaleExtensionValues`) and the generator wraps with a cast.

**Why generated Values interfaces?**
Values interfaces like `IPartnerSaleExtensionValues` extend `IRecordValues<T>` which extends `IRecordValues`. This means:
- Methods can use the specific Values interface for full IntelliSense
- The generator detects Values interface parameters and generates wrapper lambdas
- Values classes implement ALL interfaces so runtime casts work
- No manual casting required in business logic

**Why `RecordValueField<T>` instead of plain values?**
`RecordValueField<T>` tracks whether a field was explicitly set via `.IsSet` property. This enables:
- Partial updates: only write fields that were explicitly set
- Validation: check if required fields are present
- Dirty tracking: know which fields changed

**Why `RecordHandle` instead of `IPartner`?**
The entity (`IPartner`) is accessed via columnar cache using `RecordHandle`. The handle provides:
- Record ID
- Model token (for cache lookup)
- Environment reference

**How does the generator detect Values interface parameters?**
At source generation time, generated types (like `IPartnerSaleExtensionValues`) may appear as error types since they're created by the same generator. The detection logic checks:
1. Parameter type is `INamedTypeSymbol`
2. TypeKind is `Interface` OR `Error` (for generated types)
3. Type name ends with "Values" and is not "IRecordValues"
4. Type is not generic (distinguishes from `IRecordValues<T>`)

## Performance

| Approach | Latency |
|----------|---------|
| Compiled delegate chain | ~3-5ns per layer |
| Virtual method dispatch | ~1-2ns |
| Reflection/dynamic | ~200+ns |

Pipeline overhead is minimal because delegates are compiled once at startup.

---

## Future: Odoo Patterns Not Yet Implemented

### Python Module Integration

```json
{
    "name": "sale",
    "depends": ["base"],
    "assembly_path": "bin/Odoo.Sale.dll",
    "python_path": "python/"
}
```

```python
# addons/sale/python/partner_extension.py
class PartnerExtension:
    @staticmethod
    def compute_credit_score(env, partner_id):
        return 750
```

### Additional CRUD Pipelines

| Method | Pattern | Status |
|--------|---------|--------|
| `write` | `(RecordHandle, IRecordValues)` | ✅ Implemented |
| `create` | `(IRecordValues) → RecordHandle` | ✅ Implemented |
| `unlink` | `(RecordHandle)` | ❌ Not yet |
| `read` | `(RecordHandle, string[]) → dict` | ❌ Not yet |
| `search` | `(Domain) → RecordHandle[]` | ❌ Not yet |

### Onchange Methods

```csharp
[OdooOnchange("res.partner", "country_id")]
public static void OnchangeCountry(RecordHandle handle, IRecordValues vals)
{
    // Auto-fill state based on country
}
```

### Constraint Methods

```csharp
[OdooConstraint("res.partner", "check_email")]
public static void CheckEmail(RecordHandle handle)
{
    var email = handle.Get<string>("email");
    if (!email.Contains("@"))
        throw new ValidationException("Invalid email");
}
```

### Security Rules (ir.rule equivalent)

```csharp
[OdooRule("res.partner", "own_partners_only")]
public static Domain GetDomainFilter(OdooEnvironment env)
{
    return new Domain("create_uid", "=", env.UserId);
}
```

### Multi-Company Support

```csharp
[OdooModel("res.partner")]
public interface IPartnerMultiCompany : IOdooRecord
{
    [OdooField("company_id")]
    int? CompanyId { get; set; }
}
```

### Delegated Inheritance (`_inherits`)

```csharp
// Odoo: _inherits = {'res.partner': 'partner_id'}
[OdooModel("res.users")]
[OdooDelegates("res.partner", "partner_id")]
public interface IUserBase : IOdooRecord
{
    int PartnerId { get; set; }
    string Login { get; set; }
}
```

### Stored Computed Fields

```csharp
[OdooField("display_name")]
[OdooComputed(nameof(ComputeDisplayName), Store = true)]
[OdooDepends("name", "is_company", "parent_id")]
string DisplayName { get; }
```

### Database Operations

- **Schema migration**: Auto-generate ALTER TABLE for new fields
- **Module-specific tables**: Extension tables like Odoo's `res_partner_sale_ext`
- **Batch operations**: Efficient multi-record write using columnar cache