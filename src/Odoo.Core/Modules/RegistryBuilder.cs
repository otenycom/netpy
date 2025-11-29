using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Odoo.Core;

namespace Odoo.Core.Modules
{
    public class RegistryBuilder
    {
        private readonly Dictionary<string, ModelSchema> _models = new();
        private readonly Dictionary<
            (ModelHandle, FieldHandle),
            List<(ModelHandle, FieldHandle)>
        > _dependencies = new();

        public void ScanAssembly(Assembly assembly)
        {
            var types = assembly
                .GetTypes()
                .Where(t => t.IsInterface && t.GetCustomAttribute<OdooModelAttribute>() != null);

            foreach (var type in types)
            {
                var modelAttr = type.GetCustomAttribute<OdooModelAttribute>()!;
                var modelName = modelAttr.ModelName;

                if (!_models.TryGetValue(modelName, out var schema))
                {
                    // Try to find generated token first
                    var modelToken = ResolveModelToken(assembly, modelName);
                    if (modelToken.Token == 0L)
                    {
                        // Fallback to stable hash
                        modelToken = new ModelHandle(StableHash.GetStableHashCode(modelName));
                    }

                    schema = new ModelSchema(modelName, modelToken);
                    _models[modelName] = schema;
                }

                schema.ContributingInterfaces.Add(type);

                foreach (var prop in type.GetProperties())
                {
                    var fieldAttr = prop.GetCustomAttribute<OdooFieldAttribute>();
                    if (fieldAttr != null)
                    {
                        var fieldName = fieldAttr.TechnicalName;

                        // Only add if not already present (first one wins? or merge?)
                        // Usually base fields are defined first if we load in order.
                        // If an override re-defines a field, we might want to update metadata.
                        // For now, we'll assume the first definition stands, or we can overwrite.

                        if (!schema.Fields.ContainsKey(fieldName))
                        {
                            // Try to find generated token first
                            var fieldToken = ResolveFieldToken(assembly, modelName, prop.Name);
                            if (fieldToken.Token == 0L)
                            {
                                // Fallback to stable hash
                                fieldToken = new FieldHandle(
                                    StableHash.GetStableHashCode($"{modelName}.{fieldName}")
                                );
                            }

                            var fieldSchema = new FieldSchema(
                                fieldName,
                                prop.PropertyType,
                                !prop.CanWrite,
                                type,
                                fieldToken
                            );
                            schema.Fields[fieldName] = fieldSchema;
                        }

                        // Register dependencies
                        var dependsAttr = prop.GetCustomAttribute<OdooDependsAttribute>();
                        if (dependsAttr != null)
                        {
                            var dependentFieldToken = schema.Fields[fieldName].Token;
                            var dependentModelToken = schema.Token;

                            foreach (var sourceFieldName in dependsAttr.Fields)
                            {
                                // TODO: Handle dot notation for related fields
                                if (!sourceFieldName.Contains("."))
                                {
                                    // Create source field handle (stable hash of "model.field")
                                    var sourceFieldHandle = new FieldHandle(
                                        StableHash.GetStableHashCode(
                                            $"{modelName}.{sourceFieldName}"
                                        ),
                                        sourceFieldName
                                    );
                                    var sourceKey = (dependentModelToken, sourceFieldHandle);

                                    if (!_dependencies.TryGetValue(sourceKey, out var dependents))
                                    {
                                        dependents = new List<(ModelHandle, FieldHandle)>();
                                        _dependencies[sourceKey] = dependents;
                                    }

                                    var dep = (dependentModelToken, dependentFieldToken);
                                    if (!dependents.Contains(dep))
                                    {
                                        dependents.Add(dep);
                                        Console.WriteLine(
                                            $"[Registry] Registered dependency: {modelName}.{sourceFieldName} ({sourceFieldHandle.Token}) -> {modelName}.{fieldName} ({dependentFieldToken.Token})"
                                        );
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public ModelRegistry Build()
        {
            return new ModelRegistry(_models, _dependencies);
        }

        private ModelHandle ResolveModelToken(Assembly assembly, string modelName)
        {
            // Try to find Odoo.Generated.{SafeAssemblyName}.ModelSchema
            var safeName = assembly.GetName().Name!.Replace(".", "");
            var schemaType = assembly.GetType($"Odoo.Generated.{safeName}.ModelSchema");

            if (schemaType != null)
            {
                // Find nested class for model
                // The generator uses the interface name minus 'I' as the class name
                // But here we only have modelName (e.g. "res.partner")
                // We need to map modelName back to class name, or search all nested types

                foreach (var nestedType in schemaType.GetNestedTypes())
                {
                    var nameField = nestedType.GetField("ModelName");
                    if (nameField != null && nameField.GetValue(null) as string == modelName)
                    {
                        var tokenField = nestedType.GetField("ModelToken");
                        if (tokenField != null)
                        {
                            return (ModelHandle)tokenField.GetValue(null)!;
                        }
                    }
                }
            }

            return default;
        }

        private FieldHandle ResolveFieldToken(
            Assembly assembly,
            string modelName,
            string propertyName
        )
        {
            var safeName = assembly.GetName().Name!.Replace(".", "");
            var schemaType = assembly.GetType($"Odoo.Generated.{safeName}.ModelSchema");

            if (schemaType != null)
            {
                foreach (var nestedType in schemaType.GetNestedTypes())
                {
                    var nameField = nestedType.GetField("ModelName");
                    if (nameField != null && nameField.GetValue(null) as string == modelName)
                    {
                        var fieldTokenField = nestedType.GetField(propertyName);
                        if (fieldTokenField != null)
                        {
                            return (FieldHandle)fieldTokenField.GetValue(null)!;
                        }
                    }
                }
            }

            return default;
        }
    }
}
