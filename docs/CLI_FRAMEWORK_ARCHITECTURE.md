# NetPy CLI Framework Architecture

## Overview

The **NetPy CLI Framework** provides a unified command-line entry point for all NetPy applications. It replaces custom `Program.cs` implementations with a consistent pattern that supports multiple runtime modes:

- **shell**: Interactive Python REPL with access to the ORM environment
- **webhost**: REST API server compatible with Odoo's `call_kw` interface

## Design Principles

| Principle | Description |
|-----------|-------------|
| **Single Entry Point** | `OdooApp.Run(args)` replaces custom Program.cs logic |
| **Odoo Compatible** | Shell and API mirror Odoo's patterns for familiarity |
| **Python-First Shell** | Interactive shell uses Python via Python.NET |
| **Type-Safe Core** | C# ORM with full IntelliSense, exposed to Python |
| **Extensible Commands** | Easy to add new commands in the future |

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         NETPY CLI FRAMEWORK                              │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Entry Point                                                             │
│  ───────────                                                             │
│  public static void Main(string[] args) => OdooApp.Run(args);           │
│                                                                          │
│       │                                                                  │
│       ▼                                                                  │
│  ┌──────────────────┐                                                    │
│  │ 1. PARSE ARGS    │  Determine command: shell, webhost, etc.          │
│  └────────┬─────────┘                                                    │
│           │                                                              │
│           ▼                                                              │
│  ┌──────────────────┐                                                    │
│  │ 2. BUILD ENV     │  OdooEnvironmentBuilder discovers addons          │
│  │                  │  Scans assemblies, registers pipelines            │
│  └────────┬─────────┘                                                    │
│           │                                                              │
│      ┌────┴────────────────────┐                                         │
│      │                         │                                         │
│      ▼                         ▼                                         │
│  ┌──────────┐           ┌──────────────┐                                 │
│  │  shell   │           │   webhost    │                                 │
│  │          │           │              │                                 │
│  │ Python   │           │ REST API     │                                 │
│  │ REPL     │           │ call_kw      │                                 │
│  │ with env │           │ port 8233    │                                 │
│  └──────────┘           └──────────────┘                                 │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

## CLI Interface

### Usage

```bash
# Start interactive Python shell
dotnet run -- shell

# Start REST API webhost
dotnet run -- webhost

# Start webhost on custom port
dotnet run -- webhost --port 9000

# Show help
dotnet run -- --help
```

### Commands

| Command | Description |
|---------|-------------|
| `shell` | Start interactive Python REPL with `env` global |
| `webhost` | Start REST API server for `call_kw` |
| `--help` | Show available commands and options |
| `--version` | Show version information |

### Global Options

| Option | Description | Default |
|--------|-------------|---------|
| `--user-id <id>` | User ID for the environment | `1` |
| `--verbose` | Enable verbose logging | `false` |

## Entry Point Pattern

### Before (Custom Program.cs)

```csharp
class Program
{
    static void Main(string[] args)
    {
        // Custom initialization logic
        var env = new OdooEnvironmentBuilder()
            .WithUserId(1)
            .Build();
        
        // Custom demo selection
        Console.WriteLine("Select a demo...");
        // ... lots of custom code
    }
}
```

### After (Unified OdooApp)

```csharp
class Program
{
    static void Main(string[] args) => OdooApp.Run(args);
}
```

Or with configuration:

```csharp
class Program
{
    static void Main(string[] args)
    {
        OdooApp.Run(args, options =>
        {
            options.DefaultUserId = 1;
            options.DefaultCommand = "shell";  // or "webhost"
        });
    }
}
```

## Shell Command

The `shell` command starts an interactive Python REPL with full access to the ORM environment.

### Features

- **`env` global**: OdooEnvironment exposed as Python object
- **Model browsing**: `env['res.partner'].browse(1)`
- **Method calls**: `partner.write({'name': 'New Name'})`
- **Tab completion**: Autocomplete for model names and fields
- **History**: Command history with up/down arrows

### Implementation

```csharp
public class ShellCommand : ICliCommand
{
    public string Name => "shell";
    public string Description => "Start interactive Python shell";
    
    public int Execute(OdooEnvironment env, string[] args)
    {
        // Initialize Python
        InitializePython();
        
        using (Py.GIL())
        {
            // Create interactive scope with env
            using var scope = Py.CreateScope();
            
            // Expose environment
            scope.Set("env", new OdooEnvironmentWrapper(env));
            
            // Start REPL
            StartInteractiveShell(scope);
        }
        
        return 0;
    }
}
```

### Python Environment Wrapper

To make the C# environment work naturally in Python, we create a wrapper:

```csharp
/// <summary>
/// Wrapper that exposes OdooEnvironment to Python with Odoo-like API.
/// </summary>
public class OdooEnvironmentWrapper
{
    private readonly OdooEnvironment _env;
    
    public OdooEnvironmentWrapper(OdooEnvironment env)
    {
        _env = env;
    }
    
    /// <summary>
    /// Python: env['res.partner']
    /// Returns a ModelProxy for the model.
    /// </summary>
    public object this[string modelName] => new ModelProxyWrapper(_env, modelName);
    
    /// <summary>
    /// Python: env.user_id
    /// </summary>
    public int UserId => _env.UserId;
    
    /// <summary>
    /// Python: env.cr (placeholder for future cursor)
    /// </summary>
    public object Cr => null;
}

/// <summary>
/// Wrapper for model access from Python.
/// </summary>
public class ModelProxyWrapper
{
    private readonly OdooEnvironment _env;
    private readonly string _modelName;
    
    public ModelProxyWrapper(OdooEnvironment env, string modelName)
    {
        _env = env;
        _modelName = modelName;
    }
    
    /// <summary>
    /// Python: env['res.partner'].browse(1)
    /// </summary>
    public object Browse(params int[] ids)
    {
        // Return RecordSet wrapper
        return new RecordSetWrapper(_env, _modelName, ids);
    }
    
    /// <summary>
    /// Python: env['res.partner'].create({'name': 'Test'})
    /// </summary>
    public object Create(IDictionary<string, object> vals)
    {
        // Call create pipeline
        // ...
    }
    
    /// <summary>
    /// Python: env['res.partner'].search([('name', '=', 'Test')])
    /// </summary>
    public object Search(object domain)
    {
        // Future: implement domain search
        // ...
    }
}
```

### Shell Session Example

```
$ dotnet run -- shell

NetPy Shell 1.0
Python 3.12 with OdooEnvironment
Type 'exit()' to quit.

>>> env
<OdooEnvironment user_id=1>

>>> env['res.partner']
<Model res.partner>

>>> partner = env['res.partner'].browse(1)
>>> partner.name
'Azure Interior'

>>> partner.write({'name': 'Updated Name'})
True

>>> partner.name
'Updated Name'

>>> # Access sale extension fields
>>> partner.is_customer
True

>>> partner.credit_limit
5000.0

>>> exit()
$
```

## Webhost Command

The `webhost` command starts a REST API server compatible with Odoo's JSON-RPC `call_kw` interface.

### Features

- **POST /api/call_kw**: Call any model method with keyword arguments
- **JSON-RPC compatible**: Same protocol as Odoo
- **Authentication**: Basic auth or API keys (future)
- **CORS support**: For web clients

### Default Configuration

| Setting | Value |
|---------|-------|
| Port | `8233` |
| Host | `localhost` |
| Protocol | HTTP |

### API Endpoints

#### POST /api/call_kw

Call a model method with positional and keyword arguments.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "method": "call_kw",
  "params": {
    "model": "res.partner",
    "method": "write",
    "args": [[1, 2, 3]],
    "kwargs": {
      "vals": {
        "name": "Updated Name"
      }
    }
  },
  "id": 1
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "result": true,
  "id": 1
}
```

#### Common Methods

| Model | Method | Description |
|-------|--------|-------------|
| `res.partner` | `read` | Read record fields |
| `res.partner` | `write` | Update records |
| `res.partner` | `create` | Create new record |
| `res.partner` | `unlink` | Delete records |
| `res.partner` | `search` | Search records by domain |
| `res.partner` | `search_read` | Search and read in one call |

### Implementation

```csharp
public class WebhostCommand : ICliCommand
{
    public string Name => "webhost";
    public string Description => "Start REST API server";
    
    public int Execute(OdooEnvironment env, string[] args)
    {
        var port = GetPort(args, defaultPort: 8233);
        
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(env);
        
        var app = builder.Build();
        
        app.MapPost("/api/call_kw", async (HttpContext ctx, OdooEnvironment env) =>
        {
            var request = await ctx.Request.ReadFromJsonAsync<CallKwRequest>();
            var result = ExecuteCallKw(env, request);
            return Results.Json(new { jsonrpc = "2.0", result, id = request.Id });
        });
        
        Console.WriteLine($"NetPy Webhost running on http://localhost:{port}");
        Console.WriteLine("Press Ctrl+C to stop.");
        
        app.Run($"http://localhost:{port}");
        
        return 0;
    }
    
    private object ExecuteCallKw(OdooEnvironment env, CallKwRequest request)
    {
        var model = env[request.Model];
        
        // Get pipeline for the method
        var pipeline = env.Methods.GetPipeline(request.Model, request.Method);
        
        // Execute with args and kwargs
        return pipeline.Invoke(request.Args, request.Kwargs);
    }
}

public class CallKwRequest
{
    public string Jsonrpc { get; set; } = "2.0";
    public string Method { get; set; } = "call_kw";
    public CallKwParams Params { get; set; }
    public int Id { get; set; }
}

public class CallKwParams
{
    public string Model { get; set; }
    public string Method { get; set; }
    public object[] Args { get; set; }
    public Dictionary<string, object> Kwargs { get; set; }
}
```

### Example API Calls

```bash
# Read partner
curl -X POST http://localhost:8233/api/call_kw \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "call_kw",
    "params": {
      "model": "res.partner",
      "method": "read",
      "args": [[1]],
      "kwargs": {"fields": ["name", "email"]}
    },
    "id": 1
  }'

# Response:
{
  "jsonrpc": "2.0",
  "result": [{"id": 1, "name": "Azure Interior", "email": "azure@example.com"}],
  "id": 1
}

# Write partner
curl -X POST http://localhost:8233/api/call_kw \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "call_kw",
    "params": {
      "model": "res.partner",
      "method": "write",
      "args": [[1]],
      "kwargs": {"vals": {"name": "New Name"}}
    },
    "id": 2
  }'

# Create partner
curl -X POST http://localhost:8233/api/call_kw \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "call_kw",
    "params": {
      "model": "res.partner",
      "method": "create",
      "args": [],
      "kwargs": {"vals": {"name": "New Partner", "email": "new@example.com"}}
    },
    "id": 3
  }'
```

## OdooApp Implementation

### Core Structure

```csharp
namespace Odoo.Core.Cli
{
    /// <summary>
    /// Main entry point for NetPy applications.
    /// Provides unified CLI with shell and webhost commands.
    /// </summary>
    public static class OdooApp
    {
        /// <summary>
        /// Run the CLI with default options.
        /// </summary>
        public static void Run(string[] args)
        {
            Run(args, options => { });
        }
        
        /// <summary>
        /// Run the CLI with custom options.
        /// </summary>
        public static void Run(string[] args, Action<OdooAppOptions> configure)
        {
            var options = new OdooAppOptions();
            configure(options);
            
            // Parse command line
            var command = ParseCommand(args, options);
            var env = BuildEnvironment(options);
            
            // Execute command
            var exitCode = command.Execute(env, args);
            Environment.Exit(exitCode);
        }
        
        private static OdooEnvironment BuildEnvironment(OdooAppOptions options)
        {
            return new OdooEnvironmentBuilder()
                .WithUserId(options.DefaultUserId)
                .Build();
        }
        
        private static ICliCommand ParseCommand(string[] args, OdooAppOptions options)
        {
            var commandName = args.Length > 0 ? args[0] : options.DefaultCommand;
            
            return commandName switch
            {
                "shell" => new ShellCommand(),
                "webhost" => new WebhostCommand(),
                "--help" or "-h" => new HelpCommand(),
                "--version" or "-v" => new VersionCommand(),
                _ => throw new Exception($"Unknown command: {commandName}")
            };
        }
    }
    
    public class OdooAppOptions
    {
        public int DefaultUserId { get; set; } = 1;
        public string DefaultCommand { get; set; } = "shell";
        public int WebhostPort { get; set; } = 8233;
    }
    
    public interface ICliCommand
    {
        string Name { get; }
        string Description { get; }
        int Execute(OdooEnvironment env, string[] args);
    }
}
```

## Project Structure

```
src/Odoo.Core/
├── Cli/
│   ├── OdooApp.cs              # Main entry point
│   ├── OdooAppOptions.cs       # Configuration options
│   ├── ICliCommand.cs          # Command interface
│   ├── Commands/
│   │   ├── ShellCommand.cs     # Interactive Python REPL
│   │   ├── WebhostCommand.cs   # REST API server
│   │   ├── HelpCommand.cs      # Help output
│   │   └── VersionCommand.cs   # Version info
│   └── Python/
│       ├── OdooEnvironmentWrapper.cs   # env wrapper for Python
│       ├── ModelProxyWrapper.cs        # Model wrapper
│       ├── RecordSetWrapper.cs         # RecordSet wrapper
│       └── InteractiveShell.cs         # REPL implementation
```

## Dependencies

### Required NuGet Packages

```xml
<!-- For webhost command -->
<PackageReference Include="Microsoft.AspNetCore.App" />

<!-- For Python shell (already in Odoo.Python) -->
<PackageReference Include="pythonnet" Version="3.0.3" />
```

## Migration Guide

### Converting Existing Projects

1. Update `Program.cs`:

```csharp
// Before
class Program
{
    static void Main(string[] args)
    {
        // Custom initialization
        var env = new OdooEnvironmentBuilder().Build();
        // ... custom demo code
    }
}

// After
class Program
{
    static void Main(string[] args) => OdooApp.Run(args);
}
```

2. Run with commands:

```bash
# Start shell
dotnet run -- shell

# Start webhost
dotnet run -- webhost
```

## Future Commands

The CLI framework is designed to be extensible. Future commands could include:

| Command | Description |
|---------|-------------|
| `addon install <name>` | Install addon (from sidecar concept) |
| `addon remove <name>` | Remove addon |
| `db init` | Initialize database schema |
| `db migrate` | Run migrations |
| `test` | Run tests |
| `scaffold <model>` | Generate model boilerplate |

## Summary

The NetPy CLI Framework provides:

- **Unified Entry Point**: `OdooApp.Run(args)` for all NetPy apps
- **Interactive Shell**: Python REPL with `env` global for exploration
- **REST API**: `call_kw` compatible webhost for integrations
- **Extensible**: Easy to add new commands as needed

This approach simplifies application setup and provides familiar Odoo-like interfaces for both interactive use and API access.