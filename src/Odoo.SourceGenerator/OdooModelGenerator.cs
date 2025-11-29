using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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

                if (methodSymbol == null)
                    continue;

                var logicAttr = methodSymbol
                    .GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "OdooLogicAttribute");

                if (logicAttr != null)
                {
                    var modelName = logicAttr.ConstructorArguments[0].Value?.ToString() ?? "";
                    var methodName = logicAttr.ConstructorArguments[1].Value?.ToString() ?? "";

                    logicMethods.Add(
                        new LogicMethodInfo
                        {
                            MethodSymbol = methodSymbol,
                            ModelName = modelName,
                            MethodName = methodName,
                        }
                    );
                }
            }

            // Generate Module Registrar if logic methods or models exist
            if (logicMethods.Any() || modelGroups.Any())
            {
                // Generate Typed Super Delegates
                var delegatesSource = GenerateSuperDelegates(logicMethods);
                context.AddSource(
                    "SuperDelegates.g.cs",
                    SourceText.From(delegatesSource, Encoding.UTF8)
                );

                // Generate RecordSet Extensions
                var logicExtensionsSource = GenerateLogicExtensions(logicMethods);
                context.AddSource(
                    "LogicExtensions.g.cs",
                    SourceText.From(logicExtensionsSource, Encoding.UTF8)
                );
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
                    ClassName = GetUnifiedClassName(modelName, interfaces),
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
                context.AddSource(
                    batchContextName,
                    SourceText.From(batchContextSource, Encoding.UTF8)
                );

                // Generate Unified Wrapper CLASS (not struct)
                var wrapperSource = GenerateWrapperStruct(info, safeAssemblyName);
                var wrapperName = $"{info.ClassName}.g.cs";
                context.AddSource(wrapperName, SourceText.From(wrapperSource, Encoding.UTF8));

                // Generate Property Pipelines
                var pipelineSource = GeneratePropertyPipelines(info, safeAssemblyName);
                var pipelineName = $"{info.ClassName}Pipelines.g.cs";
                context.AddSource(pipelineName, SourceText.From(pipelineSource, Encoding.UTF8));
            }

            // Generate Values classes and Handlers
            foreach (var info in modelData)
            {
                // Generate Values class with RecordValueField<T>
                var valuesSource = GenerateValuesClass(info, safeAssemblyName);
                var valuesName = $"{info.ClassName}Values.g.cs";
                context.AddSource(valuesName, SourceText.From(valuesSource, Encoding.UTF8));

                // Generate ValuesHandler for no-reflection cache operations
                var handlerSource = GenerateValuesHandler(info, safeAssemblyName);
                var handlerName = $"{info.ClassName}ValuesHandler.g.cs";
                context.AddSource(handlerName, SourceText.From(handlerSource, Encoding.UTF8));
            }

            // Generate environment extensions
            var extensionsSource = GenerateEnvironmentExtensions(modelData, safeAssemblyName);
            context.AddSource(
                "OdooEnvironmentExtensions.g.cs",
                SourceText.From(extensionsSource, Encoding.UTF8)
            );

            // Generate Module Registrar with both pipelines and factories
            if (logicMethods.Any() || modelData.Any())
            {
                var registrarSource = GenerateModuleRegistrar(
                    logicMethods,
                    modelData,
                    context.Compilation.AssemblyName
                );
                context.AddSource(
                    "ModuleRegistrar.g.cs",
                    SourceText.From(registrarSource, Encoding.UTF8)
                );
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
            sb.AppendLine(
                "    /// Static schema registry with compile-time field and model tokens."
            );
            sb.AppendLine("    /// Eliminates string hashing overhead for cache lookups.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class ModelSchema");
            sb.AppendLine("    {");

            foreach (var model in models)
            {
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Schema for {model.ModelName}");
                sb.AppendLine(
                    $"        /// Unified wrapper implementing: {string.Join(", ", model.Interfaces.Select(i => i.Name))}"
                );
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static class {model.ClassName}");
                sb.AppendLine("        {");
                sb.AppendLine(
                    $"            public static readonly ModelHandle ModelToken = new({model.ModelToken});"
                );
                sb.AppendLine(
                    $"            public const string ModelName = \"{model.ModelName}\";"
                );
                sb.AppendLine();

                // Add field tokens
                foreach (var prop in model.Properties)
                {
                    var fieldName = GetOdooFieldName(prop);
                    var key = (model.ModelName, fieldName);
                    var token = _fieldTokens[key];

                    sb.AppendLine($"            /// <summary>Field: {fieldName}</summary>");
                    sb.AppendLine(
                        $"            public static readonly FieldHandle {prop.Name} = new({token});"
                    );
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
            var namespaceName =
                model.InterfaceSymbol?.ContainingNamespace?.ToDisplayString()
                ?? $"Odoo.Generated.{safeAssemblyName}";
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
            sb.AppendLine("        private readonly RecordId[] _ids;");
            sb.AppendLine();

            // Generate column span fields
            foreach (var prop in model.Properties)
            {
                var propertyType = prop.Type.ToDisplayString();
                sb.AppendLine(
                    $"        private ReadOnlySpan<{propertyType}> _{ToCamelCase(prop.Name)}Column;"
                );
                sb.AppendLine($"        private bool _{ToCamelCase(prop.Name)}Loaded;");
            }

            sb.AppendLine();
            sb.AppendLine($"        public {contextName}(IColumnarCache cache, RecordId[] ids)");
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
                sb.AppendLine(
                    $"        public ReadOnlySpan<{propertyType}> Get{prop.Name}Column()"
                );
                sb.AppendLine("        {");
                sb.AppendLine($"            if (!_{camelName}Loaded)");
                sb.AppendLine("            {");
                sb.AppendLine(
                    $"                _{camelName}Column = _cache.GetColumnSpan<{propertyType}>("
                );
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
        ///
        /// ODOO-ALIGNED PATTERN:
        /// - Getters: Direct cache read (no pipeline) for performance
        /// - Setters: Delegate to Write() pipeline for extensibility
        /// - Computed fields: Call compute method for computed properties
        /// </summary>
        private string GenerateWrapperStruct(UnifiedModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";
            var className = model.ClassName;

            // Build the interface list for implementation
            var interfaceList = string.Join(
                ", ",
                model.Interfaces.Select(i => i.ToDisplayString())
            );

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
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
            sb.AppendLine($"    /// ");
            sb.AppendLine($"    /// ODOO-ALIGNED PATTERN:");
            sb.AppendLine($"    /// - Getters: Direct cache read (no pipeline overhead)");
            sb.AppendLine($"    /// - Setters: Delegate to Write() pipeline for extensibility");
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
            sb.AppendLine("        public RecordId Id => _handle.Id;");
            sb.AppendLine("        public IEnvironment Env => _handle.Env;");
            sb.AppendLine();

            // Generate properties - ODOO PATTERN: direct cache reads, write() delegation
            foreach (var prop in model.Properties)
            {
                var propertyType = prop.Type.ToDisplayString();
                var fieldName = GetOdooFieldName(prop);

                // Check if property is computed
                var isComputed = prop.GetAttributes()
                    .Any(a => a.AttributeClass?.Name == "OdooComputeAttribute");

                // Get the primary interface for RecordSet type parameter
                var primaryInterface =
                    model.Interfaces.FirstOrDefault()?.ToDisplayString() ?? "IOdooRecord";

                sb.AppendLine($"        public {propertyType} {prop.Name}");
                sb.AppendLine("        {");

                // GETTER: Direct cache read (like Odoo Field.__get__)
                if (isComputed)
                {
                    // Computed field - trigger computation if needed (batch pattern)
                    // Odoo pattern: computed fields are lazy-loaded on first access
                    sb.AppendLine("            get");
                    sb.AppendLine("            {");
                    sb.AppendLine(
                        $"                // Computed field - check if recomputation needed OR value not in cache"
                    );
                    sb.AppendLine(
                        $"                // Odoo pattern: compute on first access (lazy) or when dependencies change"
                    );
                    sb.AppendLine(
                        $"                var modelToken = ModelSchema.{className}.ModelToken;"
                    );
                    sb.AppendLine(
                        $"                var fieldToken = ModelSchema.{className}.{prop.Name};"
                    );
                    sb.AppendLine($"                ");
                    sb.AppendLine($"                bool needsCompute = false;");
                    sb.AppendLine($"                if (Env is OdooEnvironment odooEnv)");
                    sb.AppendLine("                {");
                    sb.AppendLine(
                        $"                    // Check if marked for recompute (dependency changed) OR not yet computed"
                    );
                    sb.AppendLine(
                        $"                    needsCompute = odooEnv.ComputeTracker.NeedsRecompute(modelToken, Id, fieldToken)"
                    );
                    sb.AppendLine(
                        $"                        || !Env.Columns.HasValue(modelToken, Id, fieldToken);"
                    );
                    sb.AppendLine("                }");
                    sb.AppendLine($"                else");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    // Fallback: compute if not in cache");
                    sb.AppendLine(
                        $"                    needsCompute = !Env.Columns.HasValue(modelToken, Id, fieldToken);"
                    );
                    sb.AppendLine("                }");
                    sb.AppendLine($"                ");
                    sb.AppendLine($"                if (needsCompute)");
                    sb.AppendLine("                {");
                    sb.AppendLine(
                        $"                    // Trigger recomputation using batch RecordSet pattern (like Odoo)"
                    );
                    sb.AppendLine($"                    if (Env is OdooEnvironment odooEnv2)");
                    sb.AppendLine("                    {");
                    sb.AppendLine(
                        $"                        var recordSet = odooEnv2.CreateRecordSet<{prop.ContainingType.ToDisplayString()}>(new[] {{ Id }});"
                    );
                    sb.AppendLine(
                        $"                        {className}Pipelines.Compute_{prop.Name}(recordSet);"
                    );
                    sb.AppendLine("                    }");
                    sb.AppendLine("                }");
                    sb.AppendLine(
                        $"                return Env.Columns.GetValue<{propertyType}>(modelToken, Id, fieldToken);"
                    );
                    sb.AppendLine("            }");

                    // COMPUTED FIELD SETTER: Check protection and branch (Odoo pattern)
                    // During compute method execution, records are "protected" allowing direct cache write
                    // Outside computation, attempting to set a computed field throws an error
                    sb.AppendLine("            set");
                    sb.AppendLine("            {");
                    sb.AppendLine(
                        $"                // Computed field setter - check if protected (during compute)"
                    );
                    sb.AppendLine($"                if (Env is OdooEnvironment odooEnv && ");
                    sb.AppendLine(
                        $"                    odooEnv.IsProtected(ModelSchema.{className}.{prop.Name}, Id))"
                    );
                    sb.AppendLine("                {");
                    sb.AppendLine(
                        $"                    // Protected path: direct cache write (during computation)"
                    );
                    sb.AppendLine(
                        $"                    // This is the Odoo pattern: compute methods can set computed fields directly"
                    );
                    sb.AppendLine($"                    Env.Columns.SetValue(");
                    sb.AppendLine(
                        $"                        ModelSchema.{className}.ModelToken, Id, ModelSchema.{className}.{prop.Name}, value);"
                    );
                    sb.AppendLine("                }");
                    sb.AppendLine("                else");
                    sb.AppendLine("                {");
                    sb.AppendLine(
                        $"                    // Not protected: computed field with no inverse cannot be written directly"
                    );
                    sb.AppendLine(
                        $"                    // TODO: When [OdooInverse] is implemented, call the inverse method here"
                    );
                    sb.AppendLine($"                    throw new InvalidOperationException(");
                    sb.AppendLine(
                        $"                        $\"Cannot directly write to computed field '{fieldName}' on {model.ModelName}. \" +"
                    );
                    sb.AppendLine(
                        $"                        \"This field is computed and has no inverse method defined. \" +"
                    );
                    sb.AppendLine(
                        $"                        \"Computed fields can only be set within their compute method.\");"
                    );
                    sb.AppendLine("                }");
                    sb.AppendLine("            }");
                }
                else
                {
                    // Regular field - direct cache access (no pipeline overhead)
                    sb.AppendLine(
                        $"            [MethodImpl(MethodImplOptions.AggressiveInlining)]"
                    );
                    sb.AppendLine($"            get => Env.Columns.GetValue<{propertyType}>(");
                    sb.AppendLine(
                        $"                ModelSchema.{className}.ModelToken, Id, ModelSchema.{className}.{prop.Name});"
                    );

                    // SETTER: Delegate to WriteFromDict() pipeline (like Odoo Field.__set__)
                    if (!prop.IsReadOnly)
                    {
                        sb.AppendLine("            set");
                        sb.AppendLine("            {");
                        sb.AppendLine(
                            $"                // Delegate to WriteFromDict() pipeline - Odoo pattern"
                        );
                        sb.AppendLine(
                            $"                var vals = new Dictionary<string, object?> {{ {{ \"{fieldName}\", value }} }};"
                        );
                        sb.AppendLine(
                            $"                {className}Pipelines.WriteFromDict(_handle, vals);"
                        );
                        sb.AppendLine("            }");
                    }
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Override Equals and GetHashCode for identity
            sb.AppendLine("        public override bool Equals(object? obj)");
            sb.AppendLine("        {");
            sb.AppendLine(
                $"            return obj is {className} other && _handle.Id == other._handle.Id && _handle.Model.Token == other._handle.Model.Token;"
            );
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine(
                "        public override int GetHashCode() => HashCode.Combine(_handle.Id, _handle.Model.Token);"
            );
            sb.AppendLine();
            sb.AppendLine(
                $"        public override string ToString() => $\"{model.ModelName}({{Id}})\";"
            );

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Generate UNIFIED Write/Create pipelines - ODOO ALIGNED PATTERN.
        ///
        /// Instead of per-property getter/setter pipelines, we generate:
        /// - Write(handle, vals) - unified write pipeline (like Odoo's BaseModel.write)
        /// - Write_Base(handle, vals) - base implementation that writes to cache
        /// - Create(env, vals) - create pipeline (like Odoo's BaseModel.create)
        /// - Create_Base(env, vals) - base implementation
        /// - Compute_X(handle) - compute method for computed fields
        /// </summary>
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
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine($"using {schemaNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {schemaNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// ODOO-ALIGNED pipeline methods for {model.ModelName}.");
            sb.AppendLine($"    /// ");
            sb.AppendLine(
                $"    /// Pattern: Property setters delegate to Write() which is the single extension point."
            );
            sb.AppendLine(
                $"    /// This mirrors Odoo's design where Field.__set__ calls write() for extensibility."
            );
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public static class {pipelineClass}");
            sb.AppendLine("    {");

            var valuesName = $"{className}Values";
            var handlerName = $"{className}ValuesHandler";

            // ========================================
            // WRITE PIPELINE - TYPED VALUES PRIMARY PATH
            // ========================================
            sb.AppendLine("        #region Write Pipeline (Typed Values - Primary Path)");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Unified write method using typed Values - THE PRIMARY PATH."
            );
            sb.AppendLine(
                "        /// Modules override this to add business logic (validation, onchange, etc.)."
            );
            sb.AppendLine("        /// Mirrors Odoo's BaseModel.write() method.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public static void Write(RecordHandle handle, {valuesName} values)"
            );
            sb.AppendLine("        {");
            sb.AppendLine(
                $"            var pipeline = handle.Env.GetPipeline<Action<RecordHandle, {valuesName}>>(\"{model.ModelName}\", \"write\");"
            );
            sb.AppendLine("            pipeline(handle, values);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Base write implementation using typed Values - HIGH PERFORMANCE PATH."
            );
            sb.AppendLine(
                "        /// Uses ValuesHandler.ApplyToCache for direct typed cache writes (no reflection)."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public static void Write_Base(RecordHandle handle, {valuesName} values)"
            );
            sb.AppendLine("        {");
            sb.AppendLine($"            var modelToken = ModelSchema.{className}.ModelToken;");
            sb.AppendLine();
            sb.AppendLine(
                "            // High-performance typed cache write using generated handler"
            );
            sb.AppendLine(
                $"            {handlerName}.Instance.ApplyToCache(values, handle.Env.Columns, modelToken, handle.Id);"
            );
            sb.AppendLine(
                $"            {handlerName}.Instance.MarkDirty(values, handle.Env.Columns, modelToken, handle.Id);"
            );
            sb.AppendLine();
            sb.AppendLine("            // Trigger recomputation of dependent computed fields");
            sb.AppendLine("            if (handle.Env is OdooEnvironment odooEnv)");
            sb.AppendLine("            {");
            sb.AppendLine(
                $"                {handlerName}.Instance.TriggerModified(values, odooEnv, modelToken, handle.Id);"
            );
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Dictionary-based Write (converts to typed values first)
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Write using dictionary (Python interop) - CONVERTS TO TYPED VALUES."
            );
            sb.AppendLine("        /// Use typed Write() overload for maximum performance.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                "        public static void WriteFromDict(RecordHandle handle, Dictionary<string, object?> vals)"
            );
            sb.AppendLine("        {");
            sb.AppendLine($"            var typedValues = {valuesName}.FromDictionary(vals);");
            sb.AppendLine("            Write(handle, typedValues);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        #endregion");
            sb.AppendLine();

            // ========================================
            // CREATE PIPELINE - TYPED VALUES PRIMARY PATH
            // ========================================
            sb.AppendLine("        #region Create Pipeline (Typed Values - Primary Path)");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Unified create method using typed Values - THE PRIMARY PATH."
            );
            sb.AppendLine(
                "        /// Modules override this to add creation logic (defaults, validation, etc.)."
            );
            sb.AppendLine("        /// Mirrors Odoo's BaseModel.create() method.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public static {className} Create(IEnvironment env, {valuesName} values)"
            );
            sb.AppendLine("        {");
            sb.AppendLine(
                $"            var pipeline = env.GetPipeline<Func<IEnvironment, {valuesName}, {className}>>(\"{model.ModelName}\", \"create\");"
            );
            sb.AppendLine("            return pipeline(env, values);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Base create implementation using typed Values - HIGH PERFORMANCE PATH."
            );
            sb.AppendLine(
                "        /// Uses ValuesHandler.ApplyToCache for direct typed cache writes (no reflection)."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public static {className} Create_Base(IEnvironment env, {valuesName} values)"
            );
            sb.AppendLine("        {");
            sb.AppendLine(
                $"            var newId = env.IdGenerator.NextId(\"{model.ModelName}\");"
            );
            sb.AppendLine($"            var modelToken = ModelSchema.{className}.ModelToken;");
            sb.AppendLine($"            var handle = new RecordHandle(env, newId, modelToken);");
            sb.AppendLine($"            var record = new {className}(handle);");
            sb.AppendLine();
            sb.AppendLine("            // Register in identity map");
            sb.AppendLine("            if (env is OdooEnvironment odooEnv)");
            sb.AppendLine("            {");
            sb.AppendLine(
                "                odooEnv.RegisterInIdentityMap(modelToken.Token, newId, record);"
            );
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine(
                "            // High-performance typed cache write using generated handler"
            );
            sb.AppendLine(
                $"            {handlerName}.Instance.ApplyToCache(values, env.Columns, modelToken, newId);"
            );
            sb.AppendLine(
                $"            {handlerName}.Instance.MarkDirty(values, env.Columns, modelToken, newId);"
            );
            sb.AppendLine();
            sb.AppendLine("            // Trigger recomputation of dependent computed fields");
            sb.AppendLine("            if (env is OdooEnvironment odooEnv2)");
            sb.AppendLine("            {");
            sb.AppendLine(
                $"                {handlerName}.Instance.TriggerModified(values, odooEnv2, modelToken, newId);"
            );
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            return record;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Dictionary-based Create (converts to typed values first)
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Create using dictionary (Python interop) - CONVERTS TO TYPED VALUES."
            );
            sb.AppendLine("        /// Use typed Create() overload for maximum performance.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public static {className} CreateFromDict(IEnvironment env, Dictionary<string, object?> vals)"
            );
            sb.AppendLine("        {");
            sb.AppendLine($"            var typedValues = {valuesName}.FromDictionary(vals);");
            sb.AppendLine("            return Create(env, typedValues);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        #endregion");
            sb.AppendLine();

            // ========================================
            // COMPUTE METHODS - For computed fields (Batch RecordSet pattern like Odoo)
            // ========================================
            var computedProps = model
                .Properties.Where(p =>
                    p.GetAttributes().Any(a => a.AttributeClass?.Name == "OdooComputeAttribute")
                )
                .ToList();

            // Get the primary interface for RecordSet type parameter
            var primaryInterface =
                model.Interfaces.FirstOrDefault()?.ToDisplayString() ?? "IOdooRecord";

            if (computedProps.Any())
            {
                sb.AppendLine("        #region Computed Field Methods (Batch RecordSet Pattern)");
                sb.AppendLine();

                foreach (var prop in computedProps)
                {
                    var propertyType = prop.Type.ToDisplayString();
                    var computeAttr = prop.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "OdooComputeAttribute");

                    var computeMethodName =
                        computeAttr?.ConstructorArguments.Length > 0
                            ? computeAttr.ConstructorArguments[0].Value?.ToString()
                            ?? $"_compute_{prop.Name.ToLower()}"
                            : $"_compute_{prop.Name.ToLower()}";

                    // Use the interface where the property is defined for the RecordSet type
                    // This ensures compatibility with logic methods defined in upstream modules
                    var definingInterface = prop.ContainingType.ToDisplayString();

                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine(
                        $"        /// Trigger computation for {prop.Name} field using batch RecordSet pattern."
                    );
                    sb.AppendLine(
                        $"        /// This mirrors Odoo's pattern: for record in self: record.field = computed_value"
                    );
                    sb.AppendLine($"        /// ");
                    sb.AppendLine(
                        $"        /// Uses Odoo's protection mechanism: records are 'protected' during computation,"
                    );
                    sb.AppendLine(
                        $"        /// allowing the compute method to directly set the computed field value without"
                    );
                    sb.AppendLine(
                        $"        /// triggering the Write pipeline or causing infinite recursion."
                    );
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine(
                        $"        public static void Compute_{prop.Name}(RecordSet<{definingInterface}> self)"
                    );
                    sb.AppendLine("        {");
                    sb.AppendLine(
                        "            // Wrap compute in protection scope (Odoo's env.protecting pattern)"
                    );
                    sb.AppendLine(
                        "            // This allows compute methods to directly set computed field values"
                    );
                    sb.AppendLine("            if (self.Env is OdooEnvironment odooEnv)");
                    sb.AppendLine("            {");
                    sb.AppendLine(
                        $"                using (odooEnv.Protecting(new[] {{ ModelSchema.{className}.{prop.Name} }}, self.Ids))"
                    );
                    sb.AppendLine("                {");
                    sb.AppendLine(
                        $"                    // Call compute method via pipeline - module provides actual logic"
                    );
                    sb.AppendLine(
                        $"                    var pipeline = self.Env.GetPipeline<Action<RecordSet<{definingInterface}>>>("
                    );
                    sb.AppendLine(
                        $"                        \"{model.ModelName}\", \"{computeMethodName}\");"
                    );
                    sb.AppendLine("                    pipeline(self);");
                    sb.AppendLine("                }");
                    sb.AppendLine();
                    sb.AppendLine(
                        "                // Clear the needs-recompute flag for all records in the set"
                    );
                    sb.AppendLine("                foreach (var id in self.Ids)");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    odooEnv.ComputeTracker.ClearRecompute(");
                    sb.AppendLine(
                        $"                        ModelSchema.{className}.ModelToken, id, ModelSchema.{className}.{prop.Name});"
                    );
                    sb.AppendLine("                }");
                    sb.AppendLine("            }");
                    sb.AppendLine("            else");
                    sb.AppendLine("            {");
                    sb.AppendLine(
                        $"                // Fallback without protection (testing scenarios)"
                    );
                    sb.AppendLine(
                        $"                var pipeline = self.Env.GetPipeline<Action<RecordSet<{definingInterface}>>>("
                    );
                    sb.AppendLine(
                        $"                    \"{model.ModelName}\", \"{computeMethodName}\");"
                    );
                    sb.AppendLine("                pipeline(self);");
                    sb.AppendLine("            }");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    // Base compute that does nothing (placeholder for modules to override)
                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine(
                        $"        /// Base compute for {prop.Name} - does nothing by default."
                    );
                    sb.AppendLine(
                        $"        /// Modules provide the actual compute logic via [OdooLogic] attribute."
                    );
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine(
                        $"        public static void Compute_{prop.Name}_Base(RecordSet<{definingInterface}> self)"
                    );
                    sb.AppendLine("        {");
                    sb.AppendLine(
                        "            // No-op base - actual computation provided by module"
                    );
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }

                sb.AppendLine("        #endregion");
                sb.AppendLine();
            }

            // ========================================
            // FIELD ACCESSORS - Direct cache access for reading (kept for backward compatibility)
            // ========================================
            sb.AppendLine("        #region Field Accessors (Direct Cache)");
            sb.AppendLine();

            foreach (var prop in model.Properties)
            {
                var propertyType = prop.Type.ToDisplayString();
                var fieldName = GetOdooFieldName(prop);

                // Direct cache getter (for use in compute methods, etc.)
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine(
                    $"        /// Direct cache read for {prop.Name}. Use for compute methods."
                );
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine(
                    $"        public static {propertyType} Get_{prop.Name}(RecordHandle handle)"
                );
                sb.AppendLine("        {");
                sb.AppendLine($"            return handle.Env.Columns.GetValue<{propertyType}>(");
                sb.AppendLine($"                ModelSchema.{className}.ModelToken,");
                sb.AppendLine($"                handle.Id,");
                sb.AppendLine($"                ModelSchema.{className}.{prop.Name});");
                sb.AppendLine("        }");
                sb.AppendLine();

                // Direct cache setter (for use in compute methods to set values without triggering pipeline)
                if (!prop.IsReadOnly)
                {
                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine(
                        $"        /// Direct cache write for {prop.Name}. Use in compute methods to avoid triggering write pipeline."
                    );
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine(
                        $"        public static void Set_{prop.Name}_Direct(RecordHandle handle, {propertyType} value)"
                    );
                    sb.AppendLine("        {");
                    sb.AppendLine($"            handle.Env.Columns.SetValue(");
                    sb.AppendLine($"                ModelSchema.{className}.ModelToken,");
                    sb.AppendLine($"                handle.Id,");
                    sb.AppendLine($"                ModelSchema.{className}.{prop.Name},");
                    sb.AppendLine($"                value);");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("        #endregion");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Generate Values class with RecordValueField&lt;T&gt; properties.
        /// Implements IRecordValues&lt;TRecord&gt; for type-safe record creation.
        /// </summary>
        private string GenerateValuesClass(UnifiedModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var className = model.ClassName;
            var valuesName = $"{className}Values";
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";
            var primaryInterface =
                model.Interfaces.FirstOrDefault()?.ToDisplayString() ?? "IOdooRecord";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine($"using {schemaNamespace};");

            // Add using statements for all interface namespaces
            foreach (var iface in model.Interfaces)
            {
                var ns = iface.ContainingNamespace?.ToDisplayString();
                if (!string.IsNullOrEmpty(ns) && ns != schemaNamespace)
                {
                    sb.AppendLine($"using {ns};");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {schemaNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Typed values for creating/updating {model.ModelName} records.");
            sb.AppendLine(
                $"    /// Uses RecordValueField&lt;T&gt; to track which fields are explicitly set."
            );
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    /// <remarks>");
            sb.AppendLine(
                $"    /// Usage: env.Create(new {valuesName} {{ Name = \"Alice\", IsCompany = true }})"
            );
            sb.AppendLine(
                $"    /// The implicit conversion from T to RecordValueField&lt;T&gt; automatically marks fields as set."
            );
            sb.AppendLine($"    /// </remarks>");
            sb.AppendLine(
                $"    public sealed class {valuesName} : IRecordValues<{primaryInterface}>"
            );
            sb.AppendLine("    {");
            sb.AppendLine($"        /// <summary>Gets the Odoo model name.</summary>");
            sb.AppendLine($"        public string ModelName => \"{model.ModelName}\";");
            sb.AppendLine();

            // Generate RecordValueField<T> properties for each writable field
            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly)
                    continue;

                var propertyType = prop.Type.ToDisplayString();
                var fieldName = GetOdooFieldName(prop);

                sb.AppendLine($"        /// <summary>Field: {fieldName}</summary>");
                sb.AppendLine(
                    $"        public RecordValueField<{propertyType}> {prop.Name} {{ get; set; }} = new();"
                );
                sb.AppendLine();
            }

            // Generate ToDictionary method
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Convert to dictionary for pipeline compatibility.");
            sb.AppendLine("        /// Only includes fields that have been explicitly set.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public Dictionary<string, object?> ToDictionary()");
            sb.AppendLine("        {");
            sb.AppendLine("            var dict = new Dictionary<string, object?>();");

            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly)
                    continue;
                var fieldName = GetOdooFieldName(prop);
                sb.AppendLine(
                    $"            if ({prop.Name}.IsSet) dict[\"{fieldName}\"] = {prop.Name}.Value;"
                );
            }

            sb.AppendLine("            return dict;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generate FromDictionary static method
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Create a Values instance from a dictionary (Python interop)."
            );
            sb.AppendLine(
                "        /// Used by dictionary-based Create/Write to convert to typed values."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public static {valuesName} FromDictionary(Dictionary<string, object?> dict)"
            );
            sb.AppendLine("        {");
            sb.AppendLine($"            var values = new {valuesName}();");

            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly)
                    continue;
                var fieldName = GetOdooFieldName(prop);
                var propertyType = prop.Type.ToDisplayString();

                sb.AppendLine(
                    $"            if (dict.TryGetValue(\"{fieldName}\", out var {ToCamelCase(prop.Name)}Val) && {ToCamelCase(prop.Name)}Val != null)"
                );
                sb.AppendLine(
                    $"                values.{prop.Name} = ({propertyType}){ToCamelCase(prop.Name)}Val;"
                );
            }

            sb.AppendLine("            return values;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generate GetSetFields method
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Get the names of fields that have been explicitly set.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public IEnumerable<string> GetSetFields()");
            sb.AppendLine("        {");

            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly)
                    continue;
                var fieldName = GetOdooFieldName(prop);
                sb.AppendLine($"            if ({prop.Name}.IsSet) yield return \"{fieldName}\";");
            }

            // Ensure method compiles even if there are no properties (no yield statements emitted)
            sb.AppendLine("            yield break;");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Generate ValuesHandler class for no-reflection cache operations.
        /// Provides high-performance typed access to field values.
        /// </summary>
        private string GenerateValuesHandler(UnifiedModelInfo model, string safeAssemblyName)
        {
            var sb = new StringBuilder();
            var className = model.ClassName;
            var valuesName = $"{className}Values";
            var handlerName = $"{className}ValuesHandler";
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine($"using {schemaNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {schemaNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// High-performance handler for {valuesName} cache operations.");
            sb.AppendLine($"    /// Avoids reflection by using generated code for field access.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine(
                $"    public sealed class {handlerName} : IRecordValuesHandler<{valuesName}>"
            );
            sb.AppendLine("    {");
            sb.AppendLine($"        /// <summary>Singleton instance.</summary>");
            sb.AppendLine($"        public static readonly {handlerName} Instance = new();");
            sb.AppendLine();
            sb.AppendLine($"        private {handlerName}() {{ }}");
            sb.AppendLine();

            // ApplyToCache - single record
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Apply values to cache for a single record.");
            sb.AppendLine(
                "        /// Only sets fields that have been explicitly set in the values object."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public void ApplyToCache({valuesName} values, IColumnarCache cache, ModelHandle model, RecordId recordId)"
            );
            sb.AppendLine("        {");

            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly)
                    continue;
                var propertyType = prop.Type.ToDisplayString();
                sb.AppendLine($"            if (values.{prop.Name}.IsSet)");
                sb.AppendLine(
                    $"                cache.SetValue<{propertyType}>(model, recordId, ModelSchema.{className}.{prop.Name}, values.{prop.Name}.Value!);"
                );
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            // ApplyToCacheBatch - multiple values, multiple records (1:1)
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Apply multiple values to cache for multiple records (1:1 mapping)."
            );
            sb.AppendLine(
                "        /// Each values object corresponds to one record ID at the same index."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public void ApplyToCacheBatch(IEnumerable<{valuesName}> valuesCollection, IColumnarCache cache, ModelHandle model, RecordId[] recordIds)"
            );
            sb.AppendLine("        {");
            sb.AppendLine("            var valuesList = valuesCollection.ToList();");
            sb.AppendLine("            if (valuesList.Count != recordIds.Length)");
            sb.AppendLine(
                "                throw new ArgumentException(\"Values count must match record IDs count\");"
            );
            sb.AppendLine();
            sb.AppendLine("            for (int i = 0; i < recordIds.Length; i++)");
            sb.AppendLine("            {");
            sb.AppendLine(
                "                ApplyToCache(valuesList[i], cache, model, recordIds[i]);"
            );
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ApplyToCacheBulk - single values, multiple records (Odoo write pattern)
            sb.AppendLine("        /// <summary>");
            sb.AppendLine(
                "        /// Apply same values to multiple records (Odoo write pattern)."
            );
            sb.AppendLine(
                "        /// Like: records.write(vals) - same values applied to all records."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public void ApplyToCacheBulk({valuesName} values, IColumnarCache cache, ModelHandle model, RecordId[] recordIds)"
            );
            sb.AppendLine("        {");
            sb.AppendLine("            foreach (var recordId in recordIds)");
            sb.AppendLine("            {");
            sb.AppendLine("                ApplyToCache(values, cache, model, recordId);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // MarkDirty - mark fields as dirty
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Mark set fields as dirty for a record.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public void MarkDirty({valuesName} values, IColumnarCache cache, ModelHandle model, RecordId recordId)"
            );
            sb.AppendLine("        {");

            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly)
                    continue;
                sb.AppendLine($"            if (values.{prop.Name}.IsSet)");
                sb.AppendLine(
                    $"                cache.MarkDirty(model, recordId, ModelSchema.{className}.{prop.Name});"
                );
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            // GetSetFieldHandles - get field handles for set fields
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Get field handles for all set fields.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public IEnumerable<FieldHandle> GetSetFieldHandles({valuesName} values)"
            );
            sb.AppendLine("        {");

            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly)
                    continue;
                sb.AppendLine(
                    $"            if (values.{prop.Name}.IsSet) yield return ModelSchema.{className}.{prop.Name};"
                );
            }

            // Ensure iterator compiles even if no properties were emitted
            sb.AppendLine("            yield break;");

            sb.AppendLine("        }");
            sb.AppendLine();

            // TriggerModified - trigger Modified for all set fields on a single record
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Trigger Modified for all set fields on a single record.");
            sb.AppendLine(
                "        /// Called after applying values to trigger recomputation of dependent fields."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public void TriggerModified({valuesName} values, OdooEnvironment env, ModelHandle model, RecordId recordId)"
            );
            sb.AppendLine("        {");

            foreach (var prop in model.Properties)
            {
                if (prop.IsReadOnly)
                    continue;
                sb.AppendLine($"            if (values.{prop.Name}.IsSet)");
                sb.AppendLine(
                    $"                env.Modified(model, recordId, ModelSchema.{className}.{prop.Name});"
                );
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            // TriggerModifiedBatch - trigger Modified for all set fields on multiple records
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Trigger Modified for all set fields on multiple records.");
            sb.AppendLine(
                "        /// More efficient than calling TriggerModified for each record."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine(
                $"        public void TriggerModifiedBatch({valuesName} values, OdooEnvironment env, ModelHandle model, RecordId[] recordIds)"
            );
            sb.AppendLine("        {");
            sb.AppendLine("            foreach (var recordId in recordIds)");
            sb.AppendLine("            {");
            sb.AppendLine("                TriggerModified(values, env, model, recordId);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string GenerateEnvironmentExtensions(
            List<UnifiedModelInfo> models,
            string safeAssemblyName
        )
        {
            var sb = new StringBuilder();
            var schemaNamespace = $"Odoo.Generated.{safeAssemblyName}";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
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
            sb.AppendLine(
                "    /// Use env.GetRecord&lt;T&gt;(id) and env.GetRecords&lt;T&gt;(ids) for record access."
            );
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class OdooEnvironmentExtensions");
            sb.AppendLine("    {");

            foreach (var model in models)
            {
                var className = model.ClassName;
                var valuesName = $"{className}Values";
                var pipelineClass = $"{className}Pipelines";
                var primaryInterface = model.Interfaces.FirstOrDefault();

                // Type-safe Create using Values class with RecordValueField<T>
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine(
                    $"        /// Create a new {model.ModelName} record using typed Values class."
                );
                sb.AppendLine(
                    $"        /// Uses RecordValueField&lt;T&gt;.IsSet to track which fields are explicitly set."
                );
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine(
                    $"        public static {primaryInterface?.ToDisplayString() ?? "IOdooRecord"} Create("
                );
                sb.AppendLine($"            this IEnvironment env,");
                sb.AppendLine($"            {valuesName} values)");
                sb.AppendLine("        {");
                sb.AppendLine(
                    $"            // Direct typed call - HIGH PERFORMANCE PATH (no dictionary conversion)"
                );
                sb.AppendLine($"            return {pipelineClass}.Create(env, values);");
                sb.AppendLine("        }");
                sb.AppendLine();

                // Batch Create - named CreateBatch to avoid ambiguity with single Create
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine(
                    $"        /// Create multiple {model.ModelName} records (batch operation)."
                );
                sb.AppendLine(
                    $"        /// Use CreateBatch for multiple records: env.CreateBatch(new[] {{ new {valuesName} {{ ... }}, ... }})"
                );
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine(
                    $"        public static RecordSet<{primaryInterface?.ToDisplayString() ?? "IOdooRecord"}> CreateBatch("
                );
                sb.AppendLine($"            this IEnvironment env,");
                sb.AppendLine($"            IEnumerable<{valuesName}> valuesCollection)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var ids = new List<RecordId>();");
                sb.AppendLine($"            foreach (var values in valuesCollection)");
                sb.AppendLine("            {");
                sb.AppendLine($"                var record = env.Create(values);");
                sb.AppendLine($"                ids.Add(record.Id);");
                sb.AppendLine("            }");
                sb.AppendLine(
                    $"            return env.CreateRecordSet<{primaryInterface?.ToDisplayString() ?? "IOdooRecord"}>(ids.ToArray());"
                );
                sb.AppendLine("        }");
                sb.AppendLine();

                // Dictionary-based Create for Python integration
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine(
                    $"        /// Create a new {model.ModelName} record using dictionary (Python-style)."
                );
                sb.AppendLine(
                    $"        /// Converts to typed Values and calls the typed Create pipeline."
                );
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static {className} Create{className}(");
                sb.AppendLine($"            this IEnvironment env,");
                sb.AppendLine($"            Dictionary<string, object?> vals)");
                sb.AppendLine("        {");
                sb.AppendLine($"            return {pipelineClass}.CreateFromDict(env, vals);");
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
                    if (
                        member is IPropertySymbol property
                        && property.Name != "Id"
                        && property.Name != "Env"
                    )
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
            var attribute = interfaceSymbol
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "OdooModelAttribute");

            if (attribute?.ConstructorArguments.Length > 0)
            {
                return attribute.ConstructorArguments[0].Value?.ToString() ?? "";
            }

            return "";
        }

        private string GetOdooFieldName(IPropertySymbol property)
        {
            var attribute = property
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "OdooFieldAttribute");

            if (attribute?.ConstructorArguments.Length > 0)
            {
                return attribute.ConstructorArguments[0].Value?.ToString()
                    ?? property.Name.ToLower();
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
        private List<INamedTypeSymbol> CollectAllOdooInterfaces(
            Compilation compilation,
            OdooModelSyntaxReceiver receiver
        )
        {
            var result = new List<INamedTypeSymbol>();
            var processed = new HashSet<string>(StringComparer.Ordinal);

            // 1. Collect from current compilation (local interfaces)
            foreach (var interfaceDecl in receiver.InterfacesToProcess)
            {
                var model = compilation.GetSemanticModel(interfaceDecl.SyntaxTree);
                var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl);

                if (interfaceSymbol == null)
                    continue;

                var modelName = GetOdooModelName(interfaceSymbol);
                if (string.IsNullOrEmpty(modelName))
                    continue;

                var key = interfaceSymbol.ToDisplayString();
                if (processed.Add(key))
                {
                    result.Add(interfaceSymbol);
                }
            }

            // 2. Collect from referenced assemblies
            foreach (var reference in compilation.References)
            {
                var assemblySymbol =
                    compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                if (assemblySymbol == null)
                    continue;

                CollectOdooInterfacesFromNamespace(
                    assemblySymbol.GlobalNamespace,
                    result,
                    processed
                );
            }

            return result;
        }

        /// <summary>
        /// Recursively scan a namespace for [OdooModel] interfaces.
        /// </summary>
        private void CollectOdooInterfacesFromNamespace(
            INamespaceSymbol ns,
            List<INamedTypeSymbol> result,
            HashSet<string> processed
        )
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
        private Dictionary<string, List<INamedTypeSymbol>> GroupInterfacesByModel(
            List<INamedTypeSymbol> interfaces
        )
        {
            var groups = new Dictionary<string, List<INamedTypeSymbol>>();

            foreach (var iface in interfaces)
            {
                var modelName = GetOdooModelName(iface);
                if (string.IsNullOrEmpty(modelName))
                    continue;

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
            var className = string.Join("", parts.Select(p => char.ToUpper(p[0]) + p.Substring(1)));

            return className;
        }

        private string GenerateModuleRegistrar(
            List<LogicMethodInfo> methods,
            List<UnifiedModelInfo> models,
            string? assemblyName
        )
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
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine($"namespace Odoo.Generated.{safeAssemblyName}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Module registrar for unified wrappers.");
            sb.AppendLine(
                "    /// Registers factories that create class-based wrappers with identity map support."
            );
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public class ModuleRegistrar : IModuleRegistrar");
            sb.AppendLine("    {");

            // RegisterPipelines method - ODOO ALIGNED: Register unified Write/Create pipelines
            sb.AppendLine("        public void RegisterPipelines(IPipelineBuilder builder)");
            sb.AppendLine("        {");

            // Register UNIFIED Write/Create Pipelines (ODOO PATTERN)
            foreach (var model in models)
            {
                var className = model.ClassName;
                var pipelineClass = $"{className}Pipelines";

                var valuesName = $"{className}Values";

                sb.AppendLine();
                sb.AppendLine(
                    $"            // {model.ModelName} - TYPED VALUES pipelines (high-performance path)"
                );

                // Register Write pipeline base with TYPED VALUES
                sb.AppendLine(
                    $"            builder.RegisterBase(\"{model.ModelName}\", \"write\", "
                );
                sb.AppendLine(
                    $"                (Action<RecordHandle, {valuesName}>){pipelineClass}.Write_Base);"
                );

                // Register Create pipeline base with TYPED VALUES
                sb.AppendLine(
                    $"            builder.RegisterBase(\"{model.ModelName}\", \"create\", "
                );
                sb.AppendLine(
                    $"                (Func<IEnvironment, {valuesName}, {className}>){pipelineClass}.Create_Base);"
                );

                // Register compute pipelines for computed fields (batch RecordSet pattern)
                var computedProps = model
                    .Properties.Where(p =>
                        p.GetAttributes().Any(a => a.AttributeClass?.Name == "OdooComputeAttribute")
                    )
                    .ToList();

                // Get the primary interface for RecordSet type parameter
                var primaryInterfaceForReg =
                    model.Interfaces.FirstOrDefault()?.ToDisplayString() ?? "IOdooRecord";

                foreach (var prop in computedProps)
                {
                    var computeAttr = prop.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "OdooComputeAttribute");

                    var computeMethodName =
                        computeAttr?.ConstructorArguments.Length > 0
                            ? computeAttr.ConstructorArguments[0].Value?.ToString()
                            ?? $"_compute_{prop.Name.ToLower()}"
                            : $"_compute_{prop.Name.ToLower()}";

                    // Use the interface where the property is defined
                    var definingInterface = prop.ContainingType.ToDisplayString();

                    sb.AppendLine(
                        $"            builder.RegisterDefaultBase(\"{model.ModelName}\", \"{computeMethodName}\", "
                    );
                    sb.AppendLine(
                        $"                (Action<RecordSet<{definingInterface}>>){pipelineClass}.Compute_{prop.Name}_Base);"
                    );
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
                    sb.AppendLine(
                        $"            builder.RegisterOverride(\"{method.ModelName}\", \"{method.MethodName}\", 10, "
                    );
                    sb.AppendLine(
                        $"                ({GetDelegateCast(method.MethodSymbol)}){containingType}.{methodName});"
                    );
                }
                else
                {
                    // Register Base
                    sb.AppendLine(
                        $"            builder.RegisterBase(\"{method.ModelName}\", \"{method.MethodName}\", "
                    );
                    sb.AppendLine(
                        $"                ({GetDelegateCast(method.MethodSymbol)}){containingType}.{methodName});"
                    );
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            // RegisterFactories method - creates class instances with constructor
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Register factories for unified wrapper classes.");
            sb.AppendLine(
                "        /// These factories create class instances that support identity map."
            );
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public void RegisterFactories(ModelRegistry modelRegistry)");
            sb.AppendLine("        {");

            foreach (var model in models)
            {
                var className = model.ClassName;

                sb.AppendLine(
                    $"            // Factory for {model.ModelName} - unified wrapper implementing {model.Interfaces.Count} interface(s)"
                );
                sb.AppendLine($"            modelRegistry.RegisterFactory(\"{model.ModelName}\", ");
                sb.AppendLine(
                    $"                (env, id) => new {className}(new RecordHandle(env, id, ModelSchema.{className}.ModelToken)));"
                );
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
            // Track fully qualified delegate names to avoid duplicates
            var generatedDelegates = new HashSet<string>();

            foreach (var method in methods)
            {
                var modelPart = method.ModelName.Replace(".", "");
                var ns = $"Odoo.Generated.{modelPart}.Super";

                // Let's look at the 'super' parameter if it exists
                var superParam = method.MethodSymbol.Parameters.FirstOrDefault(p =>
                    p.Name == "super"
                );

                string delegateName;
                string delegateSignature;

                if (superParam != null)
                {
                    // It's an override - check if it uses a standard delegate type (Action, Func)
                    var superTypeName = superParam.Type.Name;

                    // Skip generating delegates for standard System types
                    if (superTypeName == "Action" || superTypeName == "Func")
                    {
                        // Standard delegate - don't generate, it's already in System namespace
                        continue;
                    }

                    // It's a custom delegate type - generate it
                    var parameters = method
                        .MethodSymbol.Parameters.Where(p => p.Name != "super")
                        .Select(p => $"{p.Type.ToDisplayString()} {p.Name}");

                    var returnType = method.MethodSymbol.ReturnType.ToDisplayString();
                    delegateName = superTypeName;
                    delegateSignature =
                        $"{returnType} {delegateName}({string.Join(", ", parameters)})";
                }
                else
                {
                    // It's a base method. Generate a delegate for it so overrides can use it.
                    var parameters = method.MethodSymbol.Parameters.Select(p =>
                        $"{p.Type.ToDisplayString()} {p.Name}"
                    );
                    var returnType = method.MethodSymbol.ReturnType.ToDisplayString();

                    // Use the original method name as delegate name (for consistency with LogicExtensions)
                    delegateName = method.MethodName;
                    delegateSignature =
                        $"{returnType} {delegateName}({string.Join(", ", parameters)})";
                }

                // Create a unique key for this delegate
                var fullDelegateName = $"{ns}.{delegateName}";
                if (generatedDelegates.Contains(fullDelegateName))
                {
                    continue; // Already generated
                }
                generatedDelegates.Add(fullDelegateName);

                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
                sb.AppendLine($"    public delegate {delegateSignature};");
                sb.AppendLine("}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string GenerateLogicExtensions(List<LogicMethodInfo> methods)
        {
            // We need the assembly name to make the namespace unique
            // Since we don't have it passed here easily, we'll extract it from the first method's symbol
            var assemblyName =
                methods.FirstOrDefault()?.MethodSymbol.ContainingAssembly.Name ?? "App";
            var safeAssemblyName = assemblyName.Replace(".", "");

            var sb = new StringBuilder();
            sb.AppendLine("using Odoo.Core;");
            sb.AppendLine("using Odoo.Core.Pipeline;");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");

            // Add using statements for model namespaces
            var namespaces = methods
                .Select(m => m.MethodSymbol.ContainingNamespace.ToDisplayString())
                .Distinct();
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
                if (processed.Contains(key))
                    continue;
                processed.Add(key);

                // Generate extension method on RecordSet<T>
                // We need to know T. The first parameter is usually RecordSet<T>.
                var firstParam = method.MethodSymbol.Parameters.FirstOrDefault();
                // Check if type is RecordSet<T> by checking name and generic arguments
                if (
                    firstParam != null
                    && firstParam.Type is INamedTypeSymbol namedType
                    && namedType.IsGenericType
                    && namedType.Name == "RecordSet"
                )
                {
                    if (namedType.TypeArguments.Length == 0)
                        continue;

                    var recordType = namedType.TypeArguments[0].ToDisplayString();
                    // Skip if type is invalid or an error type
                    if (
                        string.IsNullOrWhiteSpace(recordType)
                        || recordType.Contains("?")
                        || recordType == "object"
                    )
                        continue;

                    // Method signature
                    var parametersArray = method
                        .MethodSymbol.Parameters.Where(p => p.Name != "super" && p.Name != "self") // Skip self (extension target) and super
                        .Select(p => $"{p.Type.ToDisplayString()} {p.Name}")
                        .ToArray();

                    var paramNames = method
                        .MethodSymbol.Parameters.Where(p => p.Name != "super" && p.Name != "self")
                        .Select(p => p.Name)
                        .ToArray();

                    var returnType = method.MethodSymbol.ReturnType.ToDisplayString();
                    var isVoid = returnType == "void";

                    // Generate method signature - handle empty parameters correctly
                    var parameterSignature =
                        parametersArray.Length > 0
                            ? $"this RecordSet<{recordType}> self, {string.Join(", ", parametersArray)}"
                            : $"this RecordSet<{recordType}> self";

                    sb.AppendLine(
                        $"        public static {returnType} {method.MethodName}({parameterSignature})"
                    );
                    sb.AppendLine("        {");

                    // Determine delegate type
                    var modelPart = method.ModelName.Replace(".", "");
                    var superParam = method.MethodSymbol.Parameters.FirstOrDefault(p =>
                        p.Name == "super"
                    );

                    string delegateType;
                    if (superParam != null)
                    {
                        var superTypeName = superParam.Type.Name;
                        // For standard delegates, use the full type from the parameter
                        if (superTypeName == "Action" || superTypeName == "Func")
                        {
                            // Use the full generic type display string
                            delegateType = superParam.Type.ToDisplayString();
                        }
                        else
                        {
                            // Custom delegate - use our generated namespace
                            delegateType = $"Odoo.Generated.{modelPart}.Super.{superTypeName}";
                        }
                    }
                    else
                    {
                        // Base method without super - use our generated delegate
                        delegateType = $"Odoo.Generated.{modelPart}.Super.{method.MethodName}";
                    }

                    // Get typed pipeline
                    sb.AppendLine(
                        $"            var pipeline = self.Env.Methods.GetPipeline<{delegateType}>(\"{method.ModelName}\", \"{method.MethodName}\");"
                    );

                    sb.Append($"            ");
                    if (!isVoid)
                        sb.Append("return ");

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
            if (
                syntaxNode is InterfaceDeclarationSyntax interfaceDecl
                && interfaceDecl.AttributeLists.Count > 0
            )
            {
                InterfacesToProcess.Add(interfaceDecl);
            }

            if (
                syntaxNode is MethodDeclarationSyntax methodDecl
                && methodDecl.AttributeLists.Count > 0
            )
            {
                // Check for OdooLogic attribute
                if (
                    methodDecl
                        .AttributeLists.SelectMany(al => al.Attributes)
                        .Any(a => a.Name.ToString().Contains("OdooLogic"))
                )
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
