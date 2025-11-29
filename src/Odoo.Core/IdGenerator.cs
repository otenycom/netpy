using System.Collections.Concurrent;

namespace Odoo.Core
{
    /// <summary>
    /// Thread-safe ID generator for Odoo models.
    /// Manages atomic counters for each model to ensure unique IDs.
    /// </summary>
    public class IdGenerator
    {
        private readonly ConcurrentDictionary<string, long> _counters = new();

        /// <summary>
        /// Get the next available ID for a specific model.
        /// </summary>
        /// <param name="modelName">The model name (e.g., "res.partner")</param>
        /// <returns>A unique integer ID</returns>
        public RecordId NextId(string modelName)
        {
            // Start at 1000 to avoid conflicts with demo data
            return new RecordId(
                _counters.AddOrUpdate(modelName, 1000L, (_, current) => current + 1)
            );
        }

        /// <summary>
        /// Set the starting ID for a model.
        /// Useful for testing or when loading existing data.
        /// </summary>
        public void SetStartId(string modelName, long startId)
        {
            _counters[modelName] = startId;
        }
    }
}
