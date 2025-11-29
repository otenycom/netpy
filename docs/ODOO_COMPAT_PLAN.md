# Odoo Compatibility & Persistence Architecture Plan

This document outlines the architectural design for achieving Odoo module compatibility and implementing a robust persistence layer in the C# Odoo Core clone.

## 1. Hybrid Module Loader (Odoo Compatibility)

**Goal:** Enable the system to load standard Odoo modules (Python-based) alongside C# extensions, allowing for a gradual migration or hybrid deployment.

### Architecture

1.  **Manifest Parsing Strategy**:
    *   Instead of just `manifest.json`, the loader will look for `__manifest__.py`.
    *   Use `Python.NET` to execute the manifest file in a sandboxed scope and extract the dictionary.
    *   Map Odoo manifest keys (`depends`, `data`, `external_dependencies`) to our `ModuleManifest` C# record.

2.  **Directory Structure Support**:
    *   Standard Odoo structure:
        ```text
        /addons/my_module/
        ├── __manifest__.py
        ├── __init__.py
        ├── models/          # Python models
        ├── views/           # XML views (future)
        ├── security/        # CSV/XML access rules
        └── bin/             # C# DLLs (NetPy extension)
        ```

3.  **Hybrid Loading Process**:
    *   **Step 1 (Discovery)**: Scan directories for `__manifest__.py`.
    *   **Step 2 (Python Load)**: Initialize Python engine, add module path to `sys.path`.
    *   **Step 3 (C# Load)**: Check for `bin/*.dll` or `assembly_path` in manifest. Load via `AssemblyLoadContext`.
    *   **Step 4 (Registration)**:
        *   Register C# models via `IModuleRegistrar`.
        *   Register Python models: Iterate loaded Python modules, find classes inheriting from `models.Model`, and register them as "Virtual Models" in the C# `ModelRegistry`.

### Python-to-C# Model Bridge

*   **Virtual Models**: Python models will be represented in C# as `DynamicOdooRecord` implementing `IOdooRecord`.
*   **Field Mapping**:
    *   Odoo `fields.Char` -> C# `FieldType.String`
    *   Odoo `fields.Many2one` -> **TODO**: Implement proper `Many2one` field type in C# (wrapping ID + Relation lookup), rather than just `FieldType.Integer`.
*   **Method Interop**:
    *   Calling a method on a C# record that exists in Python (via `super()` or direct call) will use `OdooPythonBridge` to invoke the Python method.

## 2. Database Persistence Layer

**Goal:** Persist `IColumnarCache` data to PostgreSQL, matching Odoo's database schema structure.

### Architecture

1.  **IDatabaseBackend Interface**:
    ```csharp
    public interface IDatabaseBackend
    {
        // Schema Management
        void CreateTable(string modelName, IEnumerable<FieldSchema> fields);
        void MigrateColumn(string modelName, FieldSchema field);

        // CRUD
        int[] Insert(string modelName, Dictionary<string, object>[] records);
        void Update(string modelName, int[] ids, Dictionary<string, object> values);
        void Delete(string modelName, int[] ids);
        
        // Query
        IEnumerable<Dictionary<string, object>> SearchRead(
            string modelName, 
            SearchDomain domain, 
            string[] fields, 
            int limit, 
            int offset);
    }
    ```

2.  **Integration with ColumnarCache**:
    *   The `ColumnarValueCache` is currently in-memory.
    *   **Read Path**: When `GetColumnSpan` is called and data is missing, trigger a `Fetch` from `IDatabaseBackend`.
        *   *Optimization*: Prefetch all requested columns for the requested IDs in one SQL query.
    *   **Write Path**: `Flush()` currently clears dirty flags. It must now:
        1.  Group dirty records by Model.
        2.  Generate SQL `UPDATE` / `INSERT` statements.
        3.  Execute transaction via `IDatabaseBackend`.

3.  **SQL Generation (PostgreSQL)**:
    *   Use `Npgsql` for driver.
    *   Map C# types to Postgres types (`int` -> `integer`, `string` -> `varchar/text`, `bool` -> `boolean`).
    *   Handle Odoo system columns (`create_uid`, `create_date`, `write_uid`, `write_date`).

## 3. Type-Safe Search (LINQ to Domain)

**Goal:** Allow C# developers to write type-safe queries that translate to Odoo Domains (Polish Notation) or SQL.

### Architecture

1.  **DomainBuilder**:
    ```csharp
    // Usage
    var partners = env["res.partner"].Search(p => 
        p.IsCompany == true && p.Name.Contains("Tech"));
    ```

2.  **Expression Visitor**:
    *   Parse the C# `Expression<Func<T, bool>>`.
    *   Translate binary operations:
        *   `==` -> `("field", "=", value)`
        *   `Contains` -> `("field", "ilike", "%value%")`
        *   `&&` -> `'&'` (Polish notation AND)
        *   `||` -> `'|'` (Polish notation OR)

3.  **Output**:
    *   Produces a `SearchDomain` object (list of tuples) compatible with Odoo's `search()` method.
    *   Can be serialized to JSON for Python interop or converted to SQL `WHERE` clause for direct DB access.

## 4. Core ORM Enhancements

### Many2one Field Architecture
**Goal**: Replace simple Integer IDs with a rich `Many2one<T>` type that supports lazy loading and object navigation, mirroring Odoo's behavior.

*   **Structure**:
    ```csharp
    public struct Many2one<T> where T : IOdooRecord
    {
        public int Id { get; }
        public string DisplayName { get; } // Prefetched name_get
        
        // Lazy loader
        public T Record => _env.GetRecord<T>(Id);
    }
    ```
*   **Behavior**:
    *   Accessing `record.PartnerId` returns a `Many2one<IPartner>`.
    *   `record.PartnerId.Id` gives the integer ID (no DB call).
    *   `record.PartnerId.Record` fetches the full record (lazy).
    *   **Optimization**: When fetching records, optionally prefetch `name_get` results to populate `DisplayName` to avoid "N+1" queries for simple dropdowns.

## Next Steps Implementation Order

1.  **Module Loader**: Implement `__manifest__.py` parsing to unlock Odoo ecosystem compatibility.
2.  **Database Backend**: Implement PostgreSQL connection to make the ORM real.
3.  **Search**: Implement LINQ translator for usability.
4.  **Core Enhancements**: Implement `Many2one<T>` type.