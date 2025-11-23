import clr
clr.AddReference("System")
from System import Console

def greet_from_python(name):
    Console.WriteLine(f"Python calling .NET: Hello, {name}!")
    return f"Greeting from Python: Welcome, {name}!"

def process_dotnet_object(dotnet_obj):
    # Python accessing and modifying a passed .NET object
    dotnet_obj.Message = "Modified by Python"
    return dotnet_obj.Message