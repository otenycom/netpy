# Getting Started with the Odoo ORM

This guide will help you get up and running with the Odoo-style ORM for C#.

## 5-Minute Quick Start

### 1. Run the Basic Demo

```bash
dotnet run
```

When prompted, select option `1` for Basic ORM Usage.

This will demonstrate:
- Creating an environment
- Accessing single records
- Modifying data
- Working with recordsets
- Using multiple inheritance (mixins)

### 2. Explore the Code

The demo uses these key files:
- [`Examples/BasicUsageDemo.cs`](Examples/BasicUsageDemo.cs) - The demo code
- [`Models/Definitions.cs`](Models/Definitions.cs) - Model definitions
- [`Generated/SampleGenerated.cs`](Generated/SampleGenerated.cs) - Generated implementation

## Understanding the Core Concepts

### 1. Models as Interfaces

Models are defined as interfaces, not classes:

```csharp
[OdooModel("res.partner")]
public interface IPartner : IOdooRecord
{
    [OdooField("name")]
    string Name { get; set; }
}
```

### 2. Multiple Inheritance via Mixins

Compose complex models from simpler interfaces:

```csharp
// Define mixins
public interface IAddress : IOdooRecord
{
    [OdooField("street")] string Street { get; set; }
    [OdooField("city")] string City { get; set; }
}

public interface IContactInfo : IOdooRecord
{
    [OdooField("email")] string Email { get; set; }
    [OdooField("phone")] string Phone { get; set; }
}

// Combine them
[OdooModel("res.partner")]
public interface IPartner : IAddress, IContactInfo
{
    [OdooField("name")] string Name { get; set; }
}
```

### 3. The Environment

The environment is your entry point:

```csharp
var env = new OdooEnvironment(userId: 1);
```

It provides:
- User context (`env.UserId`)
- Data cache (`env.Cache`)
- Record factory methods

### 4. Accessing Records

#### Single Record

```csharp
var partner = env.Partner(10);
Console.WriteLine(partner.Name);
```

#### Multiple Records (RecordSet)

```csharp
var partners = env.Partners(new[] { 10, 11, 12 });

// Iterate
foreach (var p in partners)
{
    Console.WriteLine(p.Name);
}

// Filter
var companies = partners.Where(p => p.IsCompany);

// Map
var names = partners.Select(p => p.Name);
```

### 5. Modifying Data

```csharp
var partner = env.Partner(10);
partner.Name = "New Name";
partner.City = "Brussels";

// Check what changed
var dirtyFields = env.Cache.GetDirtyFields("res.partner", 10);
// Returns: ["name", "city"]
```

## Adding Python Extensions

### 1. Enable Python

Make sure Python is initialized in [`Program.cs`](Program.cs):

```csharp
Runtime.PythonDLL = "/path/to/your/python3";
PythonEngine.Initialize();
```

### 2. Create Python Module

Create a file in [`Scripts/`](Scripts/):

```python
# Scripts/my_extensions.py
def custom_validation(env, partner_id):
    """Custom validation logic"""
    return True

def compute_field(record):
    """Compute a field value"""
    return f"{record.Name} - Computed"
```

### 3. Use from C#

```csharp
var loader = new PythonModuleLoader("./Scripts");
var bridge = new OdooPythonBridge(env, loader);

var result = bridge.ExecuteModuleMethod<bool>(
    "my_extensions",
    "custom_validation",
    partnerId);
```

## Defining Your Own Models

### Step 1: Create the Interface

In [`Models/Definitions.cs`](Models/Definitions.cs):

```csharp
[OdooModel("custom.model")]
public interface ICustomModel : IOdooRecord
{
    [OdooField("name")]
    string Name { get; set; }
    
    [OdooField("description")]
    string? Description { get; set; }
    
    [OdooField("amount")]
    decimal Amount { get; set; }
}
```

### Step 2: Generate Implementation

With the Source Generator enabled, the implementation is automatically created.

For now, you can manually create in [`Generated/`](Generated/):

```csharp
public readonly struct CustomModelRecord : ICustomModel
{
    private readonly IEnvironment _env;
    private readonly int _id;
    private const string ModelName = "custom.model";
    
    public CustomModelRecord(IEnvironment env, int id)
    {
        _env = env;
        _id = id;
    }
    
    public int Id => _id;
    public IEnvironment Env => _env;
    
    public string Name
    {
        get => _env.Cache.GetValue<string>(ModelName, _id, "name");
        set
        {
            _env.Cache.SetValue(ModelName, _id, "name", value);
            _env.Cache.MarkDirty(ModelName, _id, "name");
        }
    }
    
    // ... other properties
}
```

### Step 3: Add Environment Extension

```csharp
public static class CustomEnvironmentExtensions
{
    public static ICustomModel CustomModel(this IEnvironment env, int id)
    {
        return new CustomModelRecord(env, id);
    }
    
    public static RecordSet<ICustomModel> CustomModels(
        this IEnvironment env, int[] ids)
    {
        return new RecordSet<ICustomModel>(
            env,
            "custom.model",
            ids,
            (e, id) => new CustomModelRecord(e, id));
    }
}
```

### Step 4: Use It

```csharp
var model = env.CustomModel(1);
model.Name = "Test";
model.Amount = 100.50m;
```

## Working with the Cache

### Seeding Data

```csharp
var cache = new SimpleValueCache();
cache.BulkLoad("res.partner", new()
{
    [10] = new()
    {
        ["name"] = "Test Partner",
        ["email"] = "test@example.com",
        ["is_company"] = true
    }
});
```

### Checking Dirty State

```csharp
var partner = env.Partner(10);
partner.Name = "Changed";

var dirtyFields = cache.GetDirtyFields("res.partner", 10);
// Returns: ["name"]

// Clear dirty state
cache.ClearDirty("res.partner", 10);
```

### Replacing the Cache

Implement [`IValueCache`](Core/OdooFramework.cs) for your database:

```csharp
public class SqlServerCache : IValueCache
{
    private readonly SqlConnection _connection;
    
    public T GetValue<T>(string model, int id, string field)
    {
        // Query database
        var sql = "SELECT @field FROM @table WHERE id = @id";
        // Execute and return
    }
    
    public void SetValue<T>(string model, int id, string field, T value)
    {
        // Update database
    }
    
    // ... implement other methods
}
```

Use it:

```csharp
var cache = new SqlServerCache(connectionString);
var env = new OdooEnvironment(userId: 1, cache: cache);
```

## Common Patterns

### Pattern 1: Batch Processing

```csharp
var partners = env.Partners(new[] { 10, 11, 12 });
foreach (var partner in partners)
{
    partner.Active = true;
    // Changes are tracked per record
}

// Save all changes at once
foreach (var partner in partners)
{
    var dirty = env.Cache.GetDirtyFields("res.partner", partner.Id);
    // Persist dirty fields to database
}
```

### Pattern 2: Filtering and Mapping

```csharp
var activeCompanies = env.Partners(allIds)
    .Where(p => p.IsCompany && p.Active)
    .Select(p => new { p.Id, p.Name, p.Email })
    .ToList();
```

### Pattern 3: Different Users/Contexts

```csharp
var adminEnv = new OdooEnvironment(userId: 1);
var userEnv = adminEnv.WithUser(userId: 2);

// Same cache, different user context
Console.WriteLine(adminEnv.UserId); // 1
Console.WriteLine(userEnv.UserId);  // 2
```

### Pattern 4: Python-Enhanced Fields

```csharp
var computedField = PythonFieldHelpers.CreateValidator(
    "record.Name.startswith('Valid')",
    moduleLoader);

if (computedField(partner))
{
    // Valid
}
```

## Troubleshooting

### Issue: "No factory registered for type"

**Cause**: The environment doesn't know how to create records for your model.

**Solution**: Make sure you've created the extension methods or registered the factory:

```csharp
env.RegisterFactory<IPartner>(
    "res.partner",
    (e, id) => new PartnerRecord(e, id));
```

### Issue: Python initialization fails

**Cause**: Python DLL path is incorrect.

**Solution**: Update [`Program.cs`](Program.cs) with the correct path:

```bash
# Find your Python path
which python3
# or on Windows: where python
```

Then update:
```csharp
Runtime.PythonDLL = "/correct/path/to/python3";
```

### Issue: Cache returns default values

**Cause**: Data not loaded into cache.

**Solution**: Use `BulkLoad` to seed data:

```csharp
cache.BulkLoad("model.name", new() {
    [id] = new() { ["field"] = value }
});
```

## Next Steps

1. ✅ Run the demos to see everything in action
2. 📖 Read the [Complete Documentation](ODOO_ORM_README.md)
3. 🔧 Define your own models in [`Models/Definitions.cs`](Models/Definitions.cs)
4. 🐍 Create Python extensions in [`Scripts/`](Scripts/)
5. 💾 Implement a real database cache
6. 🚀 Build your application!

## Resources

- [Full Documentation](ODOO_ORM_README.md)
- [Basic Usage Demo](Examples/BasicUsageDemo.cs)
- [Python Integration Demo](Examples/PythonIntegrationDemo.cs)
- [Model Definitions](Models/Definitions.cs)
- [Source Generator](Odoo.SourceGenerator/OdooModelGenerator.cs)

## Getting Help

- Check the examples in [`Examples/`](Examples/)
- Review the Python samples in [`Scripts/`](Scripts/)
- Read the inline documentation in the code
- Explore the test demos by running `dotnet run`

Happy coding! 🎉