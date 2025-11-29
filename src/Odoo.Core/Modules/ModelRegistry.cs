using System;
using System.Collections.Generic;
using Odoo.Core;

namespace Odoo.Core.Modules
{
    public class ModelRegistry
    {
        // Primary storage: ModelHandle-based for fast lookup
        private readonly Dictionary<ModelHandle, ModelSchema> _modelsByHandle;
        private readonly Dictionary<
            ModelHandle,
            Func<IEnvironment, RecordId, IOdooRecord>
        > _factoriesByHandle;

        // Secondary index: string-based for backward compatibility and string-based lookups
        private readonly Dictionary<string, ModelSchema> _modelsByName;

        // Dependency graph: (Model, Field) -> List<(Model, Field)>
        private readonly Dictionary<
            (ModelHandle, FieldHandle),
            List<(ModelHandle, FieldHandle)>
        > _dependencies;

        public ModelRegistry(
            Dictionary<string, ModelSchema> models,
            Dictionary<(ModelHandle, FieldHandle), List<(ModelHandle, FieldHandle)>>? dependencies =
                null
        )
        {
            _modelsByName = models;
            _modelsByHandle = new Dictionary<ModelHandle, ModelSchema>();
            _factoriesByHandle =
                new Dictionary<ModelHandle, Func<IEnvironment, RecordId, IOdooRecord>>();
            _dependencies =
                dependencies
                ?? new Dictionary<(ModelHandle, FieldHandle), List<(ModelHandle, FieldHandle)>>();

            // Build handle-based index from name-based input
            foreach (var (name, schema) in models)
            {
                _modelsByHandle[schema.Token] = schema;
            }
        }

        #region Model Lookup

        /// <summary>
        /// Get a model by its handle (fast path).
        /// </summary>
        public ModelSchema? GetModel(ModelHandle handle)
        {
            return _modelsByHandle.TryGetValue(handle, out var schema) ? schema : null;
        }

        /// <summary>
        /// Get a model by name (backward compatible).
        /// </summary>
        public ModelSchema? GetModel(string modelName)
        {
            return _modelsByName.TryGetValue(modelName, out var schema) ? schema : null;
        }

        /// <summary>
        /// Try to get a model by handle.
        /// </summary>
        public bool TryGetModel(ModelHandle handle, out ModelSchema? schema)
        {
            return _modelsByHandle.TryGetValue(handle, out schema);
        }

        /// <summary>
        /// Try to get a model by name.
        /// </summary>
        public bool TryGetModel(string modelName, out ModelSchema? schema)
        {
            return _modelsByName.TryGetValue(modelName, out schema);
        }

        public IEnumerable<ModelSchema> GetAllModels()
        {
            return _modelsByHandle.Values;
        }

        #endregion

        #region Factory Registration

        /// <summary>
        /// Register a factory by model handle (preferred).
        /// </summary>
        public void RegisterFactory(
            ModelHandle handle,
            Func<IEnvironment, RecordId, IOdooRecord> factory
        )
        {
            _factoriesByHandle[handle] = factory;
        }

        /// <summary>
        /// Register a factory by model name (backward compatible).
        /// Looks up the handle from the model schema.
        /// </summary>
        public void RegisterFactory(
            string modelName,
            Func<IEnvironment, RecordId, IOdooRecord> factory
        )
        {
            if (_modelsByName.TryGetValue(modelName, out var schema))
            {
                _factoriesByHandle[schema.Token] = factory;
            }
            else
            {
                // Model not registered yet - create a handle from hash for late binding
                var handle = new ModelHandle(StableHash.GetStableHashCode(modelName), modelName);
                _factoriesByHandle[handle] = factory;
            }
        }

        /// <summary>
        /// Get record factory by model handle (fast path).
        /// </summary>
        public Func<IEnvironment, RecordId, IOdooRecord> GetRecordFactory(ModelHandle handle)
        {
            if (_factoriesByHandle.TryGetValue(handle, out var factory))
            {
                return factory;
            }

            var schema = GetModel(handle);
            var modelName = schema?.ModelName ?? $"handle:{handle.Token}";
            throw new KeyNotFoundException(
                $"No record factory registered for model '{modelName}'. Ensure the source generator has run."
            );
        }

        /// <summary>
        /// Get record factory by model name (backward compatible).
        /// </summary>
        public Func<IEnvironment, RecordId, IOdooRecord> GetRecordFactory(string modelName)
        {
            if (_modelsByName.TryGetValue(modelName, out var schema))
            {
                return GetRecordFactory(schema.Token);
            }

            // Try direct hash lookup for models registered before schema
            var handle = new ModelHandle(StableHash.GetStableHashCode(modelName), modelName);
            if (_factoriesByHandle.TryGetValue(handle, out var factory))
            {
                return factory;
            }

            throw new KeyNotFoundException(
                $"No record factory registered for model '{modelName}'. Ensure the source generator has run."
            );
        }

        #endregion

        #region Dependencies

        /// <summary>
        /// Get fields that depend on the specified field (and thus need recomputation).
        /// </summary>
        public IEnumerable<(ModelHandle Model, FieldHandle Field)> GetDependents(
            ModelHandle model,
            FieldHandle field
        )
        {
            if (_dependencies.TryGetValue((model, field), out var dependents))
            {
                foreach (var (depModel, depField) in dependents)
                {
                    yield return (depModel, depField);
                }
            }
        }

        #endregion
    }
}
