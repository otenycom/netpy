using Python.Runtime;
using System;
using System.IO;
using Odoo.Examples;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║         Odoo-Style ORM for C# - Demo Application          ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        string? choice;
        
        // Check if choice was provided via command line
        if (args.Length > 0)
        {
            choice = args[0];
            Console.WriteLine($"Running demo: {choice}");
        }
        else
        {
            // Show interactive menu
            Console.WriteLine("Select a demo to run:");
            Console.WriteLine("  1. Basic ORM Usage (without Python)");
            Console.WriteLine("  2. Python Integration Demo");
            Console.WriteLine("  3. Run Both Demos");
            Console.WriteLine("  4. Original Python.NET Demo");
            Console.WriteLine("  5. Modularity & Pipeline Demo");
            Console.WriteLine();
            Console.Write("Enter your choice (1-5): ");

            choice = Console.ReadLine();
        }
        Console.WriteLine();

        switch (choice)
        {
            case "1":
                RunBasicDemo();
                break;
            case "2":
                RunPythonDemo();
                break;
            case "3":
                RunBasicDemo();
                Console.WriteLine("\n" + new string('=', 60) + "\n");
                RunPythonDemo();
                break;
            case "4":
                RunOriginalDemo();
                break;
            case "5":
                RunModularityDemo();
                break;
            default:
                Console.WriteLine("Invalid choice. Running Basic Demo...");
                RunBasicDemo();
                break;
        }

        // Console.WriteLine("\nPress any key to exit...");
        // if (!Console.IsInputRedirected)
        // {
        //     Console.ReadKey();
        // }
    }

    static void RunBasicDemo()
    {
        try
        {
            BasicUsageDemo.RunDemo();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running basic demo: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static void RunPythonDemo()
    {
        try
        {
            // Initialize Python
            if (!PythonEngine.IsInitialized)
            {
                // Set path to your Python DLL (adjust based on installation)
                Runtime.PythonDLL = "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3";
                
                Environment.SetEnvironmentVariable("PYTHONFROZENMODULES", "0");
                Environment.SetEnvironmentVariable("PYDEVD_DISABLE_FILE_VALIDATION", "1");
                
                var pythonArgs = new string[] { "-X", "frozen_modules=off" };
                PythonEngine.Initialize(pythonArgs, setSysArgv: true, initSigs: false);
            }

            using (Py.GIL())
            {
                global::Python.Runtime.RuntimeData.FormatterType = typeof(global::Python.Runtime.NoopFormatter);
                
                // Run the Python integration demo
                PythonIntegrationDemo.RunDemo();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running Python demo: {ex.Message}");
            Console.WriteLine("\nNote: Make sure Python is properly configured.");
            Console.WriteLine("Update the Runtime.PythonDLL path in Program.cs if needed.");
        }
    }

    static void RunOriginalDemo()
    {
        try
        {
            if (!PythonEngine.IsInitialized)
            {
                Runtime.PythonDLL = "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3";
                Environment.SetEnvironmentVariable("PYTHONFROZENMODULES", "0");
                Environment.SetEnvironmentVariable("PYDEVD_DISABLE_FILE_VALIDATION", "1");
                
                var pythonArgs = new string[] { "-X", "frozen_modules=off" };
                PythonEngine.Initialize(pythonArgs, setSysArgv: true, initSigs: false);
            }

            using (Py.GIL())
            {
                global::Python.Runtime.RuntimeData.FormatterType = typeof(global::Python.Runtime.NoopFormatter);

                dynamic sys = Py.Import("sys");
                string scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts");
                sys.path.append(scriptPath);

                dynamic sample = Py.Import("sample");
                string greeting = sample.greet_from_python("User");
                Console.WriteLine($"Received from Python: {greeting}");

                var dotnetObj = new MessageContainer { Message = "Original from .NET" };
                Console.WriteLine($"\n.NET object before Python modification:");
                Console.WriteLine($"  Message: {dotnetObj.Message}");
                Console.WriteLine($"  Counter: {dotnetObj.Counter}");
                Console.WriteLine($"  Timestamp: {dotnetObj.Timestamp}");
                Console.WriteLine($"  IsProcessed: {dotnetObj.IsProcessed}");
                
                dynamic pyObj = dotnetObj.ToPython();
                string modified = sample.process_dotnet_object(pyObj);
                
                Console.WriteLine($"\n.NET object after Python modification:");
                Console.WriteLine($"  Message: {dotnetObj.Message}");
                Console.WriteLine($"  Counter: {dotnetObj.Counter}");
                Console.WriteLine($"  Timestamp: {dotnetObj.Timestamp}");
                Console.WriteLine($"  IsProcessed: {dotnetObj.IsProcessed}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void RunModularityDemo()
    {
        try
        {
            ModularityDemo.RunDemo();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running modularity demo: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}

public class MessageContainer
{
    public string Message { get; set; } = "";
    public int Counter { get; set; } = 0;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsProcessed { get; set; } = false;
}
