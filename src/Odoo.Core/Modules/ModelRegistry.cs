using System;
using System.Collections.Generic;
using Odoo.Core;

namespace Odoo.Core.Modules
{
    public class ModelRegistry
    {
        private readonly Dictionary<string, ModelSchema> _models;
        private readonly Dictionary<string, Func<IEnvironment, int, IOdooRecord>> _factories;

        public ModelRegistry(Dictionary<string, ModelSchema> models)
        {
            _models = models;
            _factories = new Dictionary<string, Func<IEnvironment, int, IOdooRecord>>();
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
    }
}