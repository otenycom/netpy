# Modularity Architecture

## Overview

NetPy implements an Odoo-style modular addon system with:
- **Module discovery**: Scan `addons/` for `manifest.json` files
- **Dependency resolution**: Topological sort by `depends` field
- **Model composition**: Multiple interfaces merged into unified schema
- **Diamond inheritance**: Multiple addons can independently extend the same base model
- **Method pipelines**: Compiled delegate chains with `super` pattern
- **Cross-assembly compatibility**: Non-generic `IRecordValues` base + generated Values interfaces
- **Type-safe overrides**: Use generated Values interfaces (e.g., `IPartnerSaleExtensionValues`) directly in method signatures
- **Protection-aware computed fields**: Computed field getters check protection status to prevent infinite recursion
- **Project-specific unified interfaces**: Each project gets its own unified interface (e.g., `IResPartner`) with all visible extensions

## Directory Structure

```
addons/
├── base/
│   ├── manifest.json           # {"name": "base", "depends": []}
│   ├── Odoo.Base.csproj
│   ├── Models/Partner.cs       # IPartnerBase interface
│   └── Logic/PartnerLogic.cs   # Base implementations
│
├── sale/
│   ├── manifest.json           # {"name": "sale", "depends": ["base"]}
│   ├── Odoo.Sale.csproj
│   ├── Models/PartnerExtension.cs  # IPartnerSaleExtension interface
│   └── Logic/PartnerLogic.cs       # Overrides with super
│
└── purchase/
    ├── manifest.json           # {"name": "purchase", "depends": ["base"]}
    ├── Odoo.Purchase.csproj
    ├── Models/PartnerExtension.cs  # IPartnerPurchaseExtension interface
    └── Logic/PartnerLogic.cs       # Write and ComputeDisplayName overrides
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

**Purchase module** (`addons/purchase/Models/PartnerExtension.cs`):
```csharp
[OdooModel("res.partner")]
public interface IPartnerPurchaseExtension : IOdooRecord
{
    bool IsSupplier { get; set; }
}
```

**Generated unified interface** (Source Generator):
```csharp
// Generated in each project that references multiple addons
// The interface is project-specific and includes ALL visible extensions
[OdooModel("res.partner")]
public interface IResPartner : IPartnerBase, IPartnerSaleExtension, IPartnerPurchaseExtension { }
```

## Diamond Inheritance Pattern

NetPy supports Odoo's diamond inheritance pattern where multiple modules independently extend the same base model:

```
           ┌──────────────┐
           │ IPartnerBase │  (base addon)
           │  - Name      │
           │  - Email     │
           │  - IsCompany │
           └──────┬───────┘
                  │
       ┌──────────┴──────────┐
       ▼                     ▼
┌──────────────────┐  ┌─────────────────────┐
│ IPartnerSale     │  │ IPartnerPurchase    │
│ Extension        │  │ Extension           │
│  - IsCustomer    │  │  - IsSupplier       │
│  - CreditLimit   │  │                     │
└────────┬─────────┘  └──────────┬──────────┘
         │                       │
         └───────────┬───────────┘
                     ▼
           ┌─────────────────┐
           │  IResPartner    │  (unified interface)
           │  (all fields)   │
           └─────────────────┘
```

Each addon can override computed fields and pipelines:
- **Base**: `_compute_display_name` → "Name" or "Name | Company"
- **Purchase**: Overrides to append "| Supplier" when IsSupplier is true
- **Result**: "Big Corp | Company | Supplier" (all overrides applied via super chain)

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

**Computed field override with super pattern** (Purchase module example):
```csharp
[OdooLogic("res.partner", "_compute_display_name")]
public static void ComputeDisplayName(
    RecordSet<IPartnerPurchaseExtension> self,
    Action<RecordSet<IPartnerPurchaseExtension>> super)
{
    // Call base computation first
    super(self);
    
    // Then modify the result for suppliers
    foreach (var partner in self)
    {
        if (partner.IsSupplier)
        {
            // Safe to read DisplayName - field is protected during compute
            var currentDisplayName = partner.DisplayName ?? partner.Name ?? "";
            partner.DisplayName = currentDisplayName + " | Supplier";
        }
    }
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

## Protection-Aware Computed Field Getters

Computed field getters implement Odoo's protection mechanism to prevent infinite recursion:

```csharp
// Generated getter for DisplayName (computed field)
public string DisplayName
{
    get
    {
        var fieldToken = ModelSchema.ResPartner.DisplayName;
        
        // PROTECTION CHECK: If field is protected (we're inside a compute method),
        // return cached value directly without checking NeedsRecompute.
        // This prevents infinite recursion when overrides read the field after calling super.
        if (Env is OdooEnvironment protectedEnv &&
            protectedEnv.IsProtected(fieldToken, Id))
        {
            return Env.Columns.GetValue<string>(modelToken, Id, fieldToken);
        }
        
        // Normal path: check if recomputation needed
        if (needsCompute)
        {
            ResPartnerPipelines.Compute_DisplayName(recordSet);
        }
        return Env.Columns.GetValue<string>(modelToken, Id, fieldToken);
    }
}
```

The protection mechanism (`env.Protecting()`) wraps compute method execution:
- Records are "protected" during computation
- Protected getters return cached values directly (no recompute check)
- Overrides can safely read the field after calling `super()`

## Generated Components

The Source Generator produces:

| File | Purpose |
|------|---------|
| `IResPartner.g.cs` | Unified model interface with `[OdooModel]` attribute |
| `ResPartner.g.cs` | Unified wrapper class implementing all interfaces |
| `ResPartnerValues.g.cs` | Multi-interface Values class |
| `ResPartnerValuesHandler.g.cs` | `IRecordValuesHandler` for field writes |
| `ResPartnerPipelines.g.cs` | Pipeline compilation (write/create/compute) |
| `ModuleRegistrar.g.cs` | `IModuleRegistrar` impl for registration |
| `OdooEnvironmentExtensions.g.cs` | `env.Create()` extension returning unified interface |

## Environment Setup with OdooEnvironmentBuilder

The recommended way to create a configured `OdooEnvironment` is using the `OdooEnvironmentBuilder`:

```csharp
// Simple usage - auto-discovers all addons from loaded assemblies
var env = new OdooEnvironmentBuilder()
    .WithUserId(1)
    .Build();

// Advanced usage - custom cache
var env = new OdooEnvironmentBuilder()
    .WithUserId(1)
    .WithCache(new CustomColumnarCache())
    .Build();
```

The builder automatically:
1. Discovers all assemblies referencing `Odoo.Core`
2. Finds `IModuleRegistrar` implementations
3. Scans for `[OdooModel]` interfaces
4. Registers pipelines in dependency order
5. Compiles delegate chains
6. Returns a fully configured environment

## Static Compilation Architecture

NetPy uses **static compilation** instead of Odoo's dynamic module loading:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        COMPILE TIME                                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Odoo.Base.csproj ──────────────────────────────────────────────┐   │
│       │                                                          │   │
│       ├── IPartnerBase interface                                 │   │
│       └── [OdooLogic] methods                                    │   │
│                                                                  │   │
│  Odoo.Sale.csproj ─────────────────────────────────┐             │   │
│       │  references Odoo.Base                       │             │   │
│       ├── IPartnerSaleExtension interface           │             │   │
│       └── [OdooLogic] overrides                     │             │   │
│                                                     ▼             │   │
│  Odoo.Purchase.csproj ──────────────────────────────┼─────────────┤   │
│       │  references Odoo.Base                       │             │   │
│       ├── IPartnerPurchaseExtension interface       │             │   │
│       └── [OdooLogic] overrides                     │             │   │
│                                                     │             │   │
│                                                     ▼             ▼   │
│  App.csproj ◄───────────────────────────────────────┴─────────────┘   │
│       │  references all addons                                        │
│       │                                                               │
│       └── Source Generator produces:                                  │
│           ├── IResPartner (unified interface)                         │
│           ├── ResPartner (wrapper class)                              │
│           ├── ResPartnerValues                                        │
│           └── ModuleRegistrar                                         │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                        RUNTIME                                       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  OdooEnvironmentBuilder.Build()                                      │
│       │                                                              │
│       ├── Discover addon assemblies (from loaded assemblies)         │
│       ├── Find IModuleRegistrar implementations                      │
│       ├── Scan [OdooModel] interfaces                                │
│       ├── Register pipelines in dependency order                     │
│       ├── Compile delegate chains                                    │
│       └── Return OdooEnvironment                                     │
│                                                                      │
│  Application executes with fully typed, compiled code                │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

### Why Static Compilation?

| Aspect | Static Compilation | Dynamic Loading |
|--------|-------------------|-----------------|
| **Type Safety** | Full IntelliSense, compile-time errors | Runtime reflection, no IntelliSense |
| **Performance** | Compiled delegates (~3-5ns) | Reflection dispatch (~200ns) |
| **Source Generators** | Full support - all types visible | Limited - types not visible at compile time |
| **AOT/Trimming** | Fully compatible | Requires special handling |
| **Diamond Inheritance** | Unified interfaces generated | Manual casting required |

### Sidecar Project Concept

When new addons need to be installed or removed, a **sidecar tool** handles the process:

```
┌─────────────────────────────────────────────────────────────────────┐
│                     ADDON INSTALLATION FLOW                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  1. User requests addon installation                                 │
│       │                                                              │
│       ▼                                                              │
│  2. Sidecar tool updates solution                                    │
│       ├── Add addon project to .slnx                                 │
│       ├── Add project reference to App.csproj                        │
│       └── Update manifest.json                                       │
│       │                                                              │
│       ▼                                                              │
│  3. Sidecar triggers recompilation                                   │
│       ├── dotnet build App.csproj                                    │
│       ├── Source generators regenerate unified interfaces            │
│       └── All pipelines recompiled with new addon                    │
│       │                                                              │
│       ▼                                                              │
│  4. Sidecar restarts application                                     │
│       └── New assembly loaded with all addons                        │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

This approach ensures:
- Source generators see all addon interfaces at compile time
- Unified interfaces include all visible extensions
- No runtime assembly loading complexity
- Full type safety and IntelliSense

The sidecar project will be implemented separately.

## Runtime Flow

```
1. OdooEnvironmentBuilder.Build()
   ├── Discover addon assemblies (from AppDomain.GetAssemblies)
   ├── Scan for [OdooModel] interfaces
   ├── Find IModuleRegistrar implementations
   ├── Sort by assembly dependency order
   └── Build ModelRegistry and PipelineRegistry

2. For each IModuleRegistrar (in dependency order):
   ├── registrar.RegisterPipelines(pipelineRegistry)
   └── Last registrar: registrar.RegisterFactories(modelRegistry)

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

**Why is the unified interface (`IResPartner`) project-specific?**
Each project that references multiple addons gets its own unified interface containing ALL visible extensions:
- Test project references Base + Sale + Purchase → `IResPartner : IPartnerBase, IPartnerSaleExtension, IPartnerPurchaseExtension`
- Sale addon only references Base → `IResPartner : IPartnerBase, IPartnerSaleExtension`
- This enables `env.Create()` to return the most specific interface for that project

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