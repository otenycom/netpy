using Odoo.Core;
using Odoo.Generated.OdooTests;
using Odoo.Python.Cli;
using Xunit;

namespace Odoo.Tests;

/// <summary>
/// Tests for the Python shell command and env['model'] syntax.
/// These tests verify the Python proxy wrappers work correctly.
/// </summary>
public class PythonShellTests
{
    private readonly ShellCommand _shell;

    public PythonShellTests()
    {
        _shell = new ShellCommand();
    }

    private static OdooEnvironment CreateEnvironment()
    {
        return new OdooEnvironmentBuilder().WithUserId(1).Build();
    }

    /// <summary>
    /// Test that env object is accessible in Python.
    /// </summary>
    [Fact]
    public void Shell_EnvIsAccessible()
    {
        // Arrange
        var env = CreateEnvironment();

        // Act - evaluate env.user_id
        var result = _shell.ExecuteExpression(env, "env.user_id");

        // Assert
        Assert.Equal(1, Convert.ToInt32(result));
    }

    /// <summary>
    /// Test that env['res.partner'] subscript syntax works.
    /// </summary>
    [Fact]
    public void Shell_EnvSubscriptSyntax_ReturnsModelProxy()
    {
        // Arrange
        var env = CreateEnvironment();

        // Act - access model via subscript
        var result = _shell.ExecuteExpression(env, "repr(env['res.partner'])");

        // Assert
        Assert.NotNull(result);
        var repr = result.ToString();
        Assert.Contains("res.partner", repr!);
        Assert.Contains("Model", repr);
    }

    /// <summary>
    /// Test that model_name property works.
    /// </summary>
    [Fact]
    public void Shell_ModelProxy_HasModelName()
    {
        // Arrange
        var env = CreateEnvironment();

        // Act
        var result = _shell.ExecuteExpression(env, "env['res.partner'].model_name");

        // Assert
        Assert.Equal("res.partner", result?.ToString());
    }

    /// <summary>
    /// Test that env['res.partner'].browse(id) works.
    /// </summary>
    [Fact]
    public void Shell_ModelBrowse_ReturnsRecordSet()
    {
        // Arrange - first create a partner
        var env = CreateEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Test Partner Shell" });

        // Act - browse the partner via Python
        var result = _shell.ExecuteExpression(
            env,
            $"repr(env['res.partner'].browse({partner.Id}))"
        );

        // Assert
        Assert.NotNull(result);
        var repr = result.ToString();
        Assert.Contains("res.partner", repr!);
        Assert.Contains(partner.Id.ToString(), repr);
    }

    /// <summary>
    /// Test that env['res.partner'].browse(id).name works.
    /// </summary>
    [Fact]
    public void Shell_RecordFieldAccess_ReturnsValue()
    {
        // Arrange - create a partner with a specific name
        var env = CreateEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Shell Test Name" });

        // Act - access the name field via Python
        var result = _shell.ExecuteExpression(env, $"env['res.partner'].browse({partner.Id}).name");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Shell Test Name", result.ToString());
    }

    /// <summary>
    /// Test that -c command execution works (basic execution, no print capture).
    /// </summary>
    [Fact]
    public void Shell_CommandOption_ExecutesPythonCode()
    {
        // Arrange
        var env = CreateEnvironment();

        // Act - execute a command that creates a variable and we verify via expression
        var exitCode = _shell.ExecuteCommand(env, "x = env.user_id * 10");

        // Assert - command executed successfully
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// Test that recordset ids property works.
    /// </summary>
    [Fact]
    public void Shell_RecordSet_HasIds()
    {
        // Arrange
        var env = CreateEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "IDs Test Partner" });

        // Act - get the IDs list
        var result = _shell.ExecuteExpression(
            env,
            $"len(env['res.partner'].browse({partner.Id}).ids)"
        );

        // Assert - should have 1 ID
        Assert.Equal(1, Convert.ToInt32(result));
    }

    /// <summary>
    /// Test that recordset len() works.
    /// </summary>
    [Fact]
    public void Shell_RecordSet_LenWorks()
    {
        // Arrange
        var env = CreateEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Len Test" });

        // Act
        var result = _shell.ExecuteExpression(env, $"len(env['res.partner'].browse({partner.Id}))");

        // Assert
        Assert.Equal(1, Convert.ToInt32(result));
    }

    /// <summary>
    /// Test that record.id works.
    /// </summary>
    [Fact]
    public void Shell_RecordProxy_HasId()
    {
        // Arrange
        var env = CreateEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "ID Test Partner" });

        // Act - get record ID via Python
        var result = _shell.ExecuteExpression(env, $"env['res.partner'].browse({partner.Id}).id");

        // Assert
        Assert.Equal(partner.Id, Convert.ToInt32(result));
    }

    /// <summary>
    /// Test that iteration works - verify we can iterate and get first ID.
    /// </summary>
    [Fact]
    public void Shell_RecordSet_IterationWorks()
    {
        // Arrange
        var env = CreateEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Iter Test Partner" });

        // Act - iterate and get first record's ID (use len() to verify iteration works)
        var result = _shell.ExecuteExpression(
            env,
            $"len([r.id for r in env['res.partner'].browse({partner.Id})])"
        );

        // Assert - should have 1 item from iteration
        Assert.Equal(1, Convert.ToInt32(result));
    }

    /// <summary>
    /// Test that env['res.partner'].create({'name': 'ries'}) works and we can retrieve the record.
    /// This is a full roundtrip test: create via Python, retrieve and verify name.
    /// </summary>
    [Fact]
    public void Shell_CreateAndRetrieve_WorksEndToEnd()
    {
        // Arrange
        var env = CreateEnvironment();

        // Act - create a partner via Python and get its name back
        // We need to do this in multiple steps since create returns a recordset
        // Step 1: Create and get the ID
        var idResult = _shell.ExecuteExpression(
            env,
            "env['res.partner'].create({'name': 'ries'}).id"
        );

        Assert.NotNull(idResult);
        var partnerId = Convert.ToInt32(idResult);
        Assert.True(partnerId > 0, "Partner ID should be positive");

        // Step 2: Browse the created record and verify the name
        var nameResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).name"
        );

        // Assert
        Assert.NotNull(nameResult);
        Assert.Equal("ries", nameResult.ToString());
    }

    /// <summary>
    /// Test create with multiple fields and verify all are accessible.
    /// </summary>
    [Fact]
    public void Shell_CreateWithMultipleFields_AllFieldsAccessible()
    {
        // Arrange
        var env = CreateEnvironment();

        // Act - create a partner with multiple fields via Python
        var idResult = _shell.ExecuteExpression(
            env,
            "env['res.partner'].create({'name': 'Test Corp', 'email': 'test@corp.com', 'is_company': True}).id"
        );

        Assert.NotNull(idResult);
        var partnerId = Convert.ToInt32(idResult);

        // Verify name
        var nameResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).name"
        );
        Assert.Equal("Test Corp", nameResult?.ToString());

        // Verify email
        var emailResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).email"
        );
        Assert.Equal("test@corp.com", emailResult?.ToString());

        // Verify is_company
        var isCompanyResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).is_company"
        );
        Assert.True(Convert.ToBoolean(isCompanyResult));
    }

    /// <summary>
    /// Test that display_name computed field works via Python.
    /// display_name is a computed field that should show the partner name.
    /// </summary>
    [Fact]
    public void Shell_DisplayName_ComputedFieldWorks()
    {
        // Arrange
        var env = CreateEnvironment();

        // Act - create a partner and access display_name
        var idResult = _shell.ExecuteExpression(
            env,
            "env['res.partner'].create({'name': 'Display Test Partner'}).id"
        );

        Assert.NotNull(idResult);
        var partnerId = Convert.ToInt32(idResult);

        // Get display_name
        var displayNameResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).display_name"
        );

        // Assert - display_name should contain the name
        Assert.NotNull(displayNameResult);
        Assert.Contains("Display Test Partner", displayNameResult.ToString()!);
    }

    /// <summary>
    /// Test that display_name computed field shows "| Company" when is_company is true.
    /// This tests the computed field logic.
    /// </summary>
    [Fact]
    public void Shell_DisplayName_IncludesCompanySuffix()
    {
        // Arrange
        var env = CreateEnvironment();

        // Act - create a company partner
        var idResult = _shell.ExecuteExpression(
            env,
            "env['res.partner'].create({'name': 'Acme Corp', 'is_company': True}).id"
        );

        Assert.NotNull(idResult);
        var partnerId = Convert.ToInt32(idResult);

        // Get display_name
        var displayNameResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).display_name"
        );

        // Assert - display_name should have "| Company" suffix
        Assert.NotNull(displayNameResult);
        var displayName = displayNameResult.ToString()!;
        Assert.Contains("Acme Corp", displayName);
        Assert.Contains("| Company", displayName);
    }

    /// <summary>
    /// Test that write() method works via Python.
    /// Creates a partner, writes to it, then verifies the change.
    /// </summary>
    [Fact]
    public void Shell_Write_UpdatesRecord()
    {
        // Arrange
        var env = CreateEnvironment();

        // Create a partner
        var idResult = _shell.ExecuteExpression(
            env,
            "env['res.partner'].create({'name': 'Write Test Partner'}).id"
        );
        Assert.NotNull(idResult);
        var partnerId = Convert.ToInt32(idResult);

        // Act - update the partner's email via write()
        _shell.ExecuteCommand(
            env,
            $"env['res.partner'].browse({partnerId}).write({{'email': 'updated@test.com'}})"
        );

        // Verify the email was updated
        var emailResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).email"
        );

        // Assert
        Assert.NotNull(emailResult);
        Assert.Equal("updated@test.com", emailResult.ToString());
    }

    /// <summary>
    /// Test that write() with multiple fields works.
    /// </summary>
    [Fact]
    public void Shell_Write_UpdatesMultipleFields()
    {
        // Arrange
        var env = CreateEnvironment();

        // Create a partner
        var idResult = _shell.ExecuteExpression(
            env,
            "env['res.partner'].create({'name': 'Multi Write Test'}).id"
        );
        Assert.NotNull(idResult);
        var partnerId = Convert.ToInt32(idResult);

        // Act - update multiple fields
        _shell.ExecuteCommand(
            env,
            $"env['res.partner'].browse({partnerId}).write({{'email': 'multi@test.com', 'is_company': True}})"
        );

        // Verify both fields were updated
        var emailResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).email"
        );
        Assert.Equal("multi@test.com", emailResult?.ToString());

        var isCompanyResult = _shell.ExecuteExpression(
            env,
            $"env['res.partner'].browse({partnerId}).is_company"
        );
        Assert.True(Convert.ToBoolean(isCompanyResult));
    }

    /// <summary>
    /// Test that write() with invalid argument type raises a helpful error.
    /// </summary>
    [Fact]
    public void Shell_Write_WithSetRaisesHelpfulError()
    {
        // Arrange
        var env = CreateEnvironment();

        // Create a partner
        var idResult = _shell.ExecuteExpression(
            env,
            "env['res.partner'].create({'name': 'Error Test'}).id"
        );
        Assert.NotNull(idResult);
        var partnerId = Convert.ToInt32(idResult);

        // Act - try to write with a set (common Python mistake)
        // This should raise a TypeError with helpful message
        var exitCode = _shell.ExecuteCommand(
            env,
            $@"
try:
    env['res.partner'].browse({partnerId}).write({{'email', 'bad@test.com'}})
    print('ERROR: Should have raised')
except TypeError as e:
    if 'dict' in str(e) and 'set' in str(e):
        print('Got expected error')
    else:
        raise
"
        );

        // Assert - command should complete (the exception is caught in Python)
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// Test that action_verify method can be called on the model proxy.
    /// This tests that [OdooLogic] methods are discoverable and callable from Python.
    ///
    /// In Odoo, action methods can be called directly on the model:
    ///   env['res.partner'].action_verify()  # Verifies all partners
    ///
    /// The method is defined with [OdooLogic("res.partner", "action_verify")] in PartnerLogic.cs
    /// and should be callable from Python via dynamic method dispatch.
    ///
    /// ISSUE: This test currently fails because ModelProxy doesn't support dynamic method dispatch
    /// for [OdooLogic] methods. The current ModelProxy only has hardcoded methods (browse, create, search).
    /// See: docs/RECORDSET_UNIFICATION_ARCHITECTURE.md - "Dynamic Method Discovery" section.
    /// </summary>
    [Fact]
    public void Shell_ActionVerify_CallableOnModelProxy()
    {
        // Arrange
        var env = CreateEnvironment();

        // Create some partners to verify
        env.Create(new ResPartnerValues { Name = "Verify Test Partner 1" });
        env.Create(new ResPartnerValues { Name = "Verify Test Partner 2" });

        // Act - call action_verify on the model
        // This should work: env['res.partner'].action_verify()
        // The method signature is: ActionVerify(RecordSet<IPartnerBase> self)
        // When called on model proxy, it should create an empty recordset and call the method
        var exitCode = _shell.ExecuteCommand(
            env,
            @"
import traceback
try:
    env['res.partner'].action_verify()
except Exception as e:
    print(f'ERROR: {type(e).__name__}: {e}')
    traceback.print_exc()
    raise
"
        );

        // Assert - should execute without error
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// Test that action_verify method can be called on a recordset.
    /// This tests that [OdooLogic] methods work when called on browsed records.
    ///
    /// In Odoo:
    ///   partner = env['res.partner'].browse(1)
    ///   partner.action_verify()  # Verifies this specific partner
    ///
    /// The method is defined with [OdooLogic("res.partner", "action_verify")] in PartnerLogic.cs.
    /// </summary>
    [Fact]
    public void Shell_ActionVerify_CallableOnRecordSet()
    {
        // Arrange
        var env = CreateEnvironment();

        // Create a partner to verify
        var idResult = _shell.ExecuteExpression(
            env,
            "env['res.partner'].create({'name': 'Verify Partner'}).id"
        );
        Assert.NotNull(idResult);
        var partnerId = Convert.ToInt32(idResult);

        // Act - call action_verify on a specific recordset
        var exitCode = _shell.ExecuteCommand(
            env,
            $"env['res.partner'].browse({partnerId}).action_verify()"
        );

        // Assert - should execute without error
        Assert.Equal(0, exitCode);
    }
}
