# netpy - Odoo-Style ORM for C#

A high-performance Object-Relational Mapping system for C# inspired by Odoo's ORM architecture, featuring multiple inheritance through mixins, type-safe record access, and seamless Python integration.

## 🎯 What's Inside

This project contains a complete implementation of an Odoo-inspired ORM for C#, demonstrating:

- ✅ **Multiple Inheritance via Mixins** - Compose models from multiple interfaces
- ✅ **Source Generator** - Zero-boilerplate code generation at compile time
- ✅ **Type-Safe Record Access** - Strongly-typed properties with IntelliSense
- ✅ **Python Integration** - Extend models with Python code using pythonnet
- ✅ **High Performance** - Struct-based records with minimal overhead

## 📚 Documentation

- 📖 **[Complete ORM Documentation](ODOO_ORM_README.md)** - Full architecture guide
- 🚀 **[Quick Start](#quick-start)** - Get started in 5 minutes
- 💡 **[Examples](Examples/)** - Working code examples

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

### High Performance

- **Readonly Structs**: Minimal memory allocation
- **Direct Cache Access**: No reflection overhead
- **Compile-Time Generation**: All code generated at build time

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

## 📖 Learn More

- 📘 [Complete Documentation](ODOO_ORM_README.md) - Full architecture guide
- 🎓 [Basic Usage Example](Examples/BasicUsageDemo.cs) - Learn the fundamentals
- 🐍 [Python Integration Example](Examples/PythonIntegrationDemo.cs) - Extend with Python
- ⚙️ [Source Generator](Odoo.SourceGenerator/OdooModelGenerator.cs) - How code generation works

## 🎯 Key Features

### ✅ Implemented

- Core ORM framework with Environment and Cache
- Interface-based model definitions
- Source Generator for struct generation
- RecordSet collections with LINQ support
- Python module loader and bridge
- Multiple inheritance via mixins
- Dirty field tracking
- Comprehensive examples and demos

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