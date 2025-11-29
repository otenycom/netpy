using System;
using System.Collections.Generic;
using System.IO;
using Python.Runtime;

namespace Odoo.Python
{
    /// <summary>
    /// Loads and manages Python-based Odoo modules.
    /// This allows extending the ORM with custom business logic in Python.
    /// </summary>
    public class PythonModuleLoader : IDisposable
    {
        private readonly string _modulesPath;
        private readonly Dictionary<string, dynamic> _loadedModules = new();
        private bool _isInitialized = false;

        public PythonModuleLoader(string modulesPath)
        {
            _modulesPath = modulesPath;
        }

        /// <summary>
        /// Initialize the Python runtime and set up module paths.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
                return;

            if (!PythonEngine.IsInitialized)
            {
                PythonEngine.Initialize();
            }

            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");

                // Add modules path to Python path
                if (Directory.Exists(_modulesPath))
                {
                    sys.path.append(_modulesPath);
                }

                // Set up any required Python formatter
                global::Python.Runtime.RuntimeData.FormatterType =
                    typeof(global::Python.Runtime.NoopFormatter);
            }

            _isInitialized = true;
        }

        /// <summary>
        /// Load a Python module by name.
        /// </summary>
        public dynamic LoadModule(string moduleName)
        {
            Initialize();

            if (_loadedModules.TryGetValue(moduleName, out var cachedModule))
            {
                return cachedModule;
            }

            using (Py.GIL())
            {
                try
                {
                    dynamic module = Py.Import(moduleName);
                    _loadedModules[moduleName] = module;
                    return module;
                }
                catch (PythonException ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to load Python module '{moduleName}': {ex.Message}",
                        ex
                    );
                }
            }
        }

        /// <summary>
        /// Execute Python code in the context of a loaded module.
        /// </summary>
        public dynamic ExecuteCode(string code, Dictionary<string, object>? globals = null)
        {
            Initialize();

            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    if (globals != null)
                    {
                        foreach (var (key, value) in globals)
                        {
                            scope.Set(key, value.ToPython());
                        }
                    }

                    return scope.Exec(code);
                }
            }
        }

        /// <summary>
        /// Call a Python function with arguments.
        /// </summary>
        public T CallFunction<T>(string moduleName, string functionName, params object[] args)
        {
            var module = LoadModule(moduleName);

            using (Py.GIL())
            {
                dynamic func = module.GetAttr(functionName);

                // Convert arguments to Python objects
                var pyArgs = new dynamic[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    pyArgs[i] = args[i].ToPython();
                }

                // Call Python function directly with arguments expanded
                dynamic result = args.Length switch
                {
                    0 => func(),
                    1 => func(pyArgs[0]),
                    2 => func(pyArgs[0], pyArgs[1]),
                    3 => func(pyArgs[0], pyArgs[1], pyArgs[2]),
                    4 => func(pyArgs[0], pyArgs[1], pyArgs[2], pyArgs[3]),
                    5 => func(pyArgs[0], pyArgs[1], pyArgs[2], pyArgs[3], pyArgs[4]),
                    _ => throw new NotSupportedException(
                        $"Too many arguments ({args.Length}). Maximum 5 supported."
                    ),
                };

                return result.As<T>();
            }
        }

        /// <summary>
        /// Reload a module (useful for development).
        /// </summary>
        public void ReloadModule(string moduleName)
        {
            if (_loadedModules.Remove(moduleName))
            {
                using (Py.GIL())
                {
                    dynamic importlib = Py.Import("importlib");
                    var module = LoadModule(moduleName);
                    importlib.reload(module);
                }
            }
        }

        public void Dispose()
        {
            _loadedModules.Clear();

            if (PythonEngine.IsInitialized)
            {
                // Note: Be careful with PythonEngine.Shutdown() as it can only be called once
                // In a real application, you might want to manage this at the application level
            }
        }
    }
}
