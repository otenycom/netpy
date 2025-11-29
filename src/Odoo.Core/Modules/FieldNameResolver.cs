using System.Collections.Generic;

namespace Odoo.Core.Modules
{
    public class FieldNameResolver
    {
        private readonly ModelRegistry _registry;

        // Cache: (modelName, fieldName) -> FieldHandle
        private readonly Dictionary<(string, string), FieldHandle> _cache = new();

        public FieldNameResolver(ModelRegistry registry)
        {
            _registry = registry;
        }

        public FieldHandle GetFieldHandle(string modelName, string fieldName)
        {
            var key = (modelName, fieldName);
            if (!_cache.TryGetValue(key, out var handle))
            {
                var model = _registry.GetModel(modelName);
                if (model == null)
                {
                    throw new KeyNotFoundException($"Model '{modelName}' not found");
                }

                if (model.Fields.TryGetValue(fieldName, out var field))
                {
                    handle = field.Token;
                    _cache[key] = handle;
                }
                else
                {
                    throw new KeyNotFoundException(
                        $"Field '{fieldName}' not found on model '{modelName}'"
                    );
                }
            }
            return handle;
        }
    }
}
