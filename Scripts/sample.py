import clr
clr.AddReference("System")
from System import Console, DateTime

def greet_from_python(name):
    Console.WriteLine(f"Python calling .NET: Hello, {name}!")
    return f"Greeting from Python: Welcome, {name}!"

def process_dotnet_object(dotnet_obj):
    # Python accessing and modifying a passed .NET object
    Console.WriteLine(f"Before modification - Message: {dotnet_obj.Message}, Counter: {dotnet_obj.Counter}, IsProcessed: {dotnet_obj.IsProcessed}")
    
    # Modify all properties
    dotnet_obj.Message = "Modified by Python"
    dotnet_obj.Counter = 42
    dotnet_obj.Timestamp = DateTime.Now
    dotnet_obj.IsProcessed = True
    
    Console.WriteLine(f"After modification - Message: {dotnet_obj.Message}, Counter: {dotnet_obj.Counter}, IsProcessed: {dotnet_obj.IsProcessed}")
    
    return dotnet_obj.Message