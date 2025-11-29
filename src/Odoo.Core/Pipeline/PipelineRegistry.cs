using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Odoo.Core.Pipeline
{
    public class PipelineRegistry : IPipelineBuilder
    {
        private readonly ConcurrentDictionary<(string Model, string Method), MethodPipeline> _pipelines = new();

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

        public TDelegate GetPipeline<TDelegate>(string model, string method) where TDelegate : Delegate
        {
            if (_pipelines.TryGetValue((model, method), out var pipeline))
            {
                return pipeline.GetChain<TDelegate>();
            }
            throw new KeyNotFoundException($"No pipeline found for {model}.{method}");
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