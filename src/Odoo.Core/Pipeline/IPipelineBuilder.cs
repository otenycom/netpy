using System;

namespace Odoo.Core.Pipeline
{
    /// <summary>
    /// Interface for building method pipelines.
    /// Used by module registrars to register base implementations and overrides.
    /// </summary>
    public interface IPipelineBuilder
    {
        /// <summary>
        /// Register a base implementation (leaf node of the pipeline).
        /// </summary>
        /// <param name="model">Model name (e.g. "res.partner")</param>
        /// <param name="method">Method name (e.g. "create")</param>
        /// <param name="handler">The delegate for the base method</param>
        void RegisterBase(string model, string method, Delegate handler);

        /// <summary>
        /// Register an override (wrapper node of the pipeline).
        /// </summary>
        /// <param name="model">Model name</param>
        /// <param name="method">Method name</param>
        /// <param name="priority">Priority based on module dependency depth</param>
        /// <param name="handler">The delegate for the override method (takes super as last arg)</param>
        void RegisterOverride(string model, string method, int priority, Delegate handler);

        /// <summary>
        /// Get the compiled pipeline delegate for a method.
        /// </summary>
        TDelegate GetPipeline<TDelegate>(string model, string method) where TDelegate : Delegate;
    }
}