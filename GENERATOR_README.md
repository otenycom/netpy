# Source Generator Configuration

## Generated Code Artifacts

The project is configured to emit generated code artifacts to disk for inspection.

### Output Location

Generated files will be placed in:
```
obj/Generated/
```

This directory is excluded from git tracking but the files remain on disk after compilation.

### Configuration

The following properties in [`netpy.csproj`](netpy.csproj) enable artifact generation:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
<CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)/Generated</CompilerGeneratedFilesOutputPath>
```

## Enabling the Source Generator

The Source Generator is currently **disabled** to avoid Roslyn dependency issues. To enable it:

### 1. Restore Roslyn Packages

The generator project already has the required packages in [`Odoo.SourceGenerator/Odoo.SourceGenerator.csproj`](Odoo.SourceGenerator/Odoo.SourceGenerator.csproj):

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
```

### 2. Uncomment the ProjectReference

In [`netpy.csproj`](netpy.csproj), uncomment these lines:

```xml
<ItemGroup>
  <ProjectReference Include="Odoo.SourceGenerator\Odoo.SourceGenerator.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 3. Build the Project

```bash
dotnet build
```

The generator will run during compilation and create files like:
- `obj/Generated/netpy.SourceGenerator/Odoo.SourceGenerator.OdooModelGenerator/PartnerRecord.g.cs`
- `obj/Generated/netpy.SourceGenerator/Odoo.SourceGenerator.OdooModelGenerator/UserRecord.g.cs`
- `obj/Generated/netpy.SourceGenerator/Odoo.SourceGenerator.OdooModelGenerator/OdooEnvironmentExtensions.g.cs`
- etc.

## What Gets Generated

The Source Generator analyzes interfaces marked with `[OdooModel]` and generates:

### 1. Record Structs

For each interface like `IPartner`, it generates a struct like `PartnerRecord`:

```csharp
public readonly struct PartnerRecord : IPartner
{
    private readonly IEnvironment _env;
    private readonly int _id;
    
    public string Name
    {
        get => _env.Cache.GetValue<string>("res.partner", _id, "name");
        set => _env.Cache.SetValue("res.partner", _id, "name", value);
    }
    // ... all other properties
}
```

### 2. Environment Extensions

Extension methods for convenient access:

```csharp
public static class OdooEnvironmentExtensions
{
    public static IPartner Partner(this IEnvironment env, int id)
    {
        return new PartnerRecord(env, id);
    }
    
    public static RecordSet<IPartner> Partners(this IEnvironment env, int[] ids)
    {
        return new RecordSet<IPartner>(env, "res.partner", ids, 
            (e, id) => new PartnerRecord(e, id));
    }
}
```

## Current Workaround

While the generator is disabled, the project includes sample generated code in:
- [`Generated/SampleGenerated.cs`](Generated/SampleGenerated.cs) - Manual example of PartnerRecord

This allows the project to build and run without Roslyn dependencies.

## Inspecting Generated Code

### View on Disk

```bash
# After building with the generator enabled
ls obj/Generated/netpy.SourceGenerator/Odoo.SourceGenerator.OdooModelGenerator/

# View a generated file
cat obj/Generated/.../PartnerRecord.g.cs
```

### View in IDE

Many IDEs (including VS Code with C# extensions) can display generated files:
1. Build the project
2. Navigate to Solution Explorer
3. Expand Dependencies → Analyzers → Odoo.SourceGenerator
4. View the generated `.g.cs` files

## Troubleshooting

### Generator Not Running

**Symptom**: No files in `obj/Generated/`

**Solutions**:
1. Ensure the ProjectReference is uncommented in `netpy.csproj`
2. Clean and rebuild: `dotnet clean && dotnet build`
3. Check that `Odoo.SourceGenerator.csproj` builds successfully
4. Verify Roslyn packages are restored

### Build Errors

**Symptom**: Compilation errors about missing types

**Solutions**:
1. Check that interfaces have `[OdooModel]` attribute
2. Verify properties have `[OdooField]` attribute
3. Ensure all interfaces inherit from `IOdooRecord`
4. Review generator output for errors

### Generated Code Not Used

**Symptom**: Project uses manual `Generated/SampleGenerated.cs` instead

**Solutions**:
1. Remove or exclude manual files when generator is enabled
2. Ensure generated namespace matches usage
3. Check that extension methods are in proper namespace

## Generator Internals

The Source Generator in [`Odoo.SourceGenerator/OdooModelGenerator.cs`](Odoo.SourceGenerator/OdooModelGenerator.cs):

1. **Initialization**: Registers a syntax receiver to find interfaces
2. **Execution**: 
   - Scans for `[OdooModel]` attributes
   - Analyzes interface hierarchy
   - Collects all properties (including from base interfaces)
   - Generates record struct with properties
   - Generates environment extension methods
3. **Output**: Adds generated source to compilation

## Performance Notes

- ✅ **Structs**: Zero heap allocation for record instances
- ✅ **Readonly**: Immutable record references
- ✅ **Compile-Time**: No runtime reflection
- ✅ **Cached**: Property access goes through cache layer

## Example Generated Output

For this interface:

```csharp
[OdooModel("res.partner")]
public interface IPartner : IMailThread, IAddress
{
    [OdooField("name")] string Name { get; set; }
    [OdooField("email")] string Email { get; set; }
}
```

The generator creates (example location):
```
obj/Generated/netpy.SourceGenerator/Odoo.SourceGenerator.OdooModelGenerator/PartnerRecord.g.cs
```

With full implementation of all properties from `IPartner`, `IMailThread`, `IAddress`, and base `IOdooRecord`.

## Further Reading

- [Source Generators Documentation](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)
- [Roslyn API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis)
- [C# Incremental Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)

---

For questions about the generator implementation, see [`Odoo.SourceGenerator/OdooModelGenerator.cs`](Odoo.SourceGenerator/OdooModelGenerator.cs).