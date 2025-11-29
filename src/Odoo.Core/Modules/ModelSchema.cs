using System;
using System.Collections.Generic;
using System.Linq;

namespace Odoo.Core.Modules
{
    /// <summary>
    /// Describes a model's complete schema including fields, computed fields, and dependencies.
    /// Mirrors Odoo's model metaclass with support for multi-module extensibility.
    /// </summary>
    public class ModelSchema
    {
        // --- Basic Properties ---

        public string ModelName { get; }
        public List<Type> ContributingInterfaces { get; }
        public Dictionary<string, FieldSchema> Fields { get; }
        public ModelHandle Token { get; }

        // --- Computed Fields ---

        /// <summary>
        /// List of computed field names in this model.
        /// Used for iterating computed fields during recomputation.
        /// </summary>
        public List<string> ComputedFields { get; } = new();

        /// <summary>
        /// List of stored computed field names.
        /// These are recomputed when dependencies change.
        /// </summary>
        public List<string> StoredComputedFields { get; } = new();

        // --- Field Dependencies ---

        /// <summary>
        /// Dependency graph: field name -> list of dependent computed field names.
        /// When a field changes, all dependent fields need recomputation.
        /// </summary>
        public Dictionary<string, List<string>> FieldDependents { get; } = new();

        /// <summary>
        /// Reverse dependency graph: computed field name -> list of fields it depends on.
        /// Used for understanding what triggers a computed field.
        /// </summary>
        public Dictionary<string, List<string>> FieldDependencies { get; } = new();

        // --- Pipelines ---

        /// <summary>
        /// The write pipeline delegate type for this model.
        /// Signature: Action&lt;RecordHandle[], Dictionary&lt;string, object?&gt;&gt;
        /// </summary>
        public Type? WritePipelineType { get; set; }

        /// <summary>
        /// The create pipeline delegate type for this model.
        /// Signature: Func&lt;IEnvironment, Dictionary&lt;string, object?&gt;, RecordHandle&gt;
        /// </summary>
        public Type? CreatePipelineType { get; set; }

        public ModelSchema(string modelName, ModelHandle token)
        {
            ModelName = modelName;
            Token = token;
            ContributingInterfaces = new List<Type>();
            Fields = new Dictionary<string, FieldSchema>();
        }

        /// <summary>
        /// Register a computed field and its dependencies.
        /// This builds the dependency graph for recomputation.
        /// </summary>
        /// <param name="fieldName">The computed field name</param>
        /// <param name="dependencies">Fields that trigger recomputation</param>
        public void RegisterComputedField(string fieldName, IEnumerable<string> dependencies)
        {
            if (!ComputedFields.Contains(fieldName))
            {
                ComputedFields.Add(fieldName);
            }

            // Store dependencies for the computed field
            var depList = dependencies.ToList();
            FieldDependencies[fieldName] = depList;

            // Build reverse mapping: for each dependency, register this field as dependent
            foreach (var dep in depList)
            {
                if (!FieldDependents.TryGetValue(dep, out var dependents))
                {
                    dependents = new List<string>();
                    FieldDependents[dep] = dependents;
                }

                if (!dependents.Contains(fieldName))
                {
                    dependents.Add(fieldName);
                }
            }

            // Mark the field schema as having dependents
            foreach (var dep in depList)
            {
                if (Fields.TryGetValue(dep, out var depField))
                {
                    depField.HasDependents = true;
                }
            }
        }

        /// <summary>
        /// Register a stored computed field.
        /// Stored fields are persisted to the database and updated when dependencies change.
        /// </summary>
        public void RegisterStoredComputedField(string fieldName, IEnumerable<string> dependencies)
        {
            RegisterComputedField(fieldName, dependencies);

            if (!StoredComputedFields.Contains(fieldName))
            {
                StoredComputedFields.Add(fieldName);
            }

            if (Fields.TryGetValue(fieldName, out var field))
            {
                field.IsStored = true;
            }
        }

        /// <summary>
        /// Get all computed fields that depend on the given field.
        /// </summary>
        public IEnumerable<string> GetDependentFields(string fieldName)
        {
            if (FieldDependents.TryGetValue(fieldName, out var dependents))
            {
                return dependents;
            }
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Get all fields that a computed field depends on.
        /// </summary>
        public IEnumerable<string> GetFieldDependencies(string computedFieldName)
        {
            if (FieldDependencies.TryGetValue(computedFieldName, out var dependencies))
            {
                return dependencies;
            }
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Get the FieldHandle for a field by name.
        /// </summary>
        public FieldHandle GetFieldToken(string fieldName)
        {
            if (Fields.TryGetValue(fieldName, out var field))
            {
                return field.Token;
            }
            throw new KeyNotFoundException($"Field '{fieldName}' not found in model '{ModelName}'");
        }

        /// <summary>
        /// Check if a field exists in this model.
        /// </summary>
        public bool HasField(string fieldName) => Fields.ContainsKey(fieldName);

        /// <summary>
        /// Get all writable fields (non-readonly, non-computed or computed with inverse).
        /// </summary>
        public IEnumerable<FieldSchema> GetWritableFields()
        {
            return Fields.Values.Where(f => f.IsWritable);
        }

        /// <summary>
        /// Get all fields that need default values on create.
        /// </summary>
        public IEnumerable<FieldSchema> GetFieldsWithDefaults()
        {
            return Fields.Values.Where(f => f.DefaultMethodName != null || f.DefaultValue != null);
        }
    }
}
