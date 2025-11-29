using System;
using System.Collections.Generic;
using Odoo.Core;
using Python.Runtime;

namespace Odoo.Python
{
    /// <summary>
    /// Wrapper that exposes OdooEnvironment to Python with an Odoo-like API.
    /// Enables the familiar env['model'].browse(id) syntax in Python.
    /// </summary>
    public class OdooEnvironmentWrapper
    {
        private readonly OdooEnvironment _env;

        public OdooEnvironmentWrapper(OdooEnvironment env)
        {
            _env = env;
        }

        /// <summary>
        /// User ID of the environment.
        /// Python: env.user_id
        /// </summary>
        public int UserId => _env.UserId;

        /// <summary>
        /// Python indexer for model access.
        /// Python: env['res.partner']
        /// </summary>
        /// <param name="modelName">The model name (e.g., "res.partner").</param>
        /// <returns>A ModelProxyWrapper for the model.</returns>
        public ModelProxyWrapper __getitem__(string modelName)
        {
            return new ModelProxyWrapper(_env, modelName);
        }

        /// <summary>
        /// String representation for Python.
        /// </summary>
        public override string ToString()
        {
            return $"<OdooEnvironment user_id={UserId}>";
        }

        /// <summary>
        /// Python __repr__ method.
        /// </summary>
        public string __repr__()
        {
            return ToString();
        }
    }

    /// <summary>
    /// Wrapper for model access from Python.
    /// Provides browse(), create(), search() methods like Odoo.
    /// </summary>
    public class ModelProxyWrapper
    {
        private readonly OdooEnvironment _env;
        private readonly string _modelName;

        public ModelProxyWrapper(OdooEnvironment env, string modelName)
        {
            _env = env;
            _modelName = modelName;
        }

        /// <summary>
        /// The model name.
        /// </summary>
        public string ModelName => _modelName;

        /// <summary>
        /// Browse records by ID(s).
        /// Python: env['res.partner'].browse(1) or env['res.partner'].browse([1, 2, 3])
        /// </summary>
        public RecordSetWrapper Browse(params object[] ids)
        {
            var recordIds = new List<RecordId>();
            AddIdsRecursive(ids, recordIds);
            return new RecordSetWrapper(_env, _modelName, recordIds.ToArray());
        }

        /// <summary>
        /// Browse a single record by ID.
        /// Python: env['res.partner'].browse(1)
        /// </summary>
        public RecordSetWrapper Browse(int id)
        {
            return new RecordSetWrapper(_env, _modelName, new RecordId[] { id });
        }

        /// <summary>
        /// Browse a single record by ID (Python may pass long).
        /// </summary>
        public RecordSetWrapper Browse(long id)
        {
            return new RecordSetWrapper(_env, _modelName, new RecordId[] { (int)id });
        }

        /// <summary>
        /// Helper to recursively add IDs from various collection types.
        /// </summary>
        private void AddIdsRecursive(object item, List<RecordId> recordIds)
        {
            if (item == null)
                return;

            // Handle direct integer types
            if (item is int intId)
            {
                recordIds.Add(intId);
            }
            else if (item is long longId)
            {
                recordIds.Add((int)longId);
            }
            // Handle any IConvertible numeric type (covers Python.NET conversions)
            else if (item is IConvertible convertible && IsNumericType(item.GetType()))
            {
                try
                {
                    recordIds.Add(convertible.ToInt32(null));
                }
                catch
                {
                    // Skip non-convertible items
                }
            }
            // Handle arrays
            else if (item is object[] array)
            {
                foreach (var element in array)
                {
                    AddIdsRecursive(element, recordIds);
                }
            }
            // Handle generic IEnumerable
            else if (item is System.Collections.IEnumerable enumerable)
            {
                foreach (var element in enumerable)
                {
                    AddIdsRecursive(element, recordIds);
                }
            }
        }

        private static bool IsNumericType(Type type)
        {
            return type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(byte)
                || type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(ushort)
                || type == typeof(sbyte)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(decimal);
        }

        /// <summary>
        /// Create a new record.
        /// Python: env['res.partner'].create({'name': 'Test'})
        /// </summary>
        public RecordSetWrapper Create(IDictionary<string, object> vals)
        {
            try
            {
                // Use the ModelProxy's Create method via the environment
                var proxy = _env[_modelName];
                var record = proxy.Create(vals);
                return new RecordSetWrapper(_env, _modelName, new[] { record.Id });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to create {_modelName}: {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// Search for records (placeholder - not fully implemented yet).
        /// Python: env['res.partner'].search([('name', '=', 'Test')])
        /// </summary>
        public RecordSetWrapper Search(object domain)
        {
            // TODO: Implement domain search when search pipeline is available
            Console.WriteLine(
                $"[Warning] search() not fully implemented. Returning empty recordset."
            );
            return new RecordSetWrapper(_env, _modelName, Array.Empty<RecordId>());
        }

        /// <summary>
        /// Search and read in one call (placeholder).
        /// Python: env['res.partner'].search_read([...], ['name', 'email'])
        /// </summary>
        public object SearchRead(object domain, object? fields = null)
        {
            // TODO: Implement when search pipeline is available
            Console.WriteLine($"[Warning] search_read() not fully implemented.");
            return new List<Dictionary<string, object>>();
        }

        public override string ToString()
        {
            return $"<Model {_modelName}>";
        }

        public string __repr__()
        {
            return ToString();
        }
    }

    /// <summary>
    /// Wrapper for record sets in Python.
    /// Provides iteration and field access like Odoo recordsets.
    /// </summary>
    public class RecordSetWrapper
    {
        private readonly OdooEnvironment _env;
        private readonly string _modelName;
        private readonly RecordId[] _ids;

        public RecordSetWrapper(OdooEnvironment env, string modelName, RecordId[] ids)
        {
            _env = env;
            _modelName = modelName;
            _ids = ids;
        }

        /// <summary>
        /// The IDs in this recordset.
        /// Python: records.ids
        /// </summary>
        public long[] Ids => Array.ConvertAll(_ids, id => (long)id);

        /// <summary>
        /// Number of records.
        /// Python: len(records)
        /// </summary>
        public long __len__() => _ids.Length;

        /// <summary>
        /// Boolean test - True if recordset is not empty.
        /// Python: if records: ...
        /// </summary>
        public bool __bool__() => _ids.Length > 0;

        /// <summary>
        /// Iteration support.
        /// Python: for record in records: ...
        /// </summary>
        public IEnumerable<RecordWrapper> __iter__()
        {
            foreach (var id in _ids)
            {
                yield return new RecordWrapper(_env, _modelName, id);
            }
        }

        /// <summary>
        /// Get a single record by index.
        /// Python: records[0]
        /// </summary>
        public RecordWrapper __getitem__(long index)
        {
            if (index < 0)
                index = _ids.Length + index;

            if (index < 0 || index >= _ids.Length)
                throw new IndexOutOfRangeException(
                    $"Index {index} out of range for recordset with {_ids.Length} records"
                );

            return new RecordWrapper(_env, _modelName, _ids[index]);
        }

        /// <summary>
        /// Get attribute - for single record, delegate to the record.
        /// For multiple records, this would typically raise an error in Odoo.
        /// Python: record.name (when browsing single record)
        /// </summary>
        public object __getattr__(string name)
        {
            if (_ids.Length == 0)
            {
                return null!;
            }

            if (_ids.Length == 1)
            {
                var record = new RecordWrapper(_env, _modelName, _ids[0]);
                return record.__getattr__(name);
            }

            // For multiple records, return list of values (mapped)
            var values = new List<object?>();
            foreach (var id in _ids)
            {
                var record = new RecordWrapper(_env, _modelName, id);
                values.Add(record.__getattr__(name));
            }
            return values;
        }

        /// <summary>
        /// Write values to all records in the set.
        /// Python: records.write({'name': 'New Name'})
        /// </summary>
        public bool Write(IDictionary<string, object> vals)
        {
            // TODO: Implement when write pipeline is properly accessible
            foreach (var id in _ids)
            {
                // Create a record wrapper and write to it
                var record = new RecordWrapper(_env, _modelName, id);
                record.Write(vals);
            }
            return true;
        }

        /// <summary>
        /// Read fields from all records.
        /// Python: records.read(['name', 'email'])
        /// </summary>
        public List<Dictionary<string, object?>> Read(IEnumerable<string>? fields = null)
        {
            var result = new List<Dictionary<string, object?>>();
            foreach (var id in _ids)
            {
                var record = new RecordWrapper(_env, _modelName, id);
                result.Add(record.Read(fields));
            }
            return result;
        }

        public override string ToString()
        {
            return $"{_modelName}({string.Join(", ", _ids)})";
        }

        public string __repr__()
        {
            return ToString();
        }
    }

    /// <summary>
    /// Wrapper for a single record.
    /// Provides field access via __getattr__ and __setattr__.
    /// </summary>
    public class RecordWrapper
    {
        private readonly OdooEnvironment _env;
        private readonly string _modelName;
        private readonly RecordId _id;
        private IOdooRecord? _typedRecord;

        public RecordWrapper(OdooEnvironment env, string modelName, RecordId id)
        {
            _env = env;
            _modelName = modelName;
            _id = id;
        }

        /// <summary>
        /// The record ID.
        /// Python: record.id
        /// </summary>
        public long Id => (long)_id;

        /// <summary>
        /// Get the typed record from the identity map.
        /// This gives us the generated wrapper with proper computed field getters.
        /// </summary>
        private IOdooRecord? GetTypedRecord()
        {
            if (_typedRecord != null)
                return _typedRecord;

            _typedRecord = _env.GetRecord<IOdooRecord>(_modelName, _id);
            return _typedRecord;
        }

        /// <summary>
        /// Get field value.
        /// Python: record.name
        ///
        /// Uses the model schema to find the C# property name from the contributing interface,
        /// then accesses via the typed wrapper to trigger computed field logic.
        /// </summary>
        public object __getattr__(string name)
        {
            if (name == "id")
                return Id;

            // 1. Look up field in schema to validate it exists and get property name
            var schema = GetModelSchema();
            if (schema == null)
                throw new InvalidOperationException($"Model '{_modelName}' not found");

            if (!schema.Fields.TryGetValue(name, out var fieldSchema))
                throw new KeyNotFoundException($"Field '{name}' not found on model '{_modelName}'");

            // 2. Get the typed record wrapper (has computed field getters)
            var typedRecord = GetTypedRecord();
            if (typedRecord == null)
                throw new InvalidOperationException(
                    $"Could not get typed record for '{_modelName}'"
                );

            // 3. Find the C# property name from the contributing interface
            var propertyName = GetPropertyName(fieldSchema.ContributingInterface, name);
            if (propertyName == null)
                throw new KeyNotFoundException(
                    $"Property for field '{name}' not found on interface '{fieldSchema.ContributingInterface.Name}'"
                );

            // 4. Get property value via reflection (triggers computed field logic)
            var property = typedRecord.GetType().GetProperty(propertyName);
            if (property != null)
            {
                return property.GetValue(typedRecord)!;
            }

            throw new KeyNotFoundException(
                $"Property '{propertyName}' not found on type '{typedRecord.GetType().Name}'"
            );
        }

        /// <summary>
        /// Set field value.
        /// Python: record.name = 'New Name'
        ///
        /// Uses the model schema to find the C# property name, then sets via typed wrapper.
        /// </summary>
        public void __setattr__(string name, object value)
        {
            // 1. Look up field in schema
            var schema = GetModelSchema();
            if (schema == null)
                throw new InvalidOperationException($"Model '{_modelName}' not found");

            if (!schema.Fields.TryGetValue(name, out var fieldSchema))
                throw new KeyNotFoundException($"Field '{name}' not found on model '{_modelName}'");

            // 2. Get the typed record wrapper
            var typedRecord = GetTypedRecord();
            if (typedRecord == null)
                throw new InvalidOperationException(
                    $"Could not get typed record for '{_modelName}'"
                );

            // 3. Find the C# property name from the contributing interface
            var propertyName = GetPropertyName(fieldSchema.ContributingInterface, name);
            if (propertyName == null)
                throw new KeyNotFoundException(
                    $"Property for field '{name}' not found on interface '{fieldSchema.ContributingInterface.Name}'"
                );

            // 4. Set property value via reflection
            var property = typedRecord.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(typedRecord, value);
                return;
            }

            throw new InvalidOperationException(
                $"Property '{propertyName}' is not writable on type '{typedRecord.GetType().Name}'"
            );
        }

        /// <summary>
        /// Get the C# property name from the contributing interface by finding the property
        /// with [OdooField] attribute matching the field name.
        /// </summary>
        private string? GetPropertyName(Type contributingInterface, string fieldName)
        {
            foreach (var prop in contributingInterface.GetProperties())
            {
                var attr = prop.GetCustomAttributes(typeof(OdooFieldAttribute), true);
                if (attr.Length > 0 && attr[0] is OdooFieldAttribute fieldAttr)
                {
                    if (fieldAttr.TechnicalName == fieldName)
                    {
                        return prop.Name;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Write multiple values.
        /// Python: record.write({'name': 'New Name', 'email': 'test@example.com'})
        /// </summary>
        public bool Write(IDictionary<string, object> vals)
        {
            foreach (var (key, value) in vals)
            {
                __setattr__(key, value);
            }
            return true;
        }

        /// <summary>
        /// Read field values.
        /// Python: record.read(['name', 'email'])
        /// </summary>
        public Dictionary<string, object?> Read(IEnumerable<string>? fields = null)
        {
            var result = new Dictionary<string, object?>();
            result["id"] = Id;

            var schema = GetModelSchema();
            if (schema == null)
                return result;

            var fieldNames = fields ?? schema.Fields.Keys;

            foreach (var fieldName in fieldNames)
            {
                if (fieldName == "id")
                    continue;

                try
                {
                    result[fieldName] = __getattr__(fieldName);
                }
                catch
                {
                    result[fieldName] = null;
                }
            }

            return result;
        }

        private Odoo.Core.Modules.ModelSchema? GetModelSchema()
        {
            return _env.GetModelSchema(_modelName);
        }

        public override string ToString()
        {
            return $"{_modelName}({_id})";
        }

        public string __repr__()
        {
            return ToString();
        }
    }
}
