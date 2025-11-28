# Unified Wrapper Architecture & Identity Map

## 1. Objective
Refactor the Odoo C# ORM to support a **Unified Wrapper** model. Instead of having separate, disjoint struct wrappers for each module, the system will generate a **Unified Class** (e.g., `Partner`) in *every* project that implements **all** interfaces visible to that project.

## 2. Key Architectural Changes

### A. From Structs to Classes
*   **Current**: `readonly struct` wrappers. Low allocation, but no reference identity. Casting creates new copies.
*   **New**: `public readonly class` wrappers.
    *   **Benefit**: Reference identity (`partnerA == partnerB`), inheritance support, and polymorphism.
    *   **Cost**: Heap allocation. Mitigated by Identity Map.

### B. The Identity Map
To manage the lifecycle of class-based records and ensure consistency, `OdooEnvironment` will implement an **Identity Map**.

*   **Mechanism**: A `Dictionary<(int ModelToken, int Id), IOdooRecord>` stores active record instances.
*   **Behavior**:
    *   When requesting a record (e.g., `env.GetRecord<IPartnerBase>(1)`), check the map.
    *   **Hit**: Return the existing instance.
    *   **Miss**: Create a new instance of the **Unified Wrapper** (using the registered factory), cache it, and return it.
*   **Scope**: Per `OdooEnvironment` (Request/Transaction scope).

### C. Cumulative Source Generation (The "Snowball" Effect)
The Source Generator will run in **every** project (Libraries and Applications) and generate wrappers based on the "visible world" of that project.

#### Strategy:
1.  **Scan**: The generator scans the current compilation AND all referenced assemblies for interfaces with `[OdooModel]`.
2.  **Group**: It groups these interfaces by their Model Name (e.g., "res.partner").
3.  **Generate**: It generates a `Partner` class in the *current project's* generated namespace.
    *   This class implements **all** interfaces found for "res.partner" that are visible to this project.

#### Example Scenario:

*   **Project: Odoo.Base**
    *   Visible: `IPartnerBase`
    *   Generates: `Odoo.Base.Generated.Partner : IPartnerBase`

*   **Project: Odoo.Sale** (References Odoo.Base)
    *   Visible: `IPartnerBase` (from ref), `IPartnerSaleExtension` (local)
    *   Generates: `Odoo.Sale.Generated.Partner : IPartnerBase, IPartnerSaleExtension`
    *   *Note*: This wrapper supersedes the Base wrapper within the context of the Sale module tests.

*   **Project: Odoo.App** (References Odoo.Sale and Odoo.Stock)
    *   Visible: `IPartnerBase`, `IPartnerSaleExtension`, `IPartnerStockExtension`
    *   Generates: `Odoo.App.Generated.Partner : IPartnerBase, IPartnerSaleExtension, IPartnerStockExtension`

### D. Factory Registration & Overrides
Each project registers its generated wrappers in its `ModuleRegistrar`.

*   **Mechanism**: `ModuleRegistrar.RegisterFactories` will register the factory for "res.partner".
*   **Resolution**: When `OdooEnvironment` loads, it builds the registry.
    *   Since `Odoo.App` depends on `Odoo.Sale`, the App's registrar runs *last* (or explicitly overrides).
    *   The `ModelRegistry` will hold the factory from the "most downstream" project (the App), ensuring that `env.Create(...)` returns the fully unified `Odoo.App.Generated.Partner`.

## 3. Usage Example

```csharp
// In Odoo.App
var partner = env.Create(new PartnerValues { Name = "Test" }); 

// 1. Polymorphism: Pass to Base method
ProcessBase(partner); // Accepts IPartnerBase

// 2. Polymorphism: Pass to Sale method
ProcessSale(partner); // Accepts IPartnerSaleExtension

// 3. Identity Check
var p1 = env.GetRecord<IPartnerBase>(1);
var p2 = env.GetRecord<IPartnerSaleExtension>(1);

// Reference equality holds true!
Assert.True(ReferenceEquals(p1, p2)); 
```

## 4. Implementation Plan

1.  **Refactor `OdooEnvironment`**: Add `IdentityMap` dictionary and logic.
2.  **Update Source Generator**:
    *   Modify `OdooModelGenerator` to scan referenced assemblies for `[OdooModel]` interfaces.
    *   Change generation output from `struct` to `class`.
    *   Implement "Cumulative Interface Implementation" logic.
3.  **Update `ModuleRegistrar`**: Ensure factories are registered with a strategy that favors the "most derived" wrapper.
