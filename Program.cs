using Python.Runtime;
using System;
using System.IO;

class Program
{
    static void Main()
    {
        // Set path to your Python DLL (adjust based on installation)
        //Runtime.PythonDLL = @"C:\Python312\python312.dll";  // Example path; update as needed
        Runtime.PythonDLL = "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3";  // Replace with the actual path from the Terminal command
        

        // Disable frozen modules for better debugpy compatibility (Python 3.11+)
        // This prevents the debugger from missing breakpoints
        Environment.SetEnvironmentVariable("PYTHONFROZENMODULES", "0");
        Environment.SetEnvironmentVariable("PYDEVD_DISABLE_FILE_VALIDATION", "1");
        
        var args = new string[] { "-X", "frozen_modules=off" };  // Or include script/module if needed
        PythonEngine.Initialize(args, setSysArgv: true, initSigs: false);

        using (Py.GIL())
        {
            // https://github.com/pythonnet/pythonnet/issues/2282
            Python.Runtime.RuntimeData.FormatterType = typeof(Python.Runtime.NoopFormatter);


            // Add script path to sys.path
            dynamic sys = Py.Import("sys");
            string scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts");
            sys.path.append(scriptPath);

            // Import and call Python function ( .NET -> Python )
            dynamic sample = Py.Import("sample");
            string greeting = sample.greet_from_python("User");
            Console.WriteLine($"Received from Python: {greeting}");

            // Pass .NET object to Python for modification (bidirectional)
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

        PythonEngine.Shutdown(); 
    }
}

public class MessageContainer
{
    public string Message { get; set; } = "";
    public int Counter { get; set; } = 0;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsProcessed { get; set; } = false;
}
