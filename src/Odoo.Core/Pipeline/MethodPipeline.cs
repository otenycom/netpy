using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Odoo.Core.Pipeline
{
    public class MethodPipeline
    {
        private Delegate? _baseHandler;
        private readonly List<(int Priority, Delegate Handler)> _overrides = new();
        private Delegate? _compiledChain;

        public void SetBase(Delegate handler)
        {
            _baseHandler = handler;
        }

        public void SetDefaultBase(Delegate handler)
        {
            if (_baseHandler == null)
            {
                _baseHandler = handler;
            }
        }

        public void AddOverride(int priority, Delegate handler)
        {
            _overrides.Add((priority, handler));
        }

        public TDelegate GetChain<TDelegate>()
            where TDelegate : Delegate
        {
            if (_compiledChain != null)
                return (TDelegate)_compiledChain;

            Compile();
            return (TDelegate)_compiledChain!;
        }

        public void Compile()
        {
            if (_baseHandler == null)
                throw new InvalidOperationException("Base handler not set for pipeline.");

            // Sort overrides by priority (lowest first, so they are wrapped by higher priority ones)
            // This ensures High Priority -> calls Super -> Low Priority -> calls Super -> Base
            _overrides.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            Delegate current = _baseHandler;

            foreach (var (priority, wrapper) in _overrides)
            {
                current = Compose(wrapper, current);
            }

            _compiledChain = current;
        }

        private Delegate Compose(Delegate wrapper, Delegate super)
        {
            // wrapper signature: (args..., superDelegate) -> returnType
            // super signature: (args...) -> returnType
            // result signature: (args...) -> returnType

            var wrapperType = wrapper.GetType();
            var invokeMethod = wrapperType.GetMethod("Invoke")!;
            var parameters = invokeMethod.GetParameters();

            // The arguments for the resulting delegate (all except the last one which is super)
            var argParams = parameters
                .Take(parameters.Length - 1)
                .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                .ToArray();

            // The call to wrapper(args..., super)
            // We pass 'super' as a constant.
            // Note: super is a Delegate instance, so we pass it as such.
            var superParam = Expression.Constant(super, super.GetType());

            var allArgs = new List<Expression>(argParams);
            allArgs.Add(superParam);

            var call = Expression.Call(Expression.Constant(wrapper), invokeMethod, allArgs);

            // Create the lambda matching the 'super' signature (which is the same as the result signature)
            var lambda = Expression.Lambda(super.GetType(), call, argParams);

            return lambda.Compile();
        }
    }
}
