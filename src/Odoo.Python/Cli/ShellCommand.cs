using System;
using System.IO;
using Odoo.Core;
using Odoo.Core.Cli;
using Odoo.Python;
using Python.Runtime;

namespace Odoo.Python.Cli
{
    /// <summary>
    /// CLI command that starts an interactive Python shell with the ORM environment.
    /// Uses Python's code.interact() for a familiar REPL experience.
    ///
    /// Options:
    ///   -c, --command "code"  Execute Python code and exit
    ///   --python &lt;path&gt;       Specify Python executable path
    /// </summary>
    public class ShellCommand : ICliCommand
    {
        /// <summary>
        /// Default Python path (same as used in the demo).
        /// </summary>
        private const string DefaultPythonPath =
            "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3";

        /// <summary>
        /// Python code that defines the proxy classes for proper __getitem__ support.
        /// </summary>
        public const string PythonProxyClasses =
            @"
class OdooEnvProxy:
    '''Python wrapper for OdooEnvironmentWrapper that properly implements __getitem__'''
    def __init__(self, csharp_env):
        self._env = csharp_env
    
    @property
    def user_id(self):
        return self._env.UserId
    
    def __getitem__(self, model_name):
        '''Access models via env['model.name'] syntax'''
        return ModelProxy(self._env.__getitem__(model_name))
    
    def __repr__(self):
        return f'<OdooEnvironment user_id={self.user_id}>'
";

        public const string ModelProxyClass =
            @"
class ModelProxy:
    '''Python wrapper for ModelProxyWrapper
    
    All methods (browse, create, search, and custom pipeline methods) are handled
    through __getattr__ which delegates to C# InvokeMethod with arguments.
    This allows extensions to override any method via the pipeline system.
    '''
    def __init__(self, csharp_model):
        self._model = csharp_model
    
    @property
    def model_name(self):
        return self._model.ModelName
    
    def __getattr__(self, name):
        '''Handle ALL method calls - base methods and pipeline methods
        
        Base methods (browse, create, search) and custom pipeline methods
        are all invoked through InvokeMethod, allowing for extensibility.
        '''
        if name.startswith('_'):
            raise AttributeError(name)
        # Check if this is a valid method (includes base methods like browse, create, search)
        if self._model.HasMethod(name):
            # Return a callable that invokes the method with arguments
            def method_wrapper(*args, **kwargs):
                # Convert args to be C# friendly
                converted_args = _convert_args_for_csharp(name, args, kwargs)
                result = self._model.InvokeMethod(name, converted_args)
                # Wrap result if it's a RecordSetWrapper
                if hasattr(result, 'Ids'):
                    return RecordSetProxy(result)
                return result
            return method_wrapper
        raise AttributeError(f""'ModelProxy' object has no attribute '{name}'"")
    
    def __repr__(self):
        return f'<Model {self.model_name}>'

def _convert_args_for_csharp(method_name, args, kwargs):
    '''Convert Python arguments to C#-friendly format'''
    from System.Collections.Generic import Dictionary, List
    from System import String, Object, Int64
    
    converted = []
    
    for arg in args:
        if isinstance(arg, dict):
            # Convert Python dict to C# Dictionary
            csharp_dict = Dictionary[String, Object]()
            for key, value in arg.items():
                csharp_dict[str(key)] = value
            converted.append(csharp_dict)
        elif isinstance(arg, (list, tuple)):
            # Convert Python list to C# List<long> for IDs
            if method_name == 'browse':
                csharp_list = List[Int64]()
                for item in arg:
                    csharp_list.Add(int(item))
                converted.append(csharp_list)
            else:
                # Generic list conversion
                csharp_list = List[Object]()
                for item in arg:
                    csharp_list.Add(item)
                converted.append(csharp_list)
        elif isinstance(arg, int):
            # For browse with single ID, wrap in a list
            if method_name == 'browse':
                csharp_list = List[Int64]()
                csharp_list.Add(int(arg))
                converted.append(csharp_list)
            else:
                converted.append(arg)
        else:
            converted.append(arg)
    
    return converted
";

        public const string RecordSetProxyClass =
            @"
class RecordSetProxy:
    '''Python wrapper for RecordSetWrapper
    
    All methods (write, read, and custom pipeline methods) are handled
    through __getattr__ which delegates to C# InvokeMethod with arguments.
    This allows extensions to override any method via the pipeline system.
    '''
    def __init__(self, csharp_recordset):
        self._recordset = csharp_recordset
    
    @property
    def ids(self):
        return list(self._recordset.Ids)
    
    def __len__(self):
        return self._recordset.__len__()
    
    def __bool__(self):
        return self._recordset.__bool__()
    
    def __iter__(self):
        for rec in self._recordset.__iter__():
            yield RecordProxy(rec)
    
    def __getitem__(self, index):
        return RecordProxy(self._recordset.__getitem__(index))
    
    def __getattr__(self, name):
        '''Handle ALL method calls - base methods and pipeline methods
        
        Base methods (write, read) and custom pipeline methods
        are all invoked through InvokeMethod, allowing for extensibility.
        '''
        if name.startswith('_'):
            raise AttributeError(name)
        # Check if this is a valid method (includes base methods like write, read)
        if self._recordset.HasMethod(name):
            # Return a callable that invokes the method with arguments
            def method_wrapper(*args, **kwargs):
                # Convert args to be C# friendly
                converted_args = _convert_args_for_csharp(name, args, kwargs)
                result = self._recordset.InvokeMethod(name, converted_args)
                # Wrap result if it's a RecordSetWrapper
                if hasattr(result, 'Ids'):
                    return RecordSetProxy(result)
                return result
            return method_wrapper
        # For single record, delegate to record for field access
        if len(self) == 1:
            return getattr(self[0], name)
        # For multiple records, return list of values
        return [getattr(rec, name) for rec in self]
    
    def write(self, vals):
        '''Write values to all records in the set
        
        This method provides type checking and user-friendly error messages.
        '''
        if not isinstance(vals, dict):
            raise TypeError(f'write() expects a dict, got {type(vals).__name__}. Use colon not comma: {{""key"": value}}')
        # Convert Python dict to C# Dictionary and call through InvokeMethod
        converted_args = _convert_args_for_csharp('write', (vals,), {})
        return self._recordset.InvokeMethod('write', converted_args)
    
    def read(self, fields=None):
        '''Read field values from all records'''
        return self._recordset.Read(fields)
    
    def __repr__(self):
        return self._recordset.__repr__()
";

        public const string RecordProxyClass =
            @"
class RecordProxy:
    '''Python wrapper for RecordWrapper
    
    Single record wrapper. Methods are delegated to the C# RecordWrapper.
    '''
    def __init__(self, csharp_record):
        self._record = csharp_record
    
    @property
    def id(self):
        return self._record.Id
    
    def __getattr__(self, name):
        if name.startswith('_'):
            raise AttributeError(name)
        return self._record.__getattr__(name)
    
    def __setattr__(self, name, value):
        if name == '_record':
            super().__setattr__(name, value)
        else:
            self._record.__setattr__(name, value)
    
    def write(self, vals):
        '''Write values to this record'''
        if not isinstance(vals, dict):
            raise TypeError(f'write() expects a dict, got {type(vals).__name__}. Use colon not comma: {{""key"": value}}')
        # Convert Python dict to C# Dictionary
        from System.Collections.Generic import Dictionary
        from System import String, Object
        csharp_dict = Dictionary[String, Object]()
        for key, value in vals.items():
            csharp_dict[str(key)] = value
        return self._record.Write(csharp_dict)
    
    def read(self, fields=None):
        '''Read field values from this record'''
        return self._record.Read(fields)
    
    def __repr__(self):
        return self._record.__repr__()
";

        /// <summary>
        /// All proxy classes combined.
        /// </summary>
        public static string AllProxyClasses =>
            PythonProxyClasses + ModelProxyClass + RecordSetProxyClass + RecordProxyClass;

        public string Name => "shell";
        public string Description =>
            "Start interactive Python shell with ORM environment (-c to run command)";

        public int Execute(OdooEnvironment env, string[] args)
        {
            // Check for -c / --command argument
            string? commandToExecute = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-c" || args[i] == "--command")
                {
                    commandToExecute = args[i + 1];
                    break;
                }
            }

            if (commandToExecute != null)
            {
                return ExecuteCommand(env, commandToExecute, args);
            }

            return StartInteractiveShell(env, args);
        }

        /// <summary>
        /// Execute a single Python command and return.
        /// Used for -c option and testing.
        /// </summary>
        public int ExecuteCommand(OdooEnvironment env, string command, string[]? args = null)
        {
            try
            {
                InitializePython(args ?? Array.Empty<string>());

                using (Py.GIL())
                {
                    RuntimeData.FormatterType = typeof(NoopFormatter);

                    using var scope = Py.CreateScope("netpy_exec");

                    var envWrapper = new OdooEnvironmentWrapper(env);
                    scope.Set("env", envWrapper.ToPython());
                    scope.Set("user_id", env.UserId);

                    // Define proxy classes
                    scope.Exec(AllProxyClasses);

                    // Wrap the environment
                    scope.Exec("env = OdooEnvProxy(env)");

                    // Execute the user command
                    scope.Exec(command);
                }

                return 0;
            }
            catch (PythonException ex)
            {
                Console.WriteLine($"Python error: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Execute Python code and return a result.
        /// Used for testing.
        /// </summary>
        public object? ExecuteExpression(
            OdooEnvironment env,
            string expression,
            string[]? args = null
        )
        {
            InitializePython(args ?? Array.Empty<string>());

            using (Py.GIL())
            {
                RuntimeData.FormatterType = typeof(NoopFormatter);

                using var scope = Py.CreateScope("netpy_eval");

                var envWrapper = new OdooEnvironmentWrapper(env);
                scope.Set("env", envWrapper.ToPython());
                scope.Set("user_id", env.UserId);

                // Define proxy classes
                scope.Exec(AllProxyClasses);

                // Wrap the environment
                scope.Exec("env = OdooEnvProxy(env)");

                // Evaluate the expression
                var result = scope.Eval(expression);
                return result?.AsManagedObject(typeof(object));
            }
        }

        private int StartInteractiveShell(OdooEnvironment env, string[] args)
        {
            Console.WriteLine("NetPy Shell");
            Console.WriteLine("Initializing Python...");

            try
            {
                InitializePython(args);

                using (Py.GIL())
                {
                    RuntimeData.FormatterType = typeof(NoopFormatter);

                    using var scope = Py.CreateScope("netpy_shell");

                    var envWrapper = new OdooEnvironmentWrapper(env);
                    scope.Set("env", envWrapper.ToPython());
                    scope.Set("user_id", env.UserId);

                    // Import useful modules
                    scope.Exec("from datetime import datetime, date, timedelta");

                    // Print banner
                    Console.WriteLine($"Python {GetPythonVersion()} with OdooEnvironment");
                    Console.WriteLine($"User ID: {env.UserId}");
                    Console.WriteLine();
                    Console.WriteLine("Available globals:");
                    Console.WriteLine("  env     - OdooEnvironment for model access");
                    Console.WriteLine("            Usage: env['res.partner'].browse(1)");
                    Console.WriteLine();
                    Console.WriteLine("Type 'exit()' or Ctrl+D to quit.");
                    Console.WriteLine();

                    // Start interactive shell
                    scope.Exec(
                        AllProxyClasses
                            + @"
import code

# Wrap the C# environment object
env = OdooEnvProxy(env)

# Create a custom interactive console
_local_ns = {'env': env, 'user_id': user_id}
_banner = ''  # We already printed our banner

# Use code.interact for a simple interactive shell
code.interact(banner=_banner, local=_local_ns, exitmsg='Goodbye!')
"
                    );
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting shell: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Make sure Python is properly configured.");
                Console.WriteLine("You can specify the Python path with --python <path>");
                Console.WriteLine($"Default: {DefaultPythonPath}");
                return 1;
            }
        }

        private void InitializePython(string[] args)
        {
            if (PythonEngine.IsInitialized)
                return;

            // Check for --python argument
            string? pythonPath = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--python")
                {
                    pythonPath = args[i + 1];
                    break;
                }
            }

            // Use default if not specified
            pythonPath ??= GetPythonPathFromEnvironment() ?? DefaultPythonPath;

            // Set the Python DLL path
            if (File.Exists(pythonPath))
            {
                Runtime.PythonDLL = pythonPath;
            }
            else
            {
                // Try to find Python in common locations
                var commonPaths = new[]
                {
                    "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3",
                    "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3",
                    "/usr/local/bin/python3",
                    "/usr/bin/python3",
                };

                foreach (var path in commonPaths)
                {
                    if (File.Exists(path))
                    {
                        Runtime.PythonDLL = path;
                        break;
                    }
                }
            }

            // Set environment variables to avoid issues
            Environment.SetEnvironmentVariable("PYTHONFROZENMODULES", "0");
            Environment.SetEnvironmentVariable("PYDEVD_DISABLE_FILE_VALIDATION", "1");

            // Initialize with appropriate arguments
            var pythonArgs = new string[] { "-X", "frozen_modules=off" };
            PythonEngine.Initialize(pythonArgs, setSysArgv: true, initSigs: false);
        }

        private string? GetPythonPathFromEnvironment()
        {
            // Check PYTHON_DLL environment variable
            var envPath = Environment.GetEnvironmentVariable("PYTHON_DLL");
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            {
                return envPath;
            }

            // Check PYTHONHOME
            var pythonHome = Environment.GetEnvironmentVariable("PYTHONHOME");
            if (!string.IsNullOrEmpty(pythonHome))
            {
                var possible = Path.Combine(pythonHome, "bin", "python3");
                if (File.Exists(possible))
                {
                    return possible;
                }
            }

            return null;
        }

        private string GetPythonVersion()
        {
            try
            {
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    return sys.version.ToString().Split(' ')[0];
                }
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
