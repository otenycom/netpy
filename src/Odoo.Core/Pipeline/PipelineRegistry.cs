using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Odoo.Core.Pipeline
{
    public class PipelineRegistry : IPipelineBuilder
    {
        private readonly ConcurrentDictionary<
            (string Model, string Method),
            MethodPipeline
        > _pipelines = new();

        public void RegisterBase(string model, string method, Delegate handler)
        {
            var pipeline = GetOrAddPipeline(model, method);
            pipeline.SetBase(handler);
        }

        public void RegisterDefaultBase(string model, string method, Delegate handler)
        {
            var pipeline = GetOrAddPipeline(model, method);
            pipeline.SetDefaultBase(handler);
        }

        public void RegisterOverride(string model, string method, int priority, Delegate handler)
        {
            var pipeline = GetOrAddPipeline(model, method);
            pipeline.AddOverride(priority, handler);
        }

        public TDelegate GetPipeline<TDelegate>(string model, string method)
            where TDelegate : Delegate
        {
            if (_pipelines.TryGetValue((model, method), out var pipeline))
            {
                return pipeline.GetChain<TDelegate>();
            }
            throw new KeyNotFoundException($"No pipeline found for {model}.{method}");
        }

        /// <summary>
        /// Try to get a pipeline by model and method name.
        /// Returns false if no pipeline exists for this model/method combination.
        /// </summary>
        /// <typeparam name="TDelegate">The expected delegate type</typeparam>
        /// <param name="model">The model name (e.g., "res.partner")</param>
        /// <param name="method">The method name (e.g., "action_verify")</param>
        /// <param name="pipeline">The compiled pipeline chain, if found</param>
        /// <returns>True if a pipeline was found, false otherwise</returns>
        public bool TryGetPipeline<TDelegate>(string model, string method, out TDelegate? pipeline)
            where TDelegate : Delegate
        {
            if (_pipelines.TryGetValue((model, method), out var methodPipeline))
            {
                pipeline = methodPipeline.GetChain<TDelegate>();
                return true;
            }
            pipeline = default;
            return false;
        }

        /// <summary>
        /// Check if a pipeline exists for the given model and method.
        /// </summary>
        /// <param name="model">The model name (e.g., "res.partner")</param>
        /// <param name="method">The method name (e.g., "action_verify")</param>
        /// <returns>True if a pipeline exists, false otherwise</returns>
        public bool HasPipeline(string model, string method)
        {
            return _pipelines.ContainsKey((model, method));
        }

        /// <summary>
        /// Get the raw pipeline delegate for a model/method without type checking.
        /// Used for dynamic invocation when the delegate type isn't known at compile time.
        /// </summary>
        /// <param name="model">The model name</param>
        /// <param name="method">The method name</param>
        /// <returns>The compiled delegate, or null if not found</returns>
        public Delegate? GetPipelineDelegate(string model, string method)
        {
            if (_pipelines.TryGetValue((model, method), out var pipeline))
            {
                return pipeline.GetCompiledDelegate();
            }
            return null;
        }

        private MethodPipeline GetOrAddPipeline(string model, string method)
        {
            return _pipelines.GetOrAdd((model, method), _ => new MethodPipeline());
        }

        public void CompileAll()
        {
            foreach (var pipeline in _pipelines.Values)
            {
                pipeline.Compile();
            }
        }
    }
}
