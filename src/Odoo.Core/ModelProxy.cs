using System;
using System.Collections.Generic;
using System.Linq;
using Odoo.Core.Modules;

namespace Odoo.Core
{
    /// <summary>
    /// Proxy object for Pythonic model access (e.g. env["res.partner"]).
    /// Enables dynamic record creation and searching.
    /// </summary>
    public class ModelProxy
    {
        private readonly IEnvironment _env;
        private readonly string _modelName;
        private readonly ModelSchema _schema;

        public ModelProxy(IEnvironment env, string modelName, ModelSchema schema)
        {
            _env = env;
            _modelName = modelName;
            _schema = schema;
        }

        /// <summary>
        /// Create a new record using an anonymous object or dictionary.
        /// Example: env["res.partner"].Create(new { name = "Test" })
        /// </summary>
        public IOdooRecord Create(object values)
        {
            Dictionary<string, object?> dict;

            if (values is Dictionary<string, object?> d)
            {
                dict = d;
            }
            else
            {
                // Convert anonymous object to dictionary
                dict = values.GetType()
                    .GetProperties()
                    .ToDictionary(
                        p => ToSnakeCase(p.Name),
                        p => p.GetValue(values)
                    );
            }

            return CreateFromDictionary(dict);
        }

        private IOdooRecord CreateFromDictionary(Dictionary<string, object?> values)
        {
            var newId = _env.IdGenerator.NextId(_modelName);
            
            // We need to resolve field names to tokens
            // Since we don't have the FieldNameResolver exposed on IEnvironment yet,
            // we'll do a manual lookup via schema for now, or assume OdooEnvironment has it.
            // Let's assume we can access the registry via the schema or environment.
            
            // For this implementation, we'll use the schema directly since we have it.
            
            foreach (var (fieldName, value) in values)
            {
                if (value == null) continue;

                if (_schema.Fields.TryGetValue(fieldName, out var fieldSchema))
                {
                    // We need to handle type conversion if necessary, but for now assume correct types
                    // We need to invoke SetValue generically or with object
                    // Since SetValue is generic, we need reflection or a non-generic overload
                    // The IColumnarCache interface has generic SetValue<T>
                    
                    // Use reflection to call SetValue<T>
                    var method = _env.Columns.GetType().GetMethod("SetValue");
                    var genericMethod = method!.MakeGenericMethod(fieldSchema.FieldType);
                    
                    genericMethod.Invoke(_env.Columns, new object[] { 
                        _schema.Token, 
                        newId, 
                        fieldSchema.Token, 
                        value 
                    });
                }
                else
                {
                    throw new KeyNotFoundException($"Field '{fieldName}' not found on model '{_modelName}'");
                }
            }

            // Create the record instance
            // We need to find the factory. OdooEnvironment has GetOrCreateFactory but it's private/internal logic
            // exposed via GetModel<T>.
            // But we want IOdooRecord here.
            
            // We can use the ModelRegistry to get the factory
            if (_env is OdooEnvironment odooEnv)
            {
                // We need access to the registry. OdooEnvironment has it private.
                // But we can use the public RegisterFactory method... wait, we need to GET it.
                
                // Let's add a public method to OdooEnvironment to get a factory by name, 
                // or use the one we added to ModelRegistry.
                
                // For now, let's assume we can get it from the registry if we had access.
                // But ModelProxy doesn't have access to Registry directly unless passed in.
                
                // Let's rely on the fact that we can create a generic RecordSet if we knew the type.
                // But we don't know the type T here easily (it's in ContributingInterfaces).
                
                // Let's use the first contributing interface as the main type
                var mainType = _schema.ContributingInterfaces.FirstOrDefault();
                if (mainType != null)
                {
                    // Use reflection to call CreateRecordSet<T>
                    var createMethod = typeof(IEnvironment).GetMethod("CreateRecordSet")!.MakeGenericMethod(mainType);
                    var recordSet = createMethod.Invoke(_env, new object[] { new[] { newId } });
                    
                    // Get the first record from the set
                    // RecordSet<T> implements IEnumerable<T>
                    var enumerable = (System.Collections.IEnumerable)recordSet!;
                    var enumerator = enumerable.GetEnumerator();
                    enumerator.MoveNext();
                    return (IOdooRecord)enumerator.Current;
                }
            }
            
            throw new InvalidOperationException($"Could not create record for model '{_modelName}'");
        }

        private string ToSnakeCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // Simple conversion: Name -> name, IsCompany -> is_company
            // This is a simplified version
            return string.Concat(text.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToLower();
        }
    }
}