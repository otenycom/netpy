# Python Debugging Guide for Python.NET Embedded Scripts

This document provides practical debugging strategies for Python scripts embedded in .NET applications via Python.NET.

## Important Note About debugpy

**debugpy does not work reliably with embedded Python.NET contexts.**  While debugpy is excellent for standalone Python applications, it causes initialization issues when Python is embedded in .NET applications. The warnings you see are harmless, but remote debugging attachment is problematic in this scenario.

## Recommended Debugging Approaches

### Method 1: Enhanced Logging (Recommended for Quick Debugging)

The simplest and most reliable approach - add detailed print statements:

```python
import sys
from System import Console

def greet_from_python(name):
    # Debug logging
    print(f"[DEBUG] greet_from_python called with name='{name}'", flush=True)
    print(f"[DEBUG] Python version: {sys.version}", flush=True)
    
    Console.WriteLine(f"Python calling .NET: Hello, {name}!")
    result = f"Greeting from Python: Welcome, {name}!"
    
    print(f"[DEBUG] Returning: {result}", flush=True)
    return result
```

**Advantages:**
- Always works
- No setup required
- Fast to implement
- Can be conditionally enabled with environment variables

**Example with conditional logging:**
```python
import os

DEBUG = os.getenv('PYTHON_DEBUG') == '1'

def debug_print(*args, **kwargs):
    if DEBUG:
        print("[DEBUG]", *args, **kwargs, flush=True)

def greet_from_python(name):
    debug_print(f"greet_from_python called with: {name}")
    # ... rest of code
```

Run with: `PYTHON_DEBUG=1 dotnet run`

### Method 2: Using Python's Built-in PDB (Interactive Debugging)

For step-by-step debugging, use Python's built-in debugger:

```python
import pdb

def process_dotnet_object(dotnet_obj):
    pdb.set_trace()  # Execution will pause here
    
    # You can then use pdb commands:
    # n - next line
    # s - step into function
    # c - continue execution
    # p variable_name - print variable
    # l - list code around current line
    
    dotnet_obj.Message = "Modified by Python"
    return dotnet_obj.Message
```

**Advantages:**
- Built into Python, no extra packages
- Full inspect

ion capabilities
- Works in embedded contexts

**Limitations:**
- Terminal-based (no GUI)
- Requires interactive console
- Must modify code to add breakpoints

### Method 3: Develop and Debug Python Separately

Create standalone Python test files that can be debugged normally, then integrate:

**1. Create `Scripts/sample_test.py`:**
```python
#!/usr/bin/env python3
"""
Standalone test file for developing Python logic before embedding.
Run this directly with: python Scripts/sample_test.py
Debug in VSCode with F5 using Python debugger.
"""

class MockConsole:
    @staticmethod
    def WriteLine(msg):
        print(f"[Console] {msg}")

class MockDateTime:
    @staticmethod
    def Now():
        from datetime import datetime
        return datetime.now()

class MockDotNetObject:
    def __init__(self):
        self.Message = "Original"
        self.Counter = 0
        self.Timestamp = None
        self.IsProcessed = False

def greet_from_python(name):
    """Test this function in isolation"""
    Console = MockConsole()
    Console.WriteLine(f"Python calling .NET: Hello, {name}!")
    return f"Greeting from Python: Welcome, {name}!"

def process_dotnet_object(dotnet_obj):
    """Test with mock object"""
    Console = MockConsole()
    Console.WriteLine(f"Before: {dotnet_obj.Message}")
    
    dotnet_obj.Message = "Modified by Python"
    dotnet_obj.Counter = 42
    dotnet_obj.Timestamp = MockDateTime.Now()
    dotnet_obj.IsProcessed = True
    
    Console.WriteLine(f"After: {dotnet_obj.Message}")
    return dotnet_obj.Message

if __name__ == "__main__":
    # Test functions
    print("Testing greet_from_python:")
    result = greet_from_python("TestUser")
    print(f"Result: {result}\n")
    
    print("Testing process_dotnet_object:")
    obj = MockDotNetObject()
    process_dotnet_object(obj)
    print(f"Object after processing: {obj.Message}, {obj.Counter}")
```

**2. Debug this file normally:**
- Set breakpoints in VSCode
- Press F5 and select Python Debugger
- Step through, inspect variables, etc.

**3. Copy working code to [`Scripts/sample.py`](Scripts/sample.py#L1)**

**Advantages:**
- Full VSCode debugging experience
- Rapid development cycle
- Can use unit tests
- No embedding complications

### Method 4: Exception Inspection

Add try-except blocks to catch and inspect errors:

```python
import traceback
import sys

def greet_from_python(name):
    try:
        Console.WriteLine(f"Python calling .NET: Hello, {name}!")
        result = f"Greeting from Python: Welcome, {name}!"
        return result
    except Exception as e:
        error_msg = f"Error in greet_from_python: {e}\n{traceback.format_exc()}"
        print(error_msg, file=sys.stderr, flush=True)
        Console.WriteLine(error_msg)
        raise
```

### Method 5: Variable Inspection Helper

Create a debug helper function:

```python
import sys

def inspect_var(name, value):
    """Detailed variable inspection"""
    print(f"\n{'='*50}", flush=True)
    print(f"INSPECTING: {name}", flush=True)
    print(f"Type: {type(value)}", flush=True)
    print(f"Value: {value}", flush=True)
    
    if hasattr(value, '__dict__'):
        print("Attributes:", flush=True)
        for attr in dir(value):
            if not attr.startswith('_'):
                try:
                    val = getattr(value, attr)
                    print(f"  {attr} = {val}", flush=True)
                except:
                    print(f"  {attr} = <unable to access>", flush=True)
    print(f"{'='*50}\n", flush=True)

# Usage:
def process_dotnet_object(dotnet_obj):
    inspect_var("dotnet_obj (before)", dotnet_obj)
    
    dotnet_obj.Message = "Modified by Python"
    dotnet_obj.Counter = 42
    
    inspect_var("dotnet_obj (after)", dotnet_obj)
    return dotnet_obj.Message
```

### Method 6: Use Visual Studio (Full Version)

If you have Visual Studio (not VSCode), you can use mixed-mode debugging:

1. Open the solution in Visual Studio
2. Right-click the project → Properties → Debug
3. Enable "Enable native code debugging"
4. Install Python Tools for Visual Studio (PTVS)
5. Set breakpoints in both C# and Python files
6. Start debugging (F5)

**This is the only way to seamlessly step between .NET and Python code.**

## Current Setup

The project is configured to run normally without debugging overhead:

- [`Scripts/sample.py`](Scripts/sample.py#L1) - Clean implementation without debugpy
- Optional debug logging can be added as needed
- App runs instantly without debugger initialization delays

## Quick Debugging Workflow

**For quick bug fixes:**
1. Add `print()` statements with `flush=True`
2. Run: `dotnet run`
3. Review output
4. Remove or comment out debug prints when done

**For complex debugging:**
1. Create a standalone test file with mock objects
2. Debug in VSCode with full Python debugger support
3. Copy working code to [`sample.py`](Scripts/sample.py#L1)

**For production debugging:**
1. Use proper logging framework:
```python
import logging
logging.basicConfig(level=logging.DEBUG)
logger = logging.getLogger(__name__)

def greet_from_python(name):
    logger.debug(f"Called with name={name}")
    # ... code ...
    logger.info(f"Returning greeting")
    return result
```

## Troubleshooting

### Problem: Can't see print output
**Solution:** Always use `flush=True` in print statements
```python
print("Debug message", flush=True)
```

### Problem: Need to inspect .NET objects from Python
**Solution:** Use the inspect helper or dir():
```python
print(dir(dotnet_obj), flush=True)
print(dotnet_obj.Message, flush=True)
```

### Problem: Script crashes without clear error
**Solution:** Wrap in try-except:
```python
try:
    # your code
except Exception as e:
    import traceback
    print(f"ERROR: {e}\n{traceback.format_exc()}", flush=True)
    raise
```

## Why debugpy Doesn't Work

debugpy requires:
1. Control over Python interpreter lifecycle
2. Ability to modify sys.settrace and sys.monitoring
3. Unimpeded access to Python's internal debugging hooks

In embedded Python.NET scenarios:
- The .NET CLR controls the Python interpreter
- Python.NET manages the GIL and threading
- Some Python internals are restricted or behave differently
- debugpy's initialization conflicts with Python.NET's initialization

This is a known limitation, not a bug in either debugpy or Python.NET - they simply weren't designed to work together in embedded scenarios.

## References

- [Python.NET Debugging Wiki](https://github.com/pythonnet/pythonnet/wiki/Various-debugging-scenarios-of-embedded-CPython)
- [Python PDB Documentation](https://docs.python.org/3/library/pdb.html)
- [Python Logging](https://docs.python.org/3/library/logging.html)

## Quick Reference

| Method | Complexity | Effectiveness | When to Use |
|--------|-----------|---------------|-------------|
| Print/Logging | Low | High | Quick debugging, always |
| PDB | Medium | Medium | Step-by-step without VSCode |
| Separate Testing | Medium | Very High | Complex logic |
| Try-Except | Low | Medium | Error investigation |
| Visual Studio | High | Very High | If available, for mixed-mode |