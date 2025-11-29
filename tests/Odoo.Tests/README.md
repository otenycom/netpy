# Odoo.Tests

Unit tests for the Odoo C# framework, using xUnit.

## Running Tests

### Command Line

```bash
# Run all tests
dotnet test tests/Odoo.Tests/Odoo.Tests.csproj

# Run with verbose output
dotnet test tests/Odoo.Tests/Odoo.Tests.csproj --verbosity normal

# Run specific test by name
dotnet test tests/Odoo.Tests/Odoo.Tests.csproj --filter "FullyQualifiedName~IsProtected_WhenProtected"

# Run specific test class
dotnet test tests/Odoo.Tests/Odoo.Tests.csproj --filter "FullyQualifiedName~ComputedFieldProtectionTests"
```

### VS Code Launch Configurations

From the Run and Debug panel (Ctrl+Shift+D):

- **Run Selected Test** - Select a test method name in the editor, then run this configuration
- **Run All Tests** - Runs all tests in the project

## Debugging Tests (with breakpoints)

### Method 1: Test Explorer (Recommended)

1. Open the Test Explorer sidebar (beaker icon)
2. Wait for tests to be discovered
3. Right-click on a test → **Debug Test**
4. Breakpoints will work automatically

### Method 2: Manual Attach (for complex scenarios)

This method gives you more control and works in all cases:

1. **Select test name** in the editor (highlight the test method name)

2. **Run the debug task**:
   - Press `Ctrl+Shift+P` → "Tasks: Run Task"
   - Select `debug-test-selected`
   - Or for all tests: `debug-test-all`

3. **Wait for the message**:
   ```
   Host debugging is enabled. Please attach debugger to testhost process to continue.
   Process Id: XXXXX, Name: testhost
   ```

4. **Attach the debugger**:
   - Press `Ctrl+Shift+D` (Debug panel)
   - Select `.NET Core Attach` from the dropdown
   - Click the green play button
   - In the process picker, select `testhost`

5. **Press F5 to continue** - The debugger starts paused; press F5 to resume

6. Your breakpoints will now trigger!

### Method 3: Terminal with Process ID

```bash
# Terminal 1 - Start tests waiting for debugger
VSTEST_HOST_DEBUG=1 dotnet test --filter "TestName" --no-build

# Wait for it to show the Process ID, then in VS Code:
# 1. Run ".NET Core Attach"
# 2. Enter the Process ID shown
# 3. Press F5 to continue
```

## Test Structure

```
tests/Odoo.Tests/
├── README.md                          # This file
├── Odoo.Tests.csproj                  # Project file
├── ComputedFieldProtectionTests.cs    # Protection mechanism tests
└── obj/GeneratedFiles/                # Source generator output
```

## Writing New Tests

```csharp
using Xunit;
using Odoo.Core;

namespace Odoo.Tests;

public class MyTests
{
    [Fact]
    public void MyTest_WhenCondition_ExpectedResult()
    {
        // Arrange
        var env = new OdooEnvironment(userId: 1);
        
        // Act
        var result = ...;
        
        // Assert
        Assert.Equal(expected, result);
    }
}
```

## Code Coverage

```bash
# Generate coverage report
dotnet test --collect:"XPlat Code Coverage"

# Results will be in TestResults/*/coverage.cobertura.xml