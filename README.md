# netpy

A .NET project demonstrating bidirectional interoperability between C# and Python using [pythonnet](https://github.com/pythonnet/pythonnet).

## Features

- Call Python functions from C#
- Pass .NET objects to Python for processing
- Bidirectional data exchange between C# and Python

## Prerequisites

- .NET 10.0 SDK
- Python 3.12
- pythonnet package (installed via NuGet)

## Project Structure

```
netpy/
├── Program.cs           # Main C# program with Python interop
├── Scripts/
│   └── sample.py       # Python module with sample functions
├── .vscode/
│   ├── launch.json     # VSCode debug configuration
│   └── tasks.json      # Build tasks
└── netpy.csproj        # Project file
```

## Setup

1. Install dependencies:
   ```bash
   dotnet restore
   ```

2. Update the Python DLL path in `Program.cs` line 11 to match your Python installation:
   ```csharp
   Runtime.PythonDLL = "/path/to/your/python3";
   ```

3. Build the project:
   ```bash
   dotnet build
   ```

## Running

Run the project:
```bash
dotnet run
```

## Debugging in VSCode

1. Open the project in VSCode
2. Press `F5` or go to Run and Debug
3. Select ".NET Core Launch (console)"
4. Set breakpoints as needed

The debug configuration automatically suppresses module loading messages for a cleaner output.

## How It Works

The project demonstrates:

1. **Python → C#**: Calling Python functions and receiving return values
2. **C# → Python**: Passing .NET objects to Python for modification
3. **Bidirectional**: Complete interoperability between both languages

See [`Program.cs`](Program.cs) and [`Scripts/sample.py`](Scripts/sample.py) for implementation details.

## License

MIT