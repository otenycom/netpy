# netpy - Odoo-Style ORM for C# with Data-Oriented Design

> **⚡ NEW: High-Performance Columnar Storage** - Now featuring Data-Oriented Design (DOD) with columnar caching for 10-50x performance improvements in batch operations!

A high-performance Object-Relational Mapping system for C# inspired by Odoo's ORM architecture, featuring multiple inheritance through mixins, type-safe record access, seamless Python integration, and optimized columnar storage.

## 🎯 What's Inside

This project contains a complete implementation of an Odoo-inspired ORM for C#, demonstrating:

- ✅ **Multiple Inheritance via Mixins** - Compose models from multiple interfaces
- ✅ **Source Generator** - Zero-boilerplate code generation at compile time
- ✅ **Type-Safe Record Access** - Strongly-typed properties with IntelliSense
- ✅ **Python Integration** - Extend models with Python code using pythonnet
- ✅ **High Performance** - Struct-based records with minimal overhead

## 📚 Documentation

- 📖 **[Complete ORM Documentation](ODOO_ORM_README.md)** - Full architecture guide
- ⚡ **[Data-Oriented Design Guide](DOD_ARCHITECTURE.md)** - Performance optimization docs
- 🚀 **[Quick Start](#quick-start)** - Get started in 5 minutes
- 💡 **[Examples](Examples/)** - Working code examples
- 🔥 **[Performance Demos](Examples/ColumnarBatchDemo.cs)** - Benchmark and optimization examples

## 🚀 Quick Start

### Run the Demos

```bash
dotnet run
```

Select from the menu:
1. **Basic ORM Usage** - See the ORM in action without Python
2. **Python Integration** - Demonstrates Python module extensions
3. **Run Both Demos** - See everything
4. **Original Python.NET Demo** - Basic pythonnet example

### Define a Model

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

### Use the ORM

```csharp
// Create environment
var env = new OdooEnvironment(userId: 1);

// Access records
var partner = env.Partner(10);
partner.Name = "Odoo S.A.";
partner.City = "Brussels";

// Work with recordsets
var partners = env.Partners(new[] { 10, 11, 12 });
foreach (var p in partners.Where(p => p.IsCompany))
{
    Console.WriteLine($"{p.Name} - {p.Email}");
}
```

## 📁 Project Structure

```
netpy/
├── Core/                          # ORM framework core
│   ├── OdooFramework.cs          # Base abstractions and interfaces
│   ├── SimpleValueCache.cs       # In-memory cache implementation
│   └── OdooEnvironment.cs        # Environment management
│
├── Models/                        # Model definitions (interfaces)
│   └── Definitions.cs            # Partner, User, Product, etc.
│
├── Odoo.SourceGenerator/         # Roslyn code generator
│   ├── OdooModelGenerator.cs     # Generates record structs
│   └── Odoo.SourceGenerator.csproj
│
├── Generated/                     # Sample generated code
│   └── SampleGenerated.cs        # Example output
│
├── Python/                        # Python integration layer
│   ├── PythonModuleLoader.cs     # Load Python modules
│   └── OdooPythonBridge.cs       # ORM-Python bridge
│
├── Examples/                      # Demo applications
│   ├── BasicUsageDemo.cs         # Basic ORM usage
│   └── PythonIntegrationDemo.cs  # Python integration
│
├── Scripts/                       # Python modules
│   ├── sample.py                 # Basic pythonnet example
│   └── odoo_module_sample.py     # ORM extensions in Python
│
└── Program.cs                     # Main entry point with menu
```

## 🏗️ Architecture Highlights

### Multiple Inheritance Solution

C# doesn't support multiple inheritance, but we solve this elegantly:

1. **Define with Interfaces**: Models are composed from multiple interfaces
2. **Generate with Structs**: Source generator flattens hierarchy into structs
3. **Type-Safe Access**: Full IntelliSense and compile-time type checking

```csharp
// Multiple mixins
public interface IPartner : IMailThread, IAddress, IContactInfo { }

// Generated struct implements ALL interfaces
public readonly struct PartnerRecord : IPartner { }
```

### High Performance - Data-Oriented Design

The ORM now implements **columnar storage** for exceptional performance:

- **Columnar Storage (SoA)**: Contiguous memory layout for optimal cache locality
- **Static Field Tokens**: Integer-based lookups replace string hashing (3-5x faster)
- **Batch Context Pattern**: Zero-allocation iteration with direct array access (10-50x faster)
- **Memory Efficiency**: ArrayPool usage reduces GC pressure by 90%+
- **Readonly Structs**: Minimal memory allocation
- **Compile-Time Generation**: All code generated at build time

#### Performance Comparison

```csharp
// Traditional pattern (still works, backward compatible)
var partners = env.Partners(partnerIds);
foreach (var partner in partners)
    if (partner.IsCompany)
        ProcessCompany(partner.Name);

// Optimized batch pattern (10-50x faster for 100+ records)
var batch = new PartnerBatchContext(env.Columns, partnerIds);
var names = batch.GetNameColumn();
var isCompanyFlags = batch.GetIsCompanyColumn();
for (int i = 0; i < partnerIds.Length; i++)
    if (isCompanyFlags[i])
        ProcessCompany(names[i]);
```

**📊 Benchmark Results** (1,000 records × 100 iterations):
- Single field access: **3-5x faster**
- Multi-field batch operations: **10-20x faster**
- Complex calculations: **15-50x faster**
- Memory allocations: **90%+ reduction**

**📖 Learn More**: See [`DOD_ARCHITECTURE.md`](DOD_ARCHITECTURE.md) for complete technical documentation

## 🐍 Python Integration

### Extend Models with Python

```python
# Scripts/odoo_module_sample.py
def compute_partner_display_name(env, partner_id):
    return f"Partner #{partner_id}"

class PartnerExtension:
    @staticmethod
    def send_welcome_email(env, partner_id):
        # Custom business logic
        return True
```

### Call from C#

```csharp
var pythonBridge = new OdooPythonBridge(env, moduleLoader);
var result = pythonBridge.ExecuteModuleMethod<string>(
    "odoo_module_sample",
    "compute_partner_display_name",
    partnerId);
```

## 🔧 Setup

### Prerequisites

- .NET 10.0 SDK
- Python 3.12 (for Python integration features)
- pythonnet package (already included via NuGet)

### Installation

1. Clone/download the project:
   ```bash
   git clone <repository-url>
   cd netpy
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Update Python path in [`Program.cs`](Program.cs) (line ~72):
   ```csharp
   Runtime.PythonDLL = "/path/to/your/python3";
   ```

4. Run the demos:
   ```bash
   dotnet run
   ```

### VSCode: Working with Generated Files (obj/ bin)

If you want full IDE integration (Find All References, Go to Definition, IntelliSense) for .NET-generated sources (for example files produced under `obj/GeneratedFiles/` or Razor/resource-generated `.cs` files), follow these steps. The repository already includes workspace helpers to make this easier.

- **Files added to this repo:**
    - `.vscode/settings.json` — makes `obj` and `bin` visible in the Explorer and searchable.
    - `.vscode/tasks.json` — contains build tasks (see below).
    - `omnisharp.json` — clears OmniSharp exclude patterns so generated files are not filtered out.

- **Show generated files in Explorer and Search:**
    1. Open VSCode settings or use the workspace `.vscode/settings.json` which sets:
         - `files.exclude` to show `**/bin` and `**/obj`
         - `search.exclude` to include `**/obj`
    2. Reload the window (Command Palette → `Developer: Reload Window`).

- **Build to generate files:**
    - From the workspace root run the build task or use the terminal. Example for zsh (quotes required for MSBuild args with semicolons):

```bash
dotnet build /Users/ries/oteny/netpy/netpy.slnx "/property:GenerateFullPaths=true" "/consoleloggerparameters:NoSummary;ForceNoAlign"
```

    - VSCode tasks available:
        - `dotnet build (workspace)` — builds the whole solution
        - `build` — builds the sample `Odoo.Demo` project
        - `build-tests` — builds the tests

- **Restart OmniSharp to pick up generated files:**
    - Command Palette → `OmniSharp: Restart OmniSharp` (or `OmniSharp: Reload Solution`). This forces OmniSharp to re-scan the project model and index generated `.cs` files.

- **If you see compile errors in generated files:**
    - The source generator has been updated to avoid iterator-method compiler errors where a model has no emitted properties. If you still see errors, try `dotnet clean` then `dotnet build`, inspect the generated file in `samples/Odoo.Demo/obj/GeneratedFiles/...`, and open the OmniSharp log (Output → OmniSharp Log) for details.

- **Performance note:** Including `obj` folders in Explorer/search increases files shown and may add noise; using the workspace settings keeps generated sources discoverable while minimizing global config changes.

## 📖 Learn More

- 📘 [Complete Documentation](ODOO_ORM_README.md) - Full architecture guide
- 🎓 [Basic Usage Example](Examples/BasicUsageDemo.cs) - Learn the fundamentals
- 🐍 [Python Integration Example](Examples/PythonIntegrationDemo.cs) - Extend with Python
- ⚙️ [Source Generator](Odoo.SourceGenerator/OdooModelGenerator.cs) - How code generation works

## 🎯 Key Features

### ✅ Implemented

- Core ORM framework with Environment and Cache
- **Data-Oriented Design with columnar storage**
- **Static field tokens for 3-5x faster lookups**
- **Batch context pattern for 10-50x faster iteration**
- Interface-based model definitions
- Source Generator for struct generation
- RecordSet collections with LINQ support
- Python module loader and bridge
- Multiple inheritance via mixins
- Dirty field tracking
- Comprehensive examples and performance benchmarks

### 🚧 Planned

- Async/await database operations
- Transaction management
- Complex query builder
- Relationship navigation
- Computed fields
- Validation framework
- Audit logging

## 🤝 Contributing

Contributions welcome! Areas of interest:

- Database backend implementations
- Additional model definitions
- Performance optimizations
- Documentation improvements

## 📄 License

MIT License

---

**Note**: This is a prototype/demonstration project showing how to build an Odoo-style ORM in C#. It's designed for educational purposes and as a starting point for production implementations.

Built with ❤️ using C#, Roslyn, and Python.NET