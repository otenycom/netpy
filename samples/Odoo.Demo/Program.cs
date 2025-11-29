using Odoo.Core.Cli;

// Unified CLI entry point - all commands discovered automatically
// Usage:
//   dotnet run -- shell      Start interactive Python shell
//   dotnet run -- webhost    Start REST API server (future)
//   dotnet run -- demo       Run demo menu (legacy demos)
//   dotnet run -- --help     Show available commands

OdooApp.Run(args);
