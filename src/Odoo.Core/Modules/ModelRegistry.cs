using System;
using System.Collections.Generic;
using Odoo.Core;

namespace Odoo.Core.Modules
{
    public class ModelRegistry
    {
        private readonly Dictionary<string, ModelSchema> _models;
        private readonly Dictionary<string, Func<IEnvironment, int, IOdooRecord>> _factories;
        
        // Dependency graph: (ModelToken, FieldToken) -> List<(ModelToken, FieldToken)>
        private readonly Dictionary<(int, int), List<(int, int)>> _dependencies;

        public ModelRegistry(
            Dictionary<string, ModelSchema> models,
            Dictionary<(int, int), List<(int, int)>>? dependencies = null)
        {
            _models = models;
            _factories = new Dictionary<string, Func<IEnvironment, int, IOdooRecord>>();
            _dependencies = dependencies ?? new Dictionary<(int, int), List<(int, int)>>();
        }

        public ModelSchema? GetModel(string modelName)
        {
            return _models.TryGetValue(modelName, out var schema) ? schema : null;
        }

        public IEnumerable<ModelSchema> GetAllModels()
        {
            return _models.Values;
        }

        public void RegisterFactory(string modelName, Func<IEnvironment, int, IOdooRecord> factory)
        {
            _factories[modelName] = factory;
        }

        public Func<IEnvironment, int, IOdooRecord> GetRecordFactory(string modelName)
        {
            if (_factories.TryGetValue(modelName, out var factory))
            {
                return factory;
            }
            
            throw new KeyNotFoundException($"No record factory registered for model '{modelName}'. Ensure the source generator has run.");
        }

        /// <summary>
        /// Get fields that depend on the specified field (and thus need recomputation).
        /// </summary>
        public IEnumerable<(int ModelToken, int FieldToken)> GetDependents(int modelToken, int fieldToken)
        {
            if (_dependencies.TryGetValue((modelToken, fieldToken), out var dependents))
            {
                return dependents;
            }
            return Enumerable.Empty<(int, int)>();
        }
    }
}