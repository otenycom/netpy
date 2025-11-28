using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Odoo.SourceGenerator
{
    /// <summary>
    /// Unified Wrapper Source Generator.
    /// Generates class-based wrappers that implement ALL visible interfaces for each model.
    /// Supports the "snowball" effect where downstream projects see cumulative interfaces.
    /// </summary>
    [Generator]
    public class OdooModelGenerator : ISourceGenerator
    {
        private Dictionary<string, int> _modelTokens = new();
        private Dictionary<(string Model, string Field), int> _fieldTokens = new();

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a syntax receiver to find interfaces with OdooModel attribute
            context.RegisterForSyntaxNotifications(() => new OdooModelSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not OdooModelSyntaxReceiver receiver)
                return;

            var compilation = context.Compilation;
            
            // === UNIFIED WRAPPER ARCHITECTURE ===
            // Step 1: Collect ALL [OdooModel] interfaces visible to this compilation
            // This includes interfaces from:
            //   - Current project (local interfaces)
            //   - Referenced assemblies (upstream interfaces)
            
            var allOdooInterfaces = CollectAllOdooInterfaces(compilation, receiver);
            
            // Step 2: Group interfaces by model name (e.g., "res.partner")
            var modelGroups = GroupInterfacesByModel(allOdooInterfaces);

            // Process Logic Methods
            var logicMethods = new List<LogicMethodInfo>();
            foreach (var methodDecl in receiver.MethodsToProcess)
            {
                var model = compilation.GetSemanticModel(methodDecl.SyntaxTree);
                var methodSymbol = model.GetDeclaredSymbol(methodDecl);
                
                if (methodSymbol == null) continue;

                var logicAttr = methodSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "OdooLogicAttribute");

                if (logicAttr != null)
                {
                    var modelName = logicAttr.ConstructorArguments[0].Value?.ToString() ?? "";
                    var methodName = logicAttr.ConstructorArguments[1].Value?.ToString() ?? "";
                    
                    logicMethods.Add(new LogicMethodInfo
                    {
                        MethodSymbol = methodSymbol,
                        ModelName = modelName,
                        MethodName = methodName
                    });
                }
            }

            // Generate Module Registrar if logic methods or models exist
            if (logicMethods.Any() || modelGroups.Any())
            {
                // Generate Typed Super Delegates
                var delegatesSource = GenerateSuperDelegates(logicMethods);
                context.AddSource("SuperDelegates.g.cs", SourceText.From(delegatesSource, Encoding.UTF8));
                
                // Generate RecordSet Extensions
                var logicExtensionsSource = GenerateLogicExtensions(logicMethods);
                context.AddSource("LogicExtensions.g.cs", SourceText.From(logicExtensionsSource, Encoding.UTF8));
            }

            // Step 3: Build unified model data from grouped interfaces
            var modelData = new List<UnifiedModelInfo>();

            foreach (var kvp in modelGroups)
            {
                var modelName = kvp.Key;
                var interfaces = kvp.Value;
                
                // Assign tokens using stable hashes for cross-assembly uniqueness
                if (!_modelTokens.ContainsKey(modelName))
                {
                    _modelTokens[modelName] = GetStableHashCode(modelName);
                }
                var modelToken = _modelTokens[modelName];

                // Collect ALL properties from ALL interfaces for this model
                var allProperties = new Dictionary<string, IPropertySymbol>();
                foreach (var iface in interfaces)
                {
                    foreach (var prop in GetAllProperties(iface))
                    {
                        var fieldName = GetOdooFieldName(prop);
                        // Use the first definition encountered (avoid duplicates)
                        if (!allProperties.ContainsKey(fieldName))
                        {
                            allProperties[fieldName] = prop;
                        }
                        
                        // Assign field tokens
                        var key = (modelName, fieldName);
                        if (!_fieldTokens.ContainsKey(key))
                        {
                            _fieldTokens[key] = GetStableHashCode($"{modelName}.{fieldName}");
                        }
                    }
                }

                var info = new UnifiedModelInfo
                {
                    ModelName = modelName,
                    ModelToken = modelToken,
                    Interfaces = interfaces,
                    Properties = allProperties.Values.ToList(),
                    // Use the "most specific" interface name for the class name
                    // In practice, use the first local interface or the most derived one
                    ClassName = GetUnifiedClassName(modelName, interfaces)
                };

                modelData.Add(info);
            }

            // Generate ModelSchema (assembly-specific namespace to avoid conflicts)
            var schemaSource = GenerateModelSchema(modelData, context.Compilation.AssemblyName);
            context.AddSource("ModelSchema.g.cs", SourceText.From(schemaSource, Encoding.UTF8));
            
            // Get safe assembly name for use in generated code
            var safeAssemblyName = (context.Compilation.AssemblyName ?? "App").Replace(".", "");

            // Generate for each model
            foreach (var info in modelData)
            {
                // Generate BatchContext
                var batchContextSource = GenerateBatchContext(info, safeAssemblyName);
                var batchContextName = $"{info.ClassName}BatchContext.g.cs";
                context.AddSource(batchContextName, SourceText.From(batchContextSource, Encoding.UTF8));

                // Generate Unified Wrapper CLASS (not struct)
                var wrapperSource = GenerateWrapperStruct(info, safeAssemblyName);
                var wrapperName = $"{info.ClassName}.g.cs";
                context.AddSource(wrapperName, SourceText.From(wrapperSource, Encoding.UTF8));

                // Generate Property Pipelines
                var pipelineSource = GeneratePropertyPipelines(info, safeAssemblyName);
                var pipelineName = $"{info.ClassName}Pipelines.g.cs";
                context.AddSource(pipelineName, SourceText.From(pipelineSource, Encoding.UTF8));
            }

            // Generate Values structs
            foreach (var info in modelData)
            {
                var valuesSource = GenerateValuesStruct(info, safeAssemblyName);
                var valuesName = $"{info.ClassName}Values.g.cs";
                context.AddSource(valuesName, SourceText.From(valuesSource, Encoding.UTF8));
            }

            // Generate environment extensions
            var extensionsSource = GenerateEnvironmentExtensions(modelData, safeAssemblyName);
            context.AddSource("OdooEnvironmentExtensions.g.cs", SourceText.From(extensionsSource, Encoding.UTF8));
            
            // Generate Module Registrar with both pipelines and factories
            if (logicMethods.Any() || modelData.Any())
            {
                var registrarSource = GenerateModuleRegistrar(logicMethods, modelData, context.Compilation.AssemblyName);
                context.AddSource("ModuleRegistrar.g.cs", SourceText.From(registrarSource, Encoding.UTF8));
            }
        }

        private string GenerateModelSchema(List<UnifiedModelInfo> models, string? assemblyName)
        {
            var sb = new StringBuilder();
            var safeAssemblyName = (assemblyName ?? "App").Replace(".", "");

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine();
            sb.AppendLine($"namespace Odoo.Generated.{safeAssemblyName}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Static schema registry with compile-time field and model tokens.");
            sb.AppendLine("    /// Eliminates string hashing overhead for cache lookups.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class ModelSchema");
            sb.AppendLine("    {");

            foreach (var model in models)
            {
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Schema for {model.ModelName}");
                sb.AppendLine($"        /// Unified wrapper implementing: {string.Join(", ", model.Interfaces.Select(i => i.Name))}");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static class {model.ClassName}");
                sb.AppendLine("        {");
                sb.AppendLine($"            public static readonly ModelHandle ModelToken = new({model.ModelToken});");
                sb.AppendLine($"            public const string ModelName = \"{model.ModelName}\";");
                sb.AppendLine();

                // Add field tokens
                foreach (var prop in model.Properties)
                {
                    var fieldName = GetOdooFieldName(prop);
                    var key = (model.ModelName, fieldName);
                    var token = _fieldTokens[key];
                    
                    sb.AppendLine($"            /// <summary>Field: {fieldName}</summary>");
                    sb.AppendLine($"            public static readonly FieldHandle {prop.Name} = new({token});");
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string GenerateBatchContext(UnifiedModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            // Use the first interface's namespace for backward compatibility
            var namespaceName = model.InterfaceSymbol?.ContainingNamespace?.ToDisplayString() ?? $"Odoo.Generated.{safeAssemblyName}";
            var className = model.ClassName;
            var contextName = $"{className}BatchContext";
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine($"using {schemaNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}.Generated");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Batch context for efficient {className} record iteration.");
            sb.AppendLine($"    /// Lives on the stack, caches column spans for batch access.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public ref struct {contextName}");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly IColumnarCache _cache;");
            sb.AppendLine("        private readonly int[] _ids;");
            sb.AppendLine();

            // Generate column span fields
            foreach (var prop in model.Properties)
            {
                var propertyType = prop.Type.ToDisplayString();
                sb.AppendLine($"        private ReadOnlySpan<{propertyType}> _{ToCamelCase(prop.Name)}Column;");
                sb.AppendLine($"        private bool _{ToCamelCase(prop.Name)}Loaded;");
            }

            sb.AppendLine();
            sb.AppendLine($"        public {contextName}(IColumnarCache cache, int[] ids)");
            sb.AppendLine("        {");
            sb.AppendLine("            _cache = cache;");
            sb.AppendLine("            _ids = ids;");
            
            // Initialize all fields to default
            foreach (var prop in model.Properties)
            {
                sb.AppendLine($"            _{ToCamelCase(prop.Name)}Column = default;");
                sb.AppendLine($"            _{ToCamelCase(prop.Name)}Loaded = false;");
            }
            
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generate lazy loader methods for each column
            foreach (var prop in model.Properties)
            {
                var propertyType = prop.Type.ToDisplayString();
                var camelName = ToCamelCase(prop.Name);
                
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Get the {prop.Name} column (lazy-loaded)");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"        public ReadOnlySpan<{propertyType}> Get{prop.Name}Column()");
                sb.AppendLine("        {");
                sb.AppendLine($"            if (!_{camelName}Loaded)");
                sb.AppendLine("            {");
                sb.AppendLine($"                _{camelName}Column = _cache.GetColumnSpan<{propertyType}>(");
                sb.AppendLine($"                    ModelSchema.{className}.ModelToken,");
                sb.AppendLine($"                    _ids,");
                sb.AppendLine($"                    ModelSchema.{className}.{prop.Name}");
                sb.AppendLine("                );");
                sb.AppendLine($"                _{camelName}Loaded = true;");
                sb.AppendLine("            }");
                sb.AppendLine($"            return _{camelName}Column;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Generate a UNIFIED WRAPPER CLASS (not struct) implementing ALL visible interfaces.
        /// This is the core of the unified wrapper architecture.
        /// </summary>
        private string GenerateWrapperStruct(UnifiedModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";
            var className = model.ClassName;
            
            // Build the interface list for implementation
            var interfaceList = string.Join(", ", model.Interfaces.Select(i => i.ToDisplayString()));

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine($"using {schemaNamespace};");
            
            // Add using statements for all interface namespaces
            foreach (var iface in model.Interfaces)
            {
                var ns = iface.ContainingNamespace?.ToDisplayString();
                if (!string.IsNullOrEmpty(ns))
                {
                    sb.AppendLine($"using {ns};");
                }
            }
            
            sb.AppendLine();
            sb.AppendLine($"namespace {schemaNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Unified wrapper CLASS for {model.ModelName}");
            sb.AppendLine($"    /// Implements: {interfaceList}");
            sb.AppendLine($"    /// Supports identity map and reference equality.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public sealed class {className} : {interfaceList}, IRecordWrapper");
            sb.AppendLine("    {");
            
            // Private field for handle - classes use private field + property
            sb.AppendLine("        private readonly RecordHandle _handle;");
            sb.AppendLine();
            
            // Constructor
            sb.AppendLine($"        public {className}(RecordHandle handle)");
            sb.AppendLine("        {");
            sb.AppendLine("            _handle = handle;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Handle property (from IRecordWrapper)
            sb.AppendLine("        public RecordHandle Handle => _handle;");
            sb.AppendLine();

            // IOdooRecord properties
            sb.AppendLine("        public int Id => _handle.Id;");
            sb.AppendLine("        public IEnvironment Env => _handle.Env;");
            sb.AppendLine();

            // Generate properties that delegate to pipelines
            foreach (var prop in model.Properties)
            {
                var propertyType = prop.Type.ToDisplayString();
                var pipelineClass = $"{className}Pipelines";
                
                sb.AppendLine($"        public {propertyType} {prop.Name}");
                sb.AppendLine("        {");
                sb.AppendLine($"            get => {pipelineClass}.Get_{prop.Name}(_handle);");
                if (!prop.IsReadOnly)
                {
                    sb.AppendLine($"            set => {pipelineClass}.Set_{prop.Name}(_handle, value);");
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            
            // Override Equals and GetHashCode for identity
            sb.AppendLine("        public override bool Equals(object? obj)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return obj is {className} other && _handle.Id == other._handle.Id && _handle.Model.Token == other._handle.Model.Token;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public override int GetHashCode() => HashCode.Combine(_handle.Id, _handle.Model.Token);");
            sb.AppendLine();
            sb.AppendLine($"        public override string ToString() => $\"{model.ModelName}({{Id}})\";");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string GeneratePropertyPipelines(UnifiedModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var className = model.ClassName;
            var pipelineClass = $"{className}Pipelines";
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine($"using {schemaNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {schemaNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static class {pipelineClass}");
            sb.AppendLine("    {");

            foreach (var prop in model.Properties)
            {
                var propertyType = prop.Type.ToDisplayString();
                var fieldName = GetOdooFieldName(prop);

                // GETTER
                sb.AppendLine($"        public static {propertyType} Get_{prop.Name}(RecordHandle handle)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var pipeline = handle.Env.GetPipeline<Func<RecordHandle, {propertyType}>>(");
                sb.AppendLine($"                \"{model.ModelName}\", \"get_{fieldName}\");");
                sb.AppendLine("            return pipeline(handle);");
                sb.AppendLine("        }");
                sb.AppendLine();

                // GETTER BASE
                sb.AppendLine($"        public static {propertyType} Get_{prop.Name}_Base(RecordHandle handle)");
                sb.AppendLine("        {");
                sb.AppendLine($"            return handle.Env.Columns.GetValue<{propertyType}>(");
                sb.AppendLine($"                ModelSchema.{className}.ModelToken,");
                sb.AppendLine($"                handle.Id,");
                sb.AppendLine($"                ModelSchema.{className}.{prop.Name});");
                sb.AppendLine("        }");
                sb.AppendLine();

                if (!prop.IsReadOnly)
                {
                    // SETTER
                    sb.AppendLine($"        public static void Set_{prop.Name}(RecordHandle handle, {propertyType} value)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            var pipeline = handle.Env.GetPipeline<Action<RecordHandle, {propertyType}>>(");
                    sb.AppendLine($"                \"{model.ModelName}\", \"set_{fieldName}\");");
                    sb.AppendLine("            pipeline(handle, value);");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    // SETTER BASE
                    sb.AppendLine($"        public static void Set_{prop.Name}_Base(RecordHandle handle, {propertyType} value)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            handle.Env.Columns.SetValue(");
                    sb.AppendLine($"                ModelSchema.{className}.ModelToken,");
                    sb.AppendLine($"                handle.Id,");
                    sb.AppendLine($"                ModelSchema.{className}.{prop.Name},");
                    sb.AppendLine($"                value);");
                    sb.AppendLine($"            handle.Env.Columns.MarkDirty(");
                    sb.AppendLine($"                ModelSchema.{className}.ModelToken,");
                    sb.AppendLine($"                handle.Id,");
                    sb.AppendLine($"                ModelSchema.{className}.{prop.Name});");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string GenerateValuesStruct(UnifiedModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var className = model.ClassName;
            var valuesName = $"{className}Values";
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine($"using {schemaNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {schemaNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Values for creating a {model.ModelName} record.");
            sb.AppendLine($"    /// Use with: env.Create(new {valuesName} {{ Name = \"...\" }})");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public struct {valuesName}");
            sb.AppendLine("    {");

            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly) continue;

                var propertyType = prop.Type.ToDisplayString();
                // Make nullable for optional initialization
                var nullableType = propertyType.EndsWith("?") ? propertyType : $"{propertyType}?";
                
                sb.AppendLine($"        public {nullableType} {prop.Name} {{ get; init; }}");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }


        private string GenerateEnvironmentExtensions(List<UnifiedModelInfo> models, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using Odoo.Core;");
            
            // Add using statements for all interface namespaces
            var allNamespaces = new HashSet<string>();
            foreach (var model in models)
            {
                foreach (var iface in model.Interfaces)
                {
                    var ns = iface.ContainingNamespace?.ToDisplayString();
                    if (!string.IsNullOrEmpty(ns))
                    {
                        allNamespaces.Add(ns);
                    }
                }
            }
            
            foreach (var ns in allNamespaces)
            {
                sb.AppendLine($"using {ns};");
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {schemaNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Generated extension methods for IEnvironment.");
            sb.AppendLine("    /// Use env.GetRecord&lt;T&gt;(id) and env.GetRecords&lt;T&gt;(ids) for record access.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class OdooEnvironmentExtensions");
            sb.AppendLine("    {");

            foreach (var model in models)
            {
                var className = model.ClassName;
                var valuesName = $"{className}Values";
                var primaryInterface = model.Interfaces.FirstOrDefault();

                // Only generate Create method - record access is via env.GetRecord<T>()
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Create a new {model.ModelName} record.");
                sb.AppendLine($"        /// Registers in identity map for reference equality.");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static {primaryInterface?.ToDisplayString() ?? "IOdooRecord"} Create(");
                sb.AppendLine($"            this IEnvironment env,");
                sb.AppendLine($"            {valuesName} values)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var newId = env.IdGenerator.NextId(\"{model.ModelName}\");");
                sb.AppendLine($"            var modelToken = ModelSchema.{className}.ModelToken;");
                sb.AppendLine($"            var handle = new RecordHandle(env, newId, modelToken);");
                sb.AppendLine($"            var record = new {className}(handle);");
                sb.AppendLine();
                sb.AppendLine($"            // Register in identity map");
                sb.AppendLine($"            if (env is OdooEnvironment odooEnv)");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                odooEnv.RegisterInIdentityMap(modelToken.Token, newId, record);");
                sb.AppendLine($"            }}");
                sb.AppendLine();

                foreach (var prop in model.Properties)
                {
                    if (prop.IsReadOnly) continue;

                    var propertyType = prop.Type.ToDisplayString();
                    var isNullable = propertyType.EndsWith("?");
                    var pipelineClass = $"{className}Pipelines";
                    
                    sb.AppendLine($"            if (values.{prop.Name} is not null)");
                    sb.AppendLine("            {");
                    
                    if (isNullable || !prop.Type.IsValueType)
                    {
                        sb.AppendLine($"                {pipelineClass}.Set_{prop.Name}(handle, values.{prop.Name});");
                    }
                    else
                    {
                        sb.AppendLine($"                {pipelineClass}.Set_{prop.Name}(handle, values.{prop.Name}.Value);");
                    }
                    
                    sb.AppendLine("            }");
                }

                sb.AppendLine();
                sb.AppendLine($"            return record;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private List<IPropertySymbol> GetAllProperties(INamedTypeSymbol interfaceSymbol)
        {
            var properties = new List<IPropertySymbol>();
            var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            void CollectProperties(INamedTypeSymbol symbol)
            {
                if (!visited.Add(symbol))
                    return;

                foreach (var member in symbol.GetMembers())
                {
                    if (member is IPropertySymbol property && 
                        property.Name != "Id" && 
                        property.Name != "Env")
                    {
                        properties.Add(property);
                    }
                }

                foreach (var baseInterface in symbol.Interfaces)
                {
                    CollectProperties(baseInterface);
                }
            }

            CollectProperties(interfaceSymbol);
            return properties;
        }

        private string GetOdooModelName(INamedTypeSymbol interfaceSymbol)
        {
            var attribute = interfaceSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "OdooModelAttribute");

            if (attribute?.ConstructorArguments.Length > 0)
            {
                return attribute.ConstructorArguments[0].Value?.ToString() ?? "";
            }

            return "";
        }

        private string GetOdooFieldName(IPropertySymbol property)
        {
            var attribute = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "OdooFieldAttribute");

            if (attribute?.ConstructorArguments.Length > 0)
            {
                return attribute.ConstructorArguments[0].Value?.ToString() ?? property.Name.ToLower();
            }

            return property.Name.ToLower();
        }

        private string ToCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
                return value;
            
            return char.ToLower(value[0]) + value.Substring(1);
        }

        /// <summary>
        /// Deterministic hash code for stable tokens across compilations.
        /// This ensures tokens are unique across different assemblies.
        /// </summary>
        private static int GetStableHashCode(string str)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in str)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }

        private class ModelInfo
        {
            public INamedTypeSymbol InterfaceSymbol { get; set; } = null!;
            public string ModelName { get; set; } = "";
            public int ModelToken { get; set; }
            public List<IPropertySymbol> Properties { get; set; } = new();
        }
        
        /// <summary>
        /// Unified model info for the snowball architecture.
        /// Contains ALL interfaces for a single model across all visible assemblies.
        /// </summary>
        private class UnifiedModelInfo
        {
            public string ModelName { get; set; } = "";
            public int ModelToken { get; set; }
            public string ClassName { get; set; } = "";
            public List<INamedTypeSymbol> Interfaces { get; set; } = new();
            public List<IPropertySymbol> Properties { get; set; } = new();
            
            // For backward compatibility with existing generate methods
            public INamedTypeSymbol InterfaceSymbol => Interfaces.FirstOrDefault()!;
        }
        
        /// <summary>
        /// Collect ALL interfaces with [OdooModel] attribute visible to this compilation.
        /// This includes local interfaces AND interfaces from referenced assemblies.
        /// </summary>
        private List<INamedTypeSymbol> CollectAllOdooInterfaces(Compilation compilation, OdooModelSyntaxReceiver receiver)
        {
            var result = new List<INamedTypeSymbol>();
            var processed = new HashSet<string>(StringComparer.Ordinal);
            
            // 1. Collect from current compilation (local interfaces)
            foreach (var interfaceDecl in receiver.InterfacesToProcess)
            {
                var model = compilation.GetSemanticModel(interfaceDecl.SyntaxTree);
                var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl);
                
                if (interfaceSymbol == null) continue;
                
                var modelName = GetOdooModelName(interfaceSymbol);
                if (string.IsNullOrEmpty(modelName)) continue;
                
                var key = interfaceSymbol.ToDisplayString();
                if (processed.Add(key))
                {
                    result.Add(interfaceSymbol);
                }
            }
            
            // 2. Collect from referenced assemblies
            foreach (var reference in compilation.References)
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                if (assemblySymbol == null) continue;
                
                CollectOdooInterfacesFromNamespace(assemblySymbol.GlobalNamespace, result, processed);
            }
            
            return result;
        }
        
        /// <summary>
        /// Recursively scan a namespace for [OdooModel] interfaces.
        /// </summary>
        private void CollectOdooInterfacesFromNamespace(
            INamespaceSymbol ns,
            List<INamedTypeSymbol> result,
            HashSet<string> processed)
        {
            foreach (var type in ns.GetTypeMembers())
            {
                if (type.TypeKind == TypeKind.Interface)
                {
                    var modelName = GetOdooModelName(type);
                    if (!string.IsNullOrEmpty(modelName))
                    {
                        var key = type.ToDisplayString();
                        if (processed.Add(key))
                        {
                            result.Add(type);
                        }
                    }
                }
            }
            
            foreach (var childNs in ns.GetNamespaceMembers())
            {
                CollectOdooInterfacesFromNamespace(childNs, result, processed);
            }
        }
        
        /// <summary>
        /// Group interfaces by their Odoo model name.
        /// e.g., All interfaces with [OdooModel("res.partner")] are grouped together.
        /// </summary>
        private Dictionary<string, List<INamedTypeSymbol>> GroupInterfacesByModel(List<INamedTypeSymbol> interfaces)
        {
            var groups = new Dictionary<string, List<INamedTypeSymbol>>();
            
            foreach (var iface in interfaces)
            {
                var modelName = GetOdooModelName(iface);
                if (string.IsNullOrEmpty(modelName)) continue;
                
                if (!groups.TryGetValue(modelName, out var list))
                {
                    list = new List<INamedTypeSymbol>();
                    groups[modelName] = list;
                }
                
                list.Add(iface);
            }
            
            return groups;
        }
        
        /// <summary>
        /// Generate a unified class name from the model name.
        /// e.g., "res.partner" -> "Partner"
        /// </summary>
        private string GetUnifiedClassName(string modelName, List<INamedTypeSymbol> interfaces)
        {
            // Strategy: Use the model name to derive a class name
            // e.g., "res.partner" -> "Partner"
            // e.g., "sale.order" -> "SaleOrder"
            
            var parts = modelName.Split('.');
            var className = string.Join("", parts.Select(p =>
                char.ToUpper(p[0]) + p.Substring(1)));
            
            return className;
        }

        private string GenerateModuleRegistrar(List<LogicMethodInfo> methods, List<UnifiedModelInfo> models, string? assemblyName)
        {
            var sb = new StringBuilder();
            var safeAssemblyName = (assemblyName ?? "App").Replace(".", "");
            
            sb.AppendLine("using Odoo.Core.Pipeline;");
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine("using Odoo.Core.Modules;");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            
            // Add using statements for all interface namespaces
            var allNamespaces = new HashSet<string>();
            foreach (var model in models)
            {
                foreach (var iface in model.Interfaces)
                {
                    var ns = iface.ContainingNamespace?.ToDisplayString();
                    if (!string.IsNullOrEmpty(ns))
                    {
                        allNamespaces.Add(ns);
                    }
                }
            }
            
            foreach (var ns in allNamespaces)
            {
                sb.AppendLine($"using {ns};");
            }
            
            sb.AppendLine();
            sb.AppendLine($"namespace Odoo.Generated.{safeAssemblyName}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Module registrar for unified wrappers.");
            sb.AppendLine("    /// Registers factories that create class-based wrappers with identity map support.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public class ModuleRegistrar : IModuleRegistrar");
            sb.AppendLine("    {");
            
            // RegisterPipelines method
            sb.AppendLine("        public void RegisterPipelines(IPipelineBuilder builder)");
            sb.AppendLine("        {");

            // Register Property Pipelines (Base)
            foreach (var model in models)
            {
                var className = model.ClassName;
                var pipelineClass = $"{className}Pipelines";

                foreach (var prop in model.Properties)
                {
                    var fieldName = GetOdooFieldName(prop);
                    var propertyType = prop.Type.ToDisplayString();

                    // Register Getter Base
                    sb.AppendLine($"            builder.RegisterBase(\"{model.ModelName}\", \"get_{fieldName}\", ");
                    sb.AppendLine($"                (Func<RecordHandle, {propertyType}>){pipelineClass}.Get_{prop.Name}_Base);");

                    if (!prop.IsReadOnly)
                    {
                        // Register Setter Base
                        sb.AppendLine($"            builder.RegisterBase(\"{model.ModelName}\", \"set_{fieldName}\", ");
                        sb.AppendLine($"                (Action<RecordHandle, {propertyType}>){pipelineClass}.Set_{prop.Name}_Base);");
                    }
                }
            }

            foreach (var method in methods)
            {
                var containingType = method.MethodSymbol.ContainingType.ToDisplayString();
                var methodName = method.MethodSymbol.Name;
                var isOverride = method.MethodSymbol.Parameters.Any(p => p.Name == "super");
                
                if (isOverride)
                {
                    // Register Override
                    sb.AppendLine($"            builder.RegisterOverride(\"{method.ModelName}\", \"{method.MethodName}\", 10, ");
                    sb.AppendLine($"                ({GetDelegateCast(method.MethodSymbol)}){containingType}.{methodName});");
                }
                else
                {
                    // Register Base
                    sb.AppendLine($"            builder.RegisterBase(\"{method.ModelName}\", \"{method.MethodName}\", ");
                    sb.AppendLine($"                ({GetDelegateCast(method.MethodSymbol)}){containingType}.{methodName});");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();
            
            // RegisterFactories method - creates class instances with constructor
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Register factories for unified wrapper classes.");
            sb.AppendLine("        /// These factories create class instances that support identity map.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public void RegisterFactories(ModelRegistry modelRegistry)");
            sb.AppendLine("        {");

            foreach (var model in models)
            {
                var className = model.ClassName;
                
                sb.AppendLine($"            // Factory for {model.ModelName} - unified wrapper implementing {model.Interfaces.Count} interface(s)");
                sb.AppendLine($"            modelRegistry.RegisterFactory(\"{model.ModelName}\", ");
                sb.AppendLine($"                (env, id) => new {className}(new RecordHandle(env, id, ModelSchema.{className}.ModelToken)));");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string GetDelegateCast(IMethodSymbol method)
        {
            // Generate Func<...> or Action<...> cast string
            var parameters = method.Parameters.Select(p => p.Type.ToDisplayString()).ToList();
            var returnType = method.ReturnType.ToDisplayString();
            
            if (returnType == "void")
            {
                return $"Action<{string.Join(", ", parameters)}>";
            }
            else
            {
                return $"Func<{string.Join(", ", parameters)}, {returnType}>";
            }
        }

        private string GenerateSuperDelegates(List<LogicMethodInfo> methods)
        {
            var sb = new StringBuilder();
            var processed = new HashSet<string>();

            foreach (var method in methods)
            {
                var key = $"{method.ModelName}.{method.MethodName}";
                if (processed.Contains(key)) continue;
                processed.Add(key);

                var modelPart = method.ModelName.Replace(".", "");
                var ns = $"Odoo.Generated.{modelPart}.Super";
                
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
                
                // Generate delegate signature matching the method (minus the super param if it was an override)
                // Actually, the super delegate signature matches the BASE method signature.
                // So we need to find the base signature or infer it.
                // If this is an override, the 'super' param type IS the delegate type we want to generate!
                
                // Let's look at the 'super' parameter if it exists
                var superParam = method.MethodSymbol.Parameters.FirstOrDefault(p => p.Name == "super");
                if (superParam != null)
                {
                    // It's an override, use the super param type name (which we are generating)
                    // But we need to generate the definition.
                    // The definition should match the signature of the delegate.
                    // Wait, if we use typed delegates, the user code refers to Odoo.Generated...ActionVerify.
                    // So we must generate that delegate type.
                    
                    // The signature of the super delegate matches the method signature MINUS the super param.
                    var parameters = method.MethodSymbol.Parameters
                        .Where(p => p.Name != "super")
                        .Select(p => $"{p.Type.ToDisplayString()} {p.Name}");
                    
                    var returnType = method.MethodSymbol.ReturnType.ToDisplayString();
                    var delegateName = method.MethodSymbol.Parameters.Last().Type.Name; // e.g. ActionVerify

                    sb.AppendLine($"    public delegate {returnType} {delegateName}({string.Join(", ", parameters)});");
                }
                else
                {
                    // It's a base method. We should generate a delegate for it so overrides can use it.
                    // Delegate name convention? Let's use MethodName.
                    var parameters = method.MethodSymbol.Parameters
                        .Select(p => $"{p.Type.ToDisplayString()} {p.Name}");
                    var returnType = method.MethodSymbol.ReturnType.ToDisplayString();
                    
                    sb.AppendLine($"    public delegate {returnType} {method.MethodName}({string.Join(", ", parameters)});");
                }

                sb.AppendLine("}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string GenerateLogicExtensions(List<LogicMethodInfo> methods)
        {
            // We need the assembly name to make the namespace unique
            // Since we don't have it passed here easily, we'll extract it from the first method's symbol
            var assemblyName = methods.FirstOrDefault()?.MethodSymbol.ContainingAssembly.Name ?? "App";
            var safeAssemblyName = assemblyName.Replace(".", "");

            var sb = new StringBuilder();
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine("using Odoo.Core.Pipeline;");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            
            // Add using statements for model namespaces
            var namespaces = methods.Select(m => m.MethodSymbol.ContainingNamespace.ToDisplayString()).Distinct();
            foreach (var ns in namespaces)
            {
                sb.AppendLine($"using {ns};");
            }

            sb.AppendLine();
            sb.AppendLine($"namespace Odoo.Generated.{safeAssemblyName}.Logic");
            sb.AppendLine("{");
            sb.AppendLine("    public static class LogicExtensions");
            sb.AppendLine("    {");

            var processed = new HashSet<string>();
            foreach (var method in methods)
            {
                var key = $"{method.ModelName}.{method.MethodName}";
                if (processed.Contains(key)) continue;
                processed.Add(key);

                // Generate extension method on RecordSet<T>
                // We need to know T. The first parameter is usually RecordSet<T>.
                var firstParam = method.MethodSymbol.Parameters.FirstOrDefault();
                // Check if type is RecordSet<T> by checking name and generic arguments
                if (firstParam != null && firstParam.Type is INamedTypeSymbol namedType &&
                    namedType.IsGenericType && namedType.Name == "RecordSet")
                {
                    if (namedType.TypeArguments.Length == 0) continue;
                    
                    var recordType = namedType.TypeArguments[0].ToDisplayString();
                    // Skip if type is invalid or an error type
                    if (string.IsNullOrWhiteSpace(recordType) || recordType.Contains("?") || recordType == "object") continue;
                    
                    // Method signature
                    var parametersArray = method.MethodSymbol.Parameters
                        .Where(p => p.Name != "super" && p.Name != "self") // Skip self (extension target) and super
                        .Select(p => $"{p.Type.ToDisplayString()} {p.Name}")
                        .ToArray();
                    
                    var paramNames = method.MethodSymbol.Parameters
                        .Where(p => p.Name != "super" && p.Name != "self")
                        .Select(p => p.Name)
                        .ToArray();
                        
                    var returnType = method.MethodSymbol.ReturnType.ToDisplayString();
                    var isVoid = returnType == "void";

                    // Generate method signature - handle empty parameters correctly
                    var parameterSignature = parametersArray.Length > 0
                        ? $"this RecordSet<{recordType}> self, {string.Join(", ", parametersArray)}"
                        : $"this RecordSet<{recordType}> self";

                    sb.AppendLine($"        public static {returnType} {method.MethodName}({parameterSignature})");
                    sb.AppendLine("        {");
                    
                    // Determine delegate type
                    var modelPart = method.ModelName.Replace(".", "");
                    var superParam = method.MethodSymbol.Parameters.FirstOrDefault(p => p.Name == "super");
                    var delegateName = superParam != null ? superParam.Type.Name : method.MethodName;
                    var delegateType = $"Odoo.Generated.{modelPart}.Super.{delegateName}";

                    // Get typed pipeline
                    sb.AppendLine($"            var pipeline = self.Env.Methods.GetPipeline<{delegateType}>(\"{method.ModelName}\", \"{method.MethodName}\");");
                    
                    sb.Append($"            ");
                    if (!isVoid) sb.Append("return ");
                    
                    var args = new List<string> { "self" };
                    args.AddRange(paramNames);
                    
                    sb.AppendLine($"pipeline({string.Join(", ", args)});");
                    
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    internal class OdooModelSyntaxReceiver : ISyntaxReceiver
    {
        public List<InterfaceDeclarationSyntax> InterfacesToProcess { get; } = new();
        public List<MethodDeclarationSyntax> MethodsToProcess { get; } = new();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is InterfaceDeclarationSyntax interfaceDecl &&
                interfaceDecl.AttributeLists.Count > 0)
            {
                InterfacesToProcess.Add(interfaceDecl);
            }
            
            if (syntaxNode is MethodDeclarationSyntax methodDecl &&
                methodDecl.AttributeLists.Count > 0)
            {
                // Check for OdooLogic attribute
                if (methodDecl.AttributeLists.SelectMany(al => al.Attributes)
                    .Any(a => a.Name.ToString().Contains("OdooLogic")))
                {
                    MethodsToProcess.Add(methodDecl);
                }
            }
        }
    }

    internal class LogicMethodInfo
    {
        public IMethodSymbol MethodSymbol { get; set; } = null!;
        public string ModelName { get; set; } = "";
        public string MethodName { get; set; } = "";
    }
}