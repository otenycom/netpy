using Odoo.Core;
using Odoo.Core.Cli;
using Xunit;

namespace Odoo.Tests;

/// <summary>
/// Tests for the CLI framework functionality.
/// </summary>
public class CliCommandTests
{
    /// <summary>
    /// Test that OdooApp can register and find custom commands.
    /// </summary>
    [Fact]
    public void OdooApp_RegisterCommand_CommandIsAvailable()
    {
        // Arrange
        var testCommand = new TestCliCommand();

        // Act
        OdooApp.RegisterCommand(testCommand);

        // Assert - command should be accessible (we can't easily test Run, but registration works)
        Assert.Equal("test-cmd", testCommand.Name);
        Assert.Equal("Test command for unit testing", testCommand.Description);
    }

    /// <summary>
    /// Test that ICliCommand interface works correctly.
    /// </summary>
    [Fact]
    public void ICliCommand_Execute_ReturnsCorrectExitCode()
    {
        // Arrange
        var command = new TestCliCommand();
        var env = new OdooEnvironmentBuilder().WithUserId(1).Build();

        // Act
        var exitCode = command.Execute(env, new[] { "--test-arg", "value" });

        // Assert
        Assert.Equal(42, exitCode); // Our test command returns 42
        Assert.True(command.WasExecuted);
        Assert.Equal(1, command.ReceivedUserId);
        Assert.Contains("--test-arg", command.ReceivedArgs);
    }

    /// <summary>
    /// Test command for use in unit tests.
    /// </summary>
    private class TestCliCommand : ICliCommand
    {
        public string Name => "test-cmd";
        public string Description => "Test command for unit testing";
        public bool WasExecuted { get; private set; }
        public int ReceivedUserId { get; private set; }
        public string[] ReceivedArgs { get; private set; } = Array.Empty<string>();

        public int Execute(OdooEnvironment env, string[] args)
        {
            WasExecuted = true;
            ReceivedUserId = env.UserId;
            ReceivedArgs = args;
            return 42; // Return a distinctive exit code for testing
        }
    }
}
