namespace Odoo.Core.Cli
{
    /// <summary>
    /// Interface for CLI commands that can be executed by OdooApp.
    /// Commands are discovered from loaded assemblies.
    /// </summary>
    public interface ICliCommand
    {
        /// <summary>
        /// The command name used on the command line (e.g., "shell", "webhost").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Short description of what the command does.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Execute the command.
        /// </summary>
        /// <param name="env">The configured OdooEnvironment.</param>
        /// <param name="args">Command-line arguments (after the command name).</param>
        /// <returns>Exit code (0 for success).</returns>
        int Execute(OdooEnvironment env, string[] args);
    }
}
