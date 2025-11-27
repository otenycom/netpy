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

        public void ScanAssembly(Assembly assembly)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsInterface && t.GetCustomAttribute<OdooModelAttribute>() != null);

            foreach (var type in types)
            {
                var modelAttr = type.GetCustomAttribute<OdooModelAttribute>()!;
                var modelName = modelAttr.ModelName;

                if (!_models.TryGetValue(modelName, out var schema))
                {
                    // Use stable hash for model token
                    var modelToken = new ModelHandle(GetStableHashCode(modelName));
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
                            // Use stable hash for field token
                            var fieldToken = new FieldHandle(GetStableHashCode($"{modelName}.{fieldName}"));
                            
                            var fieldSchema = new FieldSchema(
                                fieldName,
                                prop.PropertyType,
                                !prop.CanWrite,
                                type,
                                fieldToken
                            );
                            schema.Fields[fieldName] = fieldSchema;
                        }
                    }
                }
            }
        }

        public ModelRegistry Build()
        {
            return new ModelRegistry(_models);
        }

        // Deterministic hash code for stable tokens across compilations/runs
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
    }
}