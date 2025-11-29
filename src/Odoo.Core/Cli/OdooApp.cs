using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Odoo.Core.Cli
{
    /// <summary>
    /// Main entry point for NetPy applications.
    /// Provides unified CLI with command discovery and execution.
    ///
    /// <para><b>Usage:</b></para>
    /// <code>
    /// public static void Main(string[] args) => OdooApp.Run(args);
    /// </code>
    /// </summary>
    public static class OdooApp
    {
        private static readonly Dictionary<string, ICliCommand> _commands = new();
        private static bool _commandsDiscovered = false;

        /// <summary>
        /// Run the CLI with default options.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Run(string[] args)
        {
            Run(args, _ => { });
        }

        /// <summary>
        /// Run the CLI with custom options.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="configure">Action to configure options.</param>
        public static void Run(string[] args, Action<OdooAppOptions> configure)
        {
            var options = new OdooAppOptions();
            configure(options);

            try
            {
                // Discover commands from loaded assemblies
                DiscoverCommands();

                // Parse arguments
                var (commandName, commandArgs, parsedOptions) = ParseArguments(args, options);

                // Handle special commands
                if (commandName == "--help" || commandName == "-h")
                {
                    PrintHelp();
                    return;
                }

                if (commandName == "--version" || commandName == "-v")
                {
                    PrintVersion();
                    return;
                }

                // Build environment
                var env = BuildEnvironment(parsedOptions);

                // Find and execute command
                if (!_commands.TryGetValue(commandName, out var command))
                {
                    Console.WriteLine($"Unknown command: {commandName}");
                    Console.WriteLine();
                    PrintHelp();
                    Environment.Exit(1);
                    return;
                }

                if (options.Verbose)
                {
                    Console.WriteLine($"[NetPy] Executing command: {commandName}");
                }

                var exitCode = command.Execute(env, commandArgs);
                Environment.Exit(exitCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (options.Verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Register a command manually (useful for testing or custom commands).
        /// </summary>
        public static void RegisterCommand(ICliCommand command)
        {
            _commands[command.Name] = command;
        }

        /// <summary>
        /// Discover all ICliCommand implementations from loaded assemblies.
        /// </summary>
        private static void DiscoverCommands()
        {
            if (_commandsDiscovered)
                return;

            // Force load referenced assemblies that might contain commands
            ForceLoadReferencedAssemblies();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    if (assembly.IsDynamic)
                        continue;

                    var name = assembly.GetName().Name;
                    if (name == null)
                        continue;

                    // Skip system assemblies
                    if (
                        name.StartsWith("System", StringComparison.Ordinal)
                        || name.StartsWith("Microsoft", StringComparison.Ordinal)
                        || name.StartsWith("netstandard", StringComparison.Ordinal)
                    )
                        continue;

                    var commandTypes = assembly
                        .GetTypes()
                        .Where(t =>
                            !t.IsAbstract
                            && !t.IsInterface
                            && typeof(ICliCommand).IsAssignableFrom(t)
                        );

                    foreach (var type in commandTypes)
                    {
                        try
                        {
                            var command = (ICliCommand)Activator.CreateInstance(type)!;
                            _commands[command.Name] = command;
                        }
                        catch
                        {
                            // Skip commands that fail to instantiate
                        }
                    }
                }
                catch
                {
                    // Skip assemblies that can't be inspected
                }
            }

            _commandsDiscovered = true;
        }

        /// <summary>
        /// Force load all referenced assemblies to ensure command discovery finds all commands.
        /// This is needed because .NET lazy-loads assemblies.
        /// </summary>
        private static void ForceLoadReferencedAssemblies()
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly == null)
                return;

            var loadedAssemblies = new HashSet<string>(
                AppDomain
                    .CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .Select(a => a.GetName().Name ?? "")
            );

            foreach (var reference in entryAssembly.GetReferencedAssemblies())
            {
                if (loadedAssemblies.Contains(reference.Name ?? ""))
                    continue;

                // Skip system assemblies
                if (
                    reference.Name?.StartsWith("System") == true
                    || reference.Name?.StartsWith("Microsoft") == true
                )
                    continue;

                try
                {
                    Assembly.Load(reference);
                }
                catch
                {
                    // Ignore load failures
                }
            }
        }

        /// <summary>
        /// Parse command-line arguments.
        /// </summary>
        private static (
            string CommandName,
            string[] CommandArgs,
            OdooAppOptions Options
        ) ParseArguments(string[] args, OdooAppOptions options)
        {
            var commandArgs = new List<string>();
            string? commandName = null;
            var i = 0;

            while (i < args.Length)
            {
                var arg = args[i];

                if (commandName == null && !arg.StartsWith("-"))
                {
                    commandName = arg;
                    i++;
                    continue;
                }

                switch (arg)
                {
                    case "--user-id":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var userId))
                        {
                            options.DefaultUserId = userId;
                            i += 2;
                        }
                        else
                        {
                            throw new ArgumentException("--user-id requires an integer value");
                        }
                        break;

                    case "--verbose":
                        options.Verbose = true;
                        i++;
                        break;

                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var port))
                        {
                            options.WebhostPort = port;
                            i += 2;
                        }
                        else
                        {
                            throw new ArgumentException("--port requires an integer value");
                        }
                        break;

                    case "--python":
                        if (i + 1 < args.Length)
                        {
                            options.PythonPath = args[i + 1];
                            i += 2;
                        }
                        else
                        {
                            throw new ArgumentException("--python requires a path value");
                        }
                        break;

                    case "--help":
                    case "-h":
                        if (commandName == null)
                        {
                            commandName = "--help";
                        }
                        else
                        {
                            commandArgs.Add(arg);
                        }
                        i++;
                        break;

                    case "--version":
                    case "-v":
                        if (commandName == null)
                        {
                            commandName = "--version";
                        }
                        else
                        {
                            commandArgs.Add(arg);
                        }
                        i++;
                        break;

                    default:
                        // Pass through to command
                        commandArgs.Add(arg);
                        i++;
                        break;
                }
            }

            return (commandName ?? options.DefaultCommand, commandArgs.ToArray(), options);
        }

        /// <summary>
        /// Build the OdooEnvironment with discovered addons.
        /// </summary>
        private static OdooEnvironment BuildEnvironment(OdooAppOptions options)
        {
            return new OdooEnvironmentBuilder().WithUserId(options.DefaultUserId).Build();
        }

        /// <summary>
        /// Print help message with available commands.
        /// </summary>
        private static void PrintHelp()
        {
            Console.WriteLine("NetPy - Odoo-style ORM for .NET");
            Console.WriteLine();
            Console.WriteLine("Usage: dotnet run -- <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");

            // Ensure commands are discovered
            DiscoverCommands();

            if (_commands.Count == 0)
            {
                Console.WriteLine(
                    "  (no commands available - make sure Odoo.Python is referenced)"
                );
            }
            else
            {
                foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
                {
                    Console.WriteLine($"  {cmd.Name, -15} {cmd.Description}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Global Options:");
            Console.WriteLine("  --user-id <id>    Set user ID for the environment (default: 1)");
            Console.WriteLine("  --verbose         Enable verbose logging");
            Console.WriteLine("  --python <path>   Path to Python executable or DLL");
            Console.WriteLine("  --help, -h        Show this help message");
            Console.WriteLine("  --version, -v     Show version information");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- shell");
            Console.WriteLine("  dotnet run -- webhost --port 9000");
            Console.WriteLine("  dotnet run -- shell --user-id 2");
        }

        /// <summary>
        /// Print version information.
        /// </summary>
        private static void PrintVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            Console.WriteLine($"NetPy {version}");
            Console.WriteLine("Odoo-style ORM for .NET");
        }
    }
}
