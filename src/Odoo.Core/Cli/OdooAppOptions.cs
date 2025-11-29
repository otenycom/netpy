namespace Odoo.Core.Cli
{
    /// <summary>
    /// Configuration options for OdooApp.
    /// </summary>
    public class OdooAppOptions
    {
        /// <summary>
        /// Default user ID for the environment.
        /// </summary>
        public int DefaultUserId { get; set; } = 1;

        /// <summary>
        /// Default command to run if none specified.
        /// </summary>
        public string DefaultCommand { get; set; } = "shell";

        /// <summary>
        /// Default port for the webhost command.
        /// </summary>
        public int WebhostPort { get; set; } = 8233;

        /// <summary>
        /// Path to Python executable or DLL.
        /// If null, uses default detection logic.
        /// </summary>
        public string? PythonPath { get; set; }

        /// <summary>
        /// Enable verbose logging.
        /// </summary>
        public bool Verbose { get; set; } = false;
    }
}
