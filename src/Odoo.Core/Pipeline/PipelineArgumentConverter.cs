using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Odoo.Core.Pipeline
{
    /// <summary>
    /// Context containing all available data for argument conversion.
    /// Passed to converters so they can extract the data they need.
    /// </summary>
    public class PipelineInvocationContext
    {
        public OdooEnvironment Environment { get; }
        public string ModelName { get; }
        public RecordId[] RecordIds { get; }
        public object[] Args { get; }

        /// <summary>
        /// Index tracking which arg has been consumed.
        /// Converters that consume args should increment this.
        /// </summary>
        public int ArgsConsumed { get; set; }

        public PipelineInvocationContext(
            OdooEnvironment environment,
            string modelName,
            RecordId[] recordIds,
            object[] args
        )
        {
            Environment = environment;
            ModelName = modelName;
            RecordIds = recordIds;
            Args = args;
            ArgsConsumed = 0;
        }

        /// <summary>
        /// Get the next unconsumed argument, or null if none left.
        /// </summary>
        public object? PeekNextArg()
        {
            return ArgsConsumed < Args.Length ? Args[ArgsConsumed] : null;
        }

        /// <summary>
        /// Consume and return the next argument.
        /// </summary>
        public object? ConsumeNextArg()
        {
            return ArgsConsumed < Args.Length ? Args[ArgsConsumed++] : null;
        }
    }

    /// <summary>
    /// Interface for type converters that can convert invocation context data
    /// to specific parameter types expected by pipeline delegates.
    /// </summary>
    public interface IPipelineArgumentConverter
    {
        /// <summary>
        /// Check if this converter can handle the target parameter type.
        /// </summary>
        /// <param name="parameterType">The type the delegate parameter expects</param>
        /// <param name="parameterName">The parameter name (can provide hints)</param>
        /// <param name="context">The invocation context (for inspecting available data)</param>
        /// <returns>True if this converter can provide a value for this parameter</returns>
        bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        );

        /// <summary>
        /// Convert/extract the value from context for the target parameter.
        /// </summary>
        /// <param name="parameterType">The type the delegate parameter expects</param>
        /// <param name="parameterName">The parameter name</param>
        /// <param name="context">The invocation context</param>
        /// <returns>The converted value</returns>
        object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        );

        /// <summary>
        /// Priority for converter selection. Higher priority converters are tried first.
        /// This helps resolve ambiguity when multiple converters could handle a type.
        /// </summary>
        int Priority { get; }
    }

    /// <summary>
    /// Converter for OdooEnvironment parameters.
    /// </summary>
    public class OdooEnvironmentConverter : IPipelineArgumentConverter
    {
        public int Priority => 100;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return typeof(OdooEnvironment).IsAssignableFrom(parameterType);
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return context.Environment;
        }
    }

    /// <summary>
    /// Converter for IEnvironment parameters.
    /// </summary>
    public class IEnvironmentConverter : IPipelineArgumentConverter
    {
        public int Priority => 90;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return typeof(IEnvironment).IsAssignableFrom(parameterType)
                && !typeof(OdooEnvironment).IsAssignableFrom(parameterType);
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return context.Environment;
        }
    }

    /// <summary>
    /// Converter for model name string parameters.
    /// Uses parameter name hints AND positional inference to identify model name parameters.
    /// </summary>
    public class ModelNameConverter : IPipelineArgumentConverter
    {
        private static readonly HashSet<string> ModelNameHints = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            "modelName",
            "model_name",
            "model",
        };

        public int Priority => 80;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            if (parameterType != typeof(string))
                return false;

            // Check explicit name hints
            if (ModelNameHints.Contains(parameterName))
                return true;

            // If there's no string arg available, and we need a string,
            // it's likely the model name (common pattern in pipelines)
            var nextArg = context.PeekNextArg();
            if (nextArg == null || nextArg is not string)
            {
                // No string in args - this string param is probably the model name
                return true;
            }

            return false;
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return context.ModelName;
        }
    }

    /// <summary>
    /// Converter for RecordSet&lt;T&gt; parameters.
    /// Creates a RecordSet from the record IDs in the context.
    /// </summary>
    public class RecordSetConverter : IPipelineArgumentConverter
    {
        public int Priority => 70;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return parameterType.IsGenericType
                && parameterType.GetGenericTypeDefinition() == typeof(RecordSet<>);
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            var recordType = parameterType.GetGenericArguments()[0];
            return CreateRecordSetForType(
                context.Environment,
                context.ModelName,
                recordType,
                context.RecordIds
            );
        }

        private static object CreateRecordSetForType(
            OdooEnvironment env,
            string modelName,
            Type recordType,
            RecordId[] recordIds
        )
        {
            // Get model name from type attribute if not provided
            var actualModelName = modelName;
            var modelAttr =
                recordType.GetCustomAttributes(typeof(OdooModelAttribute), true).FirstOrDefault()
                as OdooModelAttribute;
            if (modelAttr != null)
            {
                actualModelName = modelAttr.ModelName;
            }

            // Create RecordSet<T> using reflection
            var recordSetType = typeof(RecordSet<>).MakeGenericType(recordType);

            // The factory type is Func<IEnvironment, RecordId, T>
            var factoryType = typeof(Func<,,>).MakeGenericType(
                typeof(IEnvironment),
                typeof(RecordId),
                recordType
            );

            // Create factory delegate
            var createFactoryMethod = typeof(RecordSetConverter)
                .GetMethod(
                    nameof(CreateRecordFactory),
                    BindingFlags.NonPublic | BindingFlags.Static
                )!
                .MakeGenericMethod(recordType);

            var factory = createFactoryMethod.Invoke(null, new object[] { env, actualModelName });

            var constructor = recordSetType.GetConstructor(
                new[] { typeof(IEnvironment), typeof(string), typeof(RecordId[]), factoryType }
            );

            if (constructor == null)
            {
                throw new InvalidOperationException(
                    $"Cannot find RecordSet<{recordType.Name}> constructor"
                );
            }

            return constructor.Invoke(new object[] { env, actualModelName, recordIds, factory! });
        }

        private static Func<IEnvironment, RecordId, T> CreateRecordFactory<T>(
            OdooEnvironment env,
            string modelName
        )
            where T : class, IOdooRecord
        {
            return (e, id) => ((OdooEnvironment)e).GetRecord<T>(modelName, id);
        }
    }

    /// <summary>
    /// Converter for RecordHandle parameters.
    /// Creates a RecordHandle from the first record ID.
    /// </summary>
    public class RecordHandleConverter : IPipelineArgumentConverter
    {
        public int Priority => 70;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return parameterType == typeof(RecordHandle) && context.RecordIds.Length > 0;
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            var modelToken = context.Environment.GetModelToken(context.ModelName);
            // Note: This returns a single handle. For multi-record operations,
            // the caller should iterate over record IDs separately.
            return new RecordHandle(context.Environment, context.RecordIds[0], modelToken);
        }
    }

    /// <summary>
    /// Converter for IRecordValues parameters.
    /// Converts dictionary arguments to typed IRecordValues using the model's handler.
    /// </summary>
    public class RecordValuesConverter : IPipelineArgumentConverter
    {
        public int Priority => 60;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            if (!typeof(IRecordValues).IsAssignableFrom(parameterType))
                return false;

            var nextArg = context.PeekNextArg();
            return nextArg is IRecordValues || nextArg is IDictionary<string, object>;
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            var arg = context.ConsumeNextArg();

            if (arg is IRecordValues recordVals)
            {
                return recordVals;
            }

            if (arg is IDictionary<string, object> dict)
            {
                var handler = context.Environment.GetValuesHandler(context.ModelName);
                return handler.FromDictionary(
                    new Dictionary<string, object?>(
                        dict.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value))
                    )
                );
            }

            throw new InvalidOperationException(
                $"Expected IRecordValues or dictionary, got {arg?.GetType().Name ?? "null"}"
            );
        }
    }

    /// <summary>
    /// Converter for IEnumerable&lt;long&gt; parameters (typically IDs for browse operations).
    /// Can use record IDs from context or consume from args.
    /// </summary>
    public class IdsEnumerableConverter : IPipelineArgumentConverter
    {
        public int Priority => 50;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            // Check if it's IEnumerable<long> or similar
            if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(parameterType))
                return false;

            // Check for generic IEnumerable<long>
            if (parameterType.IsGenericType)
            {
                var genericArg = parameterType.GetGenericArguments().FirstOrDefault();
                if (genericArg == typeof(long) || genericArg == typeof(int))
                    return true;
            }

            // Check if next arg is an enumerable of numbers
            var nextArg = context.PeekNextArg();
            return nextArg is IEnumerable<long> || nextArg is IEnumerable<int> || nextArg is long[];
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            // First check if there's an arg that provides IDs
            var nextArg = context.PeekNextArg();
            if (nextArg is IEnumerable<long> longIds)
            {
                context.ConsumeNextArg();
                return longIds;
            }

            if (nextArg is IEnumerable<int> intIds)
            {
                context.ConsumeNextArg();
                return intIds.Select(i => (long)i);
            }

            if (nextArg is long[] longArray)
            {
                context.ConsumeNextArg();
                return longArray;
            }

            // Fall back to converting RecordIds
            return context.RecordIds.Select(id => (long)id.Value);
        }
    }

    /// <summary>
    /// Converter for IDictionary&lt;string, object&gt; parameters.
    /// Passes through dictionary from args.
    /// </summary>
    public class DictionaryConverter : IPipelineArgumentConverter
    {
        public int Priority => 40;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            if (!typeof(IDictionary<string, object>).IsAssignableFrom(parameterType))
                return false;

            var nextArg = context.PeekNextArg();
            return nextArg is IDictionary<string, object>;
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return context.ConsumeNextArg();
        }
    }

    /// <summary>
    /// Fallback converter that tries to pass through args directly if type matches.
    /// </summary>
    public class PassThroughConverter : IPipelineArgumentConverter
    {
        public int Priority => 10;

        public bool CanConvert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            var nextArg = context.PeekNextArg();
            return nextArg != null && parameterType.IsAssignableFrom(nextArg.GetType());
        }

        public object? Convert(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            return context.ConsumeNextArg();
        }
    }

    /// <summary>
    /// Resolves and builds argument arrays for pipeline delegate invocation.
    /// Uses registered converters to transform context data into parameter values.
    /// </summary>
    public class PipelineArgumentResolver
    {
        private readonly List<IPipelineArgumentConverter> _converters;

        public PipelineArgumentResolver()
        {
            // Register converters in priority order
            _converters = new List<IPipelineArgumentConverter>
            {
                new OdooEnvironmentConverter(),
                new IEnvironmentConverter(),
                new ModelNameConverter(),
                new RecordSetConverter(),
                new RecordHandleConverter(),
                new RecordValuesConverter(),
                new IdsEnumerableConverter(),
                new DictionaryConverter(),
                new PassThroughConverter(),
            };

            // Sort by priority descending
            _converters.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        /// <summary>
        /// Register a custom converter.
        /// </summary>
        public void RegisterConverter(IPipelineArgumentConverter converter)
        {
            _converters.Add(converter);
            _converters.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        /// <summary>
        /// Resolve arguments for a delegate invocation.
        /// </summary>
        /// <param name="delegateToInvoke">The delegate to invoke</param>
        /// <param name="context">The invocation context with available data</param>
        /// <returns>Array of arguments ready for DynamicInvoke</returns>
        public object?[] ResolveArguments(
            Delegate delegateToInvoke,
            PipelineInvocationContext context
        )
        {
            var invokeMethod = delegateToInvoke.GetType().GetMethod("Invoke");
            if (invokeMethod == null)
            {
                throw new InvalidOperationException("Cannot get Invoke method from delegate");
            }

            var parameters = invokeMethod.GetParameters();
            var arguments = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var converter = FindConverter(param.ParameterType, param.Name ?? "", context);

                if (converter == null)
                {
                    throw new InvalidOperationException(
                        $"No converter found for parameter '{param.Name}' of type {param.ParameterType.Name}"
                    );
                }

                arguments[i] = converter.Convert(param.ParameterType, param.Name ?? "", context);
            }

            return arguments;
        }

        /// <summary>
        /// Check if all parameters can be resolved for a delegate.
        /// </summary>
        public bool CanResolveAll(Delegate delegateToInvoke, PipelineInvocationContext context)
        {
            var invokeMethod = delegateToInvoke.GetType().GetMethod("Invoke");
            if (invokeMethod == null)
                return false;

            var parameters = invokeMethod.GetParameters();

            // Create a copy of context to test without modifying state
            var testContext = new PipelineInvocationContext(
                context.Environment,
                context.ModelName,
                context.RecordIds,
                context.Args
            );

            foreach (var param in parameters)
            {
                var converter = FindConverter(param.ParameterType, param.Name ?? "", testContext);
                if (converter == null)
                    return false;

                // Simulate conversion to advance args consumed
                converter.Convert(param.ParameterType, param.Name ?? "", testContext);
            }

            return true;
        }

        private IPipelineArgumentConverter? FindConverter(
            Type parameterType,
            string parameterName,
            PipelineInvocationContext context
        )
        {
            foreach (var converter in _converters)
            {
                if (converter.CanConvert(parameterType, parameterName, context))
                {
                    return converter;
                }
            }

            return null;
        }
    }
}
