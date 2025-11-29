# Copilot Instructions for netpy

## Project Overview
- **netpy** is a C# ORM inspired by Odoo's Python ORM, designed for high performance and extensibility.
- Major features: multiple inheritance via mixins, type-safe record access, source generators, Python integration (via pythonnet), and columnar storage for batch performance.
- The codebase is organized by domain: `src/` for core libraries, `addons/` for modular features, `samples/` for demos, and `tests/` for xUnit-based unit tests.

## Architecture & Patterns
- **Core ORM**: Found in `src/Odoo.Core/`. Implements models, fields, caching, and record logic. See `ODOO_ORM_README.md` and `DOD_ARCHITECTURE.md` for design rationale.
- **Addons**: Modular features in `addons/`, each with its own `manifest.json` and C# project. Addons use mixins/interfaces for model composition.
- **Source Generation**: Compile-time code generation is used for model boilerplate. See `Odoo.SourceGenerator` in `src/`.
- **Python Integration**: Python code can extend C# models. Integration is handled via pythonnet; see `Odoo.Python` in `src/` and demo scripts in `samples/Odoo.Demo/Scripts/`.

## Developer Workflows
- **Build all**: `dotnet build netpy.slnx`
- **Run demo**: `dotnet run` in `samples/Odoo.Demo/`
- **Run tests**: `dotnet test tests/Odoo.Tests/Odoo.Tests.csproj`
- **Debug tests**: Use VS Code Test Explorer or launch configs; see `tests/Odoo.Tests/README.md` for details.
- **Addons**: Add new features as separate projects in `addons/`, update `manifest.json`.

## Conventions & Tips
- **Model Definition**: Use `[OdooModel]` and `[OdooField]` attributes for model and field declarations.
- **Mixins**: Compose models from multiple interfaces for multiple inheritance.
- **Columnar Storage**: Use provided helpers for batch operations; see `ColumnarCacheHelper.cs`.
- **Documentation**: Key design docs are in the repo root and `docs/`.
- **Testing**: All tests use xUnit; filter by name/class for targeted runs.

## Integration Points
- **Python**: Extend C# models with Python via pythonnet. See `samples/Odoo.Demo/Scripts/` for examples.
- **Odoo Compatibility**: The ORM is inspired by Odoo but is not a direct port; see `ODOO_ORM_README.md` for mapping details.

## References
- `README.md`, `ODOO_ORM_README.md`, `DOD_ARCHITECTURE.md`, `tests/Odoo.Tests/README.md`, and `docs/` for further details and examples.

---

**Example: Defining a Model**
```csharp
[OdooModel("res.partner")]
public interface IPartner : IMailThread, IAddress
{
    [OdooField("name")]
    string Name { get; set; }
    [OdooField("email")]
    string Email { get; set; }
}
```

**Example: Running All Tests**
```bash
dotnet test tests/Odoo.Tests/Odoo.Tests.csproj
```
