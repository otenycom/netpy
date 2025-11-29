using Odoo.Core.Modules;

namespace Odoo.Core.Pipeline
{
    /// <summary>
    /// Interface implemented by generated module registration classes.
    /// Registers method overrides, base implementations, and record factories.
    /// </summary>
    public interface IModuleRegistrar
    {
        /// <summary>
        /// Register pipeline handlers (method overrides and base implementations).
        /// </summary>
        void RegisterPipelines(IPipelineBuilder builder);

        /// <summary>
        /// Register record factories for dynamically creating record instances.
        /// </summary>
        void RegisterFactories(ModelRegistry modelRegistry);

        /// <summary>
        /// Register values handlers for IModel.Write/Create support.
        /// </summary>
        void RegisterValuesHandlers(OdooEnvironment env);
    }
}
