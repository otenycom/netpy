#!/usr/bin/env python3
"""
Standalone test file for developing and debugging Python logic.

This file can be run and debugged directly in VSCode with full debugger support.
Once the logic works here, copy it to sample.py for use with .NET.

Usage:
    python Scripts/sample_test.py

To debug in VSCode:
    1. Open this file
    2. Set breakpoints by clicking in the gutter
    3. Press F5 and select "Python Debugger"
    4. Step through code, inspect variables, etc.
"""

import sys
from datetime import datetime


# Mock .NET types for testing
class MockConsole:
    """Mock System.Console for testing"""
    @staticmethod
    def WriteLine(msg):
        print(f"[Console.WriteLine] {msg}")


class MockDateTime:
    """Mock System.DateTime for testing"""
    @staticmethod
    def Now():
        return datetime.now()


class MockDotNetObject:
    """Mock .NET object for testing process_dotnet_object"""
    def __init__(self):
        self.Message = "Original from .NET"
        self.Counter = 0
        self.Timestamp = datetime.now()
        self.IsProcessed = False
    
    def __repr__(self):
        return (f"MockDotNetObject(Message='{self.Message}', "
                f"Counter={self.Counter}, IsProcessed={self.IsProcessed})")


# Test implementation of your Python functions
def greet_from_python(name):
    """
    Test version of greet_from_python function.
    Set breakpoint here to debug step-by-step.
    """
    Console = MockConsole()  # Use mock in tests
    
    # Set breakpoint on next line to inspect 'name' parameter
    Console.WriteLine(f"Python calling .NET: Hello, {name}!")
    result = f"Greeting from Python: Welcome, {name}!"
    
    return result


def process_dotnet_object(dotnet_obj):
    """
    Test version of process_dotnet_object function.
    Set breakpoint here to inspect object state changes.
    """
    Console = MockConsole()  # Use mock in tests
    DateTime = MockDateTime()  # Use mock in tests
    
    # Set breakpoint here to inspect 'before' state
    Console.WriteLine(
        f"Before modification - Message: {dotnet_obj.Message}, "
        f"Counter: {dotnet_obj.Counter}, IsProcessed: {dotnet_obj.IsProcessed}"
    )
    
    # Modify all properties - set breakpoints to watch changes
    dotnet_obj.Message = "Modified by Python"
    dotnet_obj.Counter = 42
    dotnet_obj.Timestamp = DateTime.Now()
    dotnet_obj.IsProcessed = True
    
    # Set breakpoint here to inspect 'after' state
    Console.WriteLine(
        f"After modification - Message: {dotnet_obj.Message}, "
        f"Counter: {dotnet_obj.Counter}, IsProcessed: {dotnet_obj.IsProcessed}"
    )
    
    return dotnet_obj.Message


def run_tests():
    """Run tests to verify function behavior"""
    print("="* 60)
    print("Testing Python Functions")
    print("="* 60)
    
    # Test 1: greet_from_python
    print("\n[TEST 1] Testing greet_from_python:")
    print("-" * 40)
    result = greet_from_python("TestUser")
    print(f"Returned: {result}")
    assert "Welcome, TestUser" in result, "Failed: result doesn't contain expected text"
    print("✓ Test passed!")
    
    # Test 2: process_dotnet_object
    print("\n[TEST 2] Testing process_dotnet_object:")
    print("-" * 40)
    obj = MockDotNetObject()
    print(f"Before: {obj}")
    
    returned_message = process_dotnet_object(obj)
    
    print(f"After: {obj}")
    print(f"Returned message: {returned_message}")
    
    # Verify changes
    assert obj.Message == "Modified by Python", "Failed: Message not modified"
    assert obj.Counter == 42, "Failed: Counter not set to 42"
    assert obj.IsProcessed == True, "Failed: IsProcessed not set to True"
    assert returned_message == "Modified by Python", "Failed: returned wrong message"
    print("✓ Test passed!")
    
    # Test 3: Edge cases
    print("\n[TEST 3] Testing edge cases:")
    print("-" * 40)
    
    # Empty name
    try:
        result = greet_from_python("")
        print(f"Empty name result: {result}")
        print("✓ Handles empty name")
    except Exception as e:
        print(f"✗ Failed with empty name: {e}")
    
    # Special characters in name
    try:
        result = greet_from_python("User<>&\"'")
        print(f"Special chars result: {result}")
        print("✓ Handles special characters")
    except Exception as e:
        print(f"✗ Failed with special chars: {e}")
    
    print("\n" + "="* 60)
    print("All tests completed!")
    print("="* 60)


if __name__ == "__main__":
    print(f"Python version: {sys.version}")
    print(f"Script: {__file__}\n")
    
    # Run all tests
    run_tests()
    
    print("\n" + "="* 60)
    print("DEBUGGING TIPS:")
    print("="* 60)
    print("1. Set breakpoints by clicking in the gutter (left of line numbers)")
    print("2. Press F5 to start debugging")
    print("3. Use F10 to step over, F11 to step into functions")
    print("4. Hover over variables to see their values")
    print("5. Use the Debug Console to evaluate expressions")
    print("6. Once working, copy functions to sample.py")
    print("="* 60)