# Odoo-Style ORM for C#

A high-performance Object-Relational Mapping system for C# inspired by Odoo's ORM architecture, featuring multiple inheritance through mixins, type-safe record access, and Python integration.

## 🎯 Overview

This ORM implementation brings Odoo's powerful model composition patterns to C#, solving the multiple inheritance problem through Source Generators and providing seamless Python interoperability.

### Key Features

- ✅ **Multiple Inheritance via Mixins** - Compose models from multiple interfaces
- ✅ **Source Generator** - Zero-boilerplate code generation at compile time
- ✅ **Type-Safe Record Access** - Strongly-typed property access with IntelliSense support
- ✅ **High Performance** - Struct-based records with minimal memory overhead
- ✅ **Python Integration** - Extend models with Python code
- ✅ **Fluent API** - LINQ-style operations on recordsets
- ✅ **Cache System** - Efficient data caching with dirty tracking

## 📁 Project Structure

```
netpy/
├── Core/                          # Framework core
│   ├── OdooFramework.cs          # Base abstractions, attributes, interfaces
│   ├── SimpleValueCache.cs       # In-memory cache implementation
│   └── OdooEnvironment.cs        # Environment and context management
│
├── Models/                        # Model definitions
│   └── Definitions.cs            # Interface-based model definitions
│
├── Generated/                     # Generated code (sample)
│   └── SampleGenerated.cs        # Example of generated record structs
│
├── Odoo.SourceGenerator/         # Code generator
│   ├── OdooModelGenerator.cs     # Roslyn source generator
│   └── Odoo.SourceGenerator.csproj
│
├── Python/                        # Python integration
│   ├── PythonModuleLoader.cs     # Python module management
│   └── OdooPythonBridge.cs       # ORM-Python bridge
│
├── Scripts/                       # Python modules
│   ├── sample.py                 # Original Python.NET demo
│   └── odoo_module_sample.py     # Odoo ORM Python extensions
│
├── Examples/                      # Demo applications
│   ├── BasicUsageDemo.cs         # Basic ORM usage
│   └── PythonIntegrationDemo.cs  # Python integration examples
│
└── Program.cs                     # Main application entry point
```

## 🚀 Quick Start

### 1. Define Models as Interfaces

Models are defined as interfaces with attributes specifying Odoo field mappings:

```csharp
using Odoo.Core;

// Mixin for address-related fields
public interface IAddress : IOdooRecord
{
    [OdooField("street")]
    string Street { get; set; }

    [OdooField("city")]
    string City { get; set; }
}

// Concrete model with multiple inheritance
[OdooModel("res.partner")]
public interface IPartner : IMailThread, IAddress, IContactInfo
{
    [OdooField("name")]
    string Name { get; set; }

    [OdooField("email")]
    string Email { get; set; }

    [OdooField("is_company")]
    bool IsCompany { get; set; }
}
```

### 2. The Source Generator Creates Implementation

The Source Generator automatically creates record structs implementing all interfaces:

```csharp
// Generated automatically
public readonly struct PartnerRecord : IPartner
{
    private readonly IEnvironment _env;
    private readonly int _id;
    
    public string Name
    {
        get => _env.Cache.GetValue<string>("res.partner", _id, "name");
        set => _env.Cache.SetValue("res.partner", _id, "name", value);
    }
    
    // ... all other properties from all inherited interfaces
}
```

### 3. Use the ORM

```csharp
using Odoo.Core;
using Odoo.Models;
using Odoo.Generated;

// Create environment
var env = new OdooEnvironment(userId: 1);

// Access single record
var partner = env.Partner(10);
partner.Name = "Odoo S.A.";
partner.City = "Brussels";

// Work with multiple records
var partners = env.Partners(new[] { 10, 11, 12 });
foreach (var p in partners)
{
    Console.WriteLine($"{p.Name} - {p.Email}");
}

// Filter records
var companies = partners.Where(p => p.IsCompany);
```

## 🏗️ Architecture

### Multiple Inheritance Solution

C# doesn't support multiple inheritance for classes, but we solve this elegantly:

1. **Define with Interfaces**: `IPartner : IMailThread, IAddress`
2. **Generate with Structs**: The generator flattens the hierarchy into a single struct
3. **Type-Safe Access**: Use interface types for variables, struct types for implementation

### Data Flow

```
┌─────────────────┐
│  User Code      │
│  (Interface)    │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Generated Struct│ ──────► Property Access
│  (PartnerRecord)│
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  IValueCache    │ ──────► Data Storage
│  (SimpleCache)  │
└─────────────────┘
```

## 🐍 Python Integration

### Loading Python Modules

```csharp
var moduleLoader = new PythonModuleLoader("./Scripts");
var module = moduleLoader.LoadModule("odoo_module_sample");
```

### Calling Python Functions

```csharp
var pythonBridge = new OdooPythonBridge(env, moduleLoader);

// Call Python function with ORM context
var result = pythonBridge.ExecuteModuleMethod<string>(
    "odoo_module_sample",
    "compute_partner_display_name",
    partnerId);
```

### Python-Side Extensions

```python
# Scripts/odoo_module_sample.py
def compute_partner_display_name(env, partner_id):
    """Compute display name for a partner"""
    return f"Partner #{partner_id}"

class PartnerExtension:
    @staticmethod
    def send_welcome_email(env, partner_id):
        # Custom business logic in Python
        return True
```

## 🔧 Core Components

### IEnvironment

The execution context for all ORM operations:

- **UserId**: Current user ID
- **Cache**: Data cache instance
- **GetModel<T>()**: Factory for recordsets
- **CreateRecordSet<T>(ids)**: Create recordset with specific IDs

### IValueCache

Data storage abstraction:

- **GetValue<T>(model, id, field)**: Retrieve cached value
- **SetValue<T>(model, id, field, value)**: Store value
- **MarkDirty(model, id, field)**: Track modifications
- **GetDirtyFields(model, id)**: Get modified fields

### RecordSet<T>

Collection of records:

- **Count**: Number of records
- **this[index]**: Access by index
- **Where(predicate)**: Filter records
- **Select(selector)**: Map to another type

### IOdooRecord

Base interface for all models:

- **Id**: Database ID
- **Env**: Environment reference

## 📝 Defining Models

### Basic Model

```csharp
[OdooModel("res.partner")]
public interface IPartner : IOdooRecord
{
    [OdooField("name")]
    string Name { get; set; }
}
```

### With Mixins

```csharp
// Mixin definition
public interface IMailThread : IOdooRecord
{
    [OdooField("message_ids")]
    int[] MessageIds { get; }
}

// Use mixin
[OdooModel("res.partner")]
public interface IPartner : IMailThread
{
    [OdooField("name")]
    string Name { get; set; }
}
```

### Field Types

- **Simple**: `string`, `int`, `decimal`, `bool`, `DateTime`
- **Nullable**: `int?`, `string?`, `decimal?`
- **Relations**: `int` (Many2one), `int[]` (One2many, Many2many)

## 🎮 Usage Examples

### Creating Records

```csharp
var cache = new SimpleValueCache();
cache.BulkLoad("res.partner", new()
{
    [10] = new()
    {
        ["name"] = "Odoo S.A.",
        ["email"] = "info@odoo.com",
        ["is_company"] = true
    }
});
```

### Reading Records

```csharp
var partner = env.Partner(10);
Console.WriteLine($"Name: {partner.Name}");
Console.WriteLine($"Email: {partner.Email}");
```

### Updating Records

```csharp
partner.Name = "New Name";
partner.City = "Brussels";

// Check what changed
var dirtyFields = env.Cache.GetDirtyFields("res.partner", 10);
```

### Working with RecordSets

```csharp
var partners = env.Partners(new[] { 10, 11, 12 });

// Filter
var companies = partners.Where(p => p.IsCompany);

// Map
var names = partners.Select(p => p.Name);

// Iterate
foreach (var p in partners)
{
    Console.WriteLine(p.Name);
}
```

## 🔌 Source Generator

### How It Works

The Source Generator runs at compile time and:

1. Scans for interfaces with `[OdooModel]` attribute
2. Analyzes the entire interface hierarchy (including base interfaces)
3. Generates a readonly struct implementing all interfaces
4. Creates environment extension methods for convenient access

### Generated Code Features

- ✅ Readonly structs (high performance)
- ✅ Property implementations using cache
- ✅ Automatic dirty tracking on setters
- ✅ Extension methods for environment
- ✅ Full IntelliSense support

### Enabling Code Generation

Uncomment in `netpy.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="Odoo.SourceGenerator\Odoo.SourceGenerator.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## 🧪 Running the Demos

### Basic ORM Demo

```bash
dotnet run
# Select option 1
```

Demonstrates:
- Creating environment
- Accessing records
- Modifying data
- Working with recordsets
- Multiple inheritance usage

### Python Integration Demo

```bash
dotnet run
# Select option 2
```

Demonstrates:
- Loading Python modules
- Calling Python functions
- Batch processing
- Workflow execution
- Python extensions

## ⚙️ Configuration

### Python Setup

Update `Program.cs` with your Python path:

```csharp
Runtime.PythonDLL = "/path/to/your/python3";
```

### Cache Implementation

Replace `SimpleValueCache` with your database backend:

```csharp
public class DatabaseCache : IValueCache
{
    // Implement with actual database calls
}
```

## 🎯 Design Principles

1. **Interface-Based**: Models are interfaces for flexibility
2. **Struct Implementation**: Generated records are structs for performance
3. **Immutable Environment**: Environment is readonly after creation
4. **Explicit Caching**: All data goes through the cache layer
5. **Type Safety**: Strong typing throughout with no reflection
6. **Zero Boilerplate**: Source generator handles all implementation

## 🚧 Future Enhancements

- [ ] Async/await support for database operations
- [ ] Transaction management
- [ ] Query builder for complex searches
- [ ] Relationship navigation (e.g., `partner.Parent`)
- [ ] Computed fields with dependency tracking
- [ ] Record validation framework
- [ ] Change tracking and audit log
- [ ] Multi-tenancy support

## 📚 Comparison with Odoo Python ORM

| Feature | Odoo Python | This C# ORM |
|---------|------------|-------------|
| Multiple Inheritance | ✅ Native | ✅ Via mixins + generator |
| Type Safety | ❌ Dynamic | ✅ Strong typing |
| Performance | ⚠️ Interpreted | ✅ Compiled structs |
| Python Extensions | ✅ Native | ✅ Via Python.NET |
| RecordSets | ✅ Yes | ✅ Yes |
| Domain Filters | ✅ Yes | 🚧 Planned |
| ORM Methods | ✅ search, create, write | 🚧 Planned |

## 📄 License

MIT License - See LICENSE file for details

## 🤝 Contributing

Contributions are welcome! Areas of interest:

- Database backend implementations
- Additional model definitions
- Python module extensions
- Performance optimizations
- Documentation improvements

## 📞 Support

For questions and issues, please open a GitHub issue.

---

Built with ❤️ using C#, Roslyn, and Python.NET