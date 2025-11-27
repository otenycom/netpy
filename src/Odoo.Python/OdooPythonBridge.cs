using Odoo.Core;
using Python.Runtime;
using System;
using System.Collections.Generic;

namespace Odoo.Python
{
    /// <summary>
    /// Bridge between the C# ORM and Python modules.
    /// Allows Python code to interact with the ORM environment.
    /// </summary>
    public class OdooPythonBridge
    {
        private readonly IEnvironment _environment;
        private readonly PythonModuleLoader _moduleLoader;

        public OdooPythonBridge(IEnvironment environment, PythonModuleLoader moduleLoader)
        {
            _environment = environment;
            _moduleLoader = moduleLoader;
        }

        /// <summary>
        /// Expose the environment to Python for use in custom modules.
        /// </summary>
        public void ExposeEnvironmentToPython()
        {
            _moduleLoader.Initialize();

            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    // Make the environment available to Python code
                    scope.Set("env", _environment.ToPython());
                    scope.Set("user_id", _environment.UserId.ToPython());
                }
            }
        }

        /// <summary>
        /// Execute a Python module method with the ORM environment.
        /// </summary>
        public T ExecuteModuleMethod<T>(string moduleName, string methodName, params object[] args)
        {
            var module = _moduleLoader.LoadModule(moduleName);
            
            using (Py.GIL())
            {
                dynamic method = module.GetAttr(methodName);
                
                // Build arguments array starting with environment
                var allArgs = new dynamic[args.Length + 1];
                allArgs[0] = _environment.ToPython();
                for (int i = 0; i < args.Length; i++)
                {
                    allArgs[i + 1] = args[i].ToPython();
                }
                
                // Call Python function directly with arguments
                dynamic result = args.Length switch
                {
                    0 => method(allArgs[0]),
                    1 => method(allArgs[0], allArgs[1]),
                    2 => method(allArgs[0], allArgs[1], allArgs[2]),
                    3 => method(allArgs[0], allArgs[1], allArgs[2], allArgs[3]),
                    4 => method(allArgs[0], allArgs[1], allArgs[2], allArgs[3], allArgs[4]),
                    _ => throw new NotSupportedException($"Too many arguments ({args.Length}). Maximum 4 supported.")
                };
                
                return result.As<T>();
            }
        }

        /// <summary>
        /// Execute Python code with access to the ORM environment.
        /// </summary>
        public dynamic ExecuteWithEnvironment(string code)
        {
            _moduleLoader.Initialize();

            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    scope.Set("env", _environment.ToPython());
                    scope.Set("user_id", _environment.UserId.ToPython());
                    return scope.Exec(code);
                }
            }
        }

        /// <summary>
        /// Create a Python-based computed field.
        /// Allows defining field computations in Python.
        /// </summary>
        public Func<IOdooRecord, T> CreateComputedField<T>(string pythonCode)
        {
            return (record) =>
            {
                using (Py.GIL())
                {
                    using (var scope = Py.CreateScope())
                    {
                        scope.Set("record", record.ToPython());
                        scope.Set("env", _environment.ToPython());
                        
                        var result = scope.Exec(pythonCode);
                        return result.As<T>();
                    }
                }
            };
        }

        /// <summary>
        /// Register a Python module as a model extension.
        /// This allows adding methods to models from Python.
        /// </summary>
        public void RegisterModelExtension(string modelName, string moduleName)
        {
            var module = _moduleLoader.LoadModule(moduleName);
            
            using (Py.GIL())
            {
                // Store the extension for later use
                // In a real implementation, this would integrate with the model registry
                Console.WriteLine($"Registered Python extension for model '{modelName}' from module '{moduleName}'");
            }
        }
    }

    /// <summary>
    /// Helper class for Python-based field computations and validations.
    /// </summary>
    public static class PythonFieldHelpers
    {
        /// <summary>
        /// Create a validator function from Python code.
        /// </summary>
        public static Func<IOdooRecord, bool> CreateValidator(string pythonCode, PythonModuleLoader loader)
        {
            return (record) =>
            {
                loader.Initialize();
                
                using (Py.GIL())
                {
                    using (var scope = Py.CreateScope())
                    {
                        scope.Set("record", record.ToPython());
                        var result = scope.Exec(pythonCode);
                        return result.As<bool>();
                    }
                }
            };
        }

        /// <summary>
        /// Create a domain filter from Python code.
        /// </summary>
        public static Func<IOdooRecord, bool> CreateDomainFilter(string pythonExpression, PythonModuleLoader loader)
        {
            return (record) =>
            {
                loader.Initialize();
                
                using (Py.GIL())
                {
                    using (var scope = Py.CreateScope())
                    {
                        scope.Set("record", record.ToPython());
                        var result = scope.Eval(pythonExpression);
                        return result.As<bool>();
                    }
                }
            };
        }
    }
}