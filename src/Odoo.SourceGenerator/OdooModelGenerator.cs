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
            var modelData = new List<ModelInfo>();

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
            if (logicMethods.Any() || receiver.InterfacesToProcess.Any())
            {
                // We'll generate the registrar after collecting model data

                // Generate Typed Super Delegates
                var delegatesSource = GenerateSuperDelegates(logicMethods);
                context.AddSource("SuperDelegates.g.cs", SourceText.From(delegatesSource, Encoding.UTF8));
                
                // Generate RecordSet Extensions
                var logicExtensionsSource = GenerateLogicExtensions(logicMethods);
                context.AddSource("LogicExtensions.g.cs", SourceText.From(logicExtensionsSource, Encoding.UTF8));
            }

            // First pass: collect all model and field information
            foreach (var interfaceDecl in receiver.InterfacesToProcess)
            {
                var model = compilation.GetSemanticModel(interfaceDecl.SyntaxTree);
                var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl);
                
                if (interfaceSymbol == null)
                    continue;

                var modelName = GetOdooModelName(interfaceSymbol);
                if (string.IsNullOrEmpty(modelName))
                    continue;

                var info = new ModelInfo
                {
                    InterfaceSymbol = interfaceSymbol,
                    ModelName = modelName,
                    Properties = GetAllProperties(interfaceSymbol)
                };

                // Assign tokens using stable hashes for cross-assembly uniqueness
                if (!_modelTokens.ContainsKey(modelName))
                {
                    _modelTokens[modelName] = GetStableHashCode(modelName);
                }
                info.ModelToken = _modelTokens[modelName];

                // Assign field tokens using stable hashes
                foreach (var prop in info.Properties)
                {
                    var fieldName = GetOdooFieldName(prop);
                    var key = (modelName, fieldName);
                    if (!_fieldTokens.ContainsKey(key))
                    {
                        _fieldTokens[key] = GetStableHashCode($"{modelName}.{fieldName}");
                    }
                }

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
                var batchContextName = $"{info.InterfaceSymbol.Name.Substring(1)}BatchContext.g.cs";
                context.AddSource(batchContextName, SourceText.From(batchContextSource, Encoding.UTF8));

                // Generate Record struct
                var recordSource = GenerateRecordStruct(info, safeAssemblyName);
                var recordName = $"{info.InterfaceSymbol.Name}Record.g.cs";
                context.AddSource(recordName, SourceText.From(recordSource, Encoding.UTF8));
            }

            // Generate Values structs
            foreach (var info in modelData)
            {
                var valuesSource = GenerateValuesStruct(info, safeAssemblyName);
                var valuesName = $"{info.InterfaceSymbol.Name.Substring(1)}Values.g.cs";
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

        private string GenerateModelSchema(List<ModelInfo> models, string? assemblyName)
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
                var className = model.InterfaceSymbol.Name.Substring(1); // Remove 'I'
                
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Schema for {model.ModelName}");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static class {className}");
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

        private string GenerateBatchContext(ModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var namespaceName = model.InterfaceSymbol.ContainingNamespace.ToDisplayString();
            var className = model.InterfaceSymbol.Name.Substring(1); // Remove 'I'
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

        private string GenerateRecordStruct(ModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var namespaceName = model.InterfaceSymbol.ContainingNamespace.ToDisplayString();
            var recordName = model.InterfaceSymbol.Name.Substring(1) + "Record";
            var className = model.InterfaceSymbol.Name.Substring(1);
            var batchContextName = $"{className}BatchContext";
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
            sb.AppendLine($"    /// Generated record struct for {model.InterfaceSymbol.Name}");
            sb.AppendLine($"    /// Model: {model.ModelName}");
            sb.AppendLine($"    /// Supports both single-record and batch access patterns.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public readonly struct {recordName} : {model.InterfaceSymbol.ToDisplayString()}");
            sb.AppendLine("    {");
            
            // Fields
            sb.AppendLine("        private readonly IEnvironment _env;");
            sb.AppendLine("        private readonly int _id;");
            sb.AppendLine();

            // Constructor
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Create a record instance");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public {recordName}(IEnvironment env, int id)");
            sb.AppendLine("        {");
            sb.AppendLine("            _env = env;");
            sb.AppendLine("            _id = id;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // IOdooRecord properties
            sb.AppendLine("        public int Id => _id;");
            sb.AppendLine("        public IEnvironment Env => _env;");
            sb.AppendLine();

            // Generate properties with dual access mode
            foreach (var prop in model.Properties)
            {
                GenerateOptimizedProperty(sb, prop, model, className);
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string GenerateValuesStruct(ModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var namespaceName = model.InterfaceSymbol.ContainingNamespace.ToDisplayString();
            var className = model.InterfaceSymbol.Name.Substring(1);
            var valuesName = $"{className}Values";
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine($"using {schemaNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}.Generated");
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

        private void GenerateOptimizedProperty(StringBuilder sb, IPropertySymbol property, ModelInfo model, string className)
        {
            var propertyType = property.Type.ToDisplayString();
            
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Odoo field: {GetOdooFieldName(property)}");
            sb.AppendLine($"        /// </summary>");

            if (property.IsReadOnly)
            {
                // Read-only property using columnar cache
                sb.AppendLine($"        public {propertyType} {property.Name}");
                sb.AppendLine("        {");
                sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"            get => _env.Columns.GetValue<{propertyType}>(");
                sb.AppendLine($"                ModelSchema.{className}.ModelToken,");
                sb.AppendLine($"                _id,");
                sb.AppendLine($"                ModelSchema.{className}.{property.Name});");
                sb.AppendLine("        }");
            }
            else
            {
                // Read-write property
                sb.AppendLine($"        public {propertyType} {property.Name}");
                sb.AppendLine("        {");
                sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"            get => _env.Columns.GetValue<{propertyType}>(");
                sb.AppendLine($"                ModelSchema.{className}.ModelToken,");
                sb.AppendLine($"                _id,");
                sb.AppendLine($"                ModelSchema.{className}.{property.Name});");
                sb.AppendLine("            set");
                sb.AppendLine("            {");
                sb.AppendLine($"                _env.Columns.SetValue(");
                sb.AppendLine($"                    ModelSchema.{className}.ModelToken,");
                sb.AppendLine($"                    _id,");
                sb.AppendLine($"                    ModelSchema.{className}.{property.Name},");
                sb.AppendLine($"                    value);");
                sb.AppendLine($"                _env.Columns.MarkDirty(");
                sb.AppendLine($"                    ModelSchema.{className}.ModelToken,");
                sb.AppendLine($"                    _id,");
                sb.AppendLine($"                    ModelSchema.{className}.{property.Name});");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
            }
            sb.AppendLine();
        }

        private string GenerateEnvironmentExtensions(List<ModelInfo> models, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using Odoo.Core;");
            
            // Add using statements for all model namespaces (both original and generated)
            var modelNamespaces = models.Select(m => m.InterfaceSymbol.ContainingNamespace.ToDisplayString()).Distinct();
            foreach (var ns in modelNamespaces)
            {
                sb.AppendLine($"using {ns};");
            }
            
            // Add using statements for generated namespaces
            var generatedNamespaces = models.Select(m => m.InterfaceSymbol.ContainingNamespace.ToDisplayString() + ".Generated").Distinct();
            foreach (var ns in generatedNamespaces)
            {
                sb.AppendLine($"using {ns};");
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {schemaNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Generated extension methods for IEnvironment");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class OdooEnvironmentExtensions");
            sb.AppendLine("    {");

            foreach (var model in models)
            {
                var recordName = model.InterfaceSymbol.Name.Substring(1) + "Record";
                var methodName = model.InterfaceSymbol.Name.Substring(1) + "s";
                var singleMethodName = model.InterfaceSymbol.Name.Substring(1);

                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Get a recordset for {model.InterfaceSymbol.Name}");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static RecordSet<{model.InterfaceSymbol.ToDisplayString()}> {methodName}(");
                sb.AppendLine($"            this IEnvironment env,");
                sb.AppendLine($"            int[] ids)");
                sb.AppendLine("        {");
                sb.AppendLine($"            return new RecordSet<{model.InterfaceSymbol.ToDisplayString()}>(");
                sb.AppendLine($"                env,");
                sb.AppendLine($"                \"{model.ModelName}\",");
                sb.AppendLine($"                ids,");
                sb.AppendLine($"                (e, id) => new {recordName}(e, id));");
                sb.AppendLine("        }");
                sb.AppendLine();

                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Get a single record for {model.InterfaceSymbol.Name}");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static {model.InterfaceSymbol.ToDisplayString()} {singleMethodName}(");
                sb.AppendLine($"            this IEnvironment env,");
                sb.AppendLine($"            int id)");
                sb.AppendLine("        {");
                sb.AppendLine($"            return new {recordName}(env, id);");
                sb.AppendLine("        }");
                sb.AppendLine();

                // Generate Create method
                var valuesName = $"{model.InterfaceSymbol.Name.Substring(1)}Values";
                
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Create a new {model.ModelName} record.");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        /// <example>");
                sb.AppendLine($"        /// var record = env.Create(new {valuesName} {{ Name = \"...\" }});");
                sb.AppendLine($"        /// </example>");
                sb.AppendLine($"        public static {model.InterfaceSymbol.ToDisplayString()} Create(");
                sb.AppendLine($"            this IEnvironment env,");
                sb.AppendLine($"            {valuesName} values)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var newId = env.IdGenerator.NextId(\"{model.ModelName}\");");
                sb.AppendLine($"            var modelToken = ModelSchema.{singleMethodName}.ModelToken;");
                sb.AppendLine();

                foreach (var prop in model.Properties)
                {
                    if (prop.IsReadOnly) continue;

                    var propertyType = prop.Type.ToDisplayString();
                    var isNullable = propertyType.EndsWith("?");
                    
                    sb.AppendLine($"            if (values.{prop.Name} is not null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                env.Columns.SetValue(");
                    sb.AppendLine($"                    modelToken,");
                    sb.AppendLine($"                    newId,");
                    sb.AppendLine($"                    ModelSchema.{singleMethodName}.{prop.Name},");
                    
                    if (isNullable || !prop.Type.IsValueType)
                    {
                        sb.AppendLine($"                    values.{prop.Name});");
                    }
                    else
                    {
                        sb.AppendLine($"                    values.{prop.Name}.Value);");
                    }
                    
                    sb.AppendLine("            }");
                }

                sb.AppendLine();
                sb.AppendLine($"            return new {recordName}(env, newId);");
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

        private string GenerateModuleRegistrar(List<LogicMethodInfo> methods, List<ModelInfo> models, string? assemblyName)
        {
            var sb = new StringBuilder();
            var safeAssemblyName = (assemblyName ?? "App").Replace(".", "");
            
            sb.AppendLine("using Odoo.Core.Pipeline;");
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine("using Odoo.Core.Modules;");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            
            // Add using statements for model namespaces
            var namespaces = models.Select(m => m.InterfaceSymbol.ContainingNamespace.ToDisplayString()).Distinct();
            foreach (var ns in namespaces)
            {
                sb.AppendLine($"using {ns};");
                sb.AppendLine($"using {ns}.Generated;");
            }
            
            sb.AppendLine();
            sb.AppendLine($"namespace Odoo.Generated.{safeAssemblyName}");
            sb.AppendLine("{");
            sb.AppendLine("    public class ModuleRegistrar : IModuleRegistrar");
            sb.AppendLine("    {");
            
            // RegisterPipelines method
            sb.AppendLine("        public void RegisterPipelines(IPipelineBuilder builder)");
            sb.AppendLine("        {");

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
            
            // RegisterFactories method
            sb.AppendLine("        public void RegisterFactories(ModelRegistry modelRegistry)");
            sb.AppendLine("        {");

            foreach (var model in models)
            {
                var recordName = model.InterfaceSymbol.Name.Substring(1) + "Record";
                var interfaceName = model.InterfaceSymbol.ToDisplayString();
                
                sb.AppendLine($"            modelRegistry.RegisterFactory(\"{model.ModelName}\", ");
                sb.AppendLine($"                (env, id) => new {recordName}(env, id));");
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
            sb.AppendLine("namespace Odoo.Generated.Logic");
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
                    sb.AppendLine($"            var pipeline = self.Env.Methods.GetPipeline<Delegate>(\"{method.ModelName}\", \"{method.MethodName}\");");
                    
                    // Invoke pipeline
                    // We need to cast the pipeline delegate to the correct type
                    // The delegate type is defined in SuperDelegates.g.cs
                    var modelPart = method.ModelName.Replace(".", "");
                    // Use the delegate name from the super parameter if available, otherwise MethodName
                    var superParam = method.MethodSymbol.Parameters.FirstOrDefault(p => p.Name == "super");
                    var delegateName = superParam != null ? superParam.Type.Name : method.MethodName;
                    
                    var delegateType = $"Odoo.Generated.{modelPart}.Super.{delegateName}";
                    
                    sb.Append($"            ");
                    if (!isVoid) sb.Append("return ");
                    
                    var args = new List<string> { "self" };
                    args.AddRange(paramNames);
                    
                    sb.AppendLine($"(({delegateType})pipeline)({string.Join(", ", args)});");
                    
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