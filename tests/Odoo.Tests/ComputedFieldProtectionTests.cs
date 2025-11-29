using Xunit;
using Odoo.Core;
using Odoo.Core.Modules;
using Odoo.Core.Pipeline;
using Odoo.Base.Models;

namespace Odoo.Tests;

/// <summary>
/// Tests for the computed field protection mechanism.
/// This mechanism allows compute methods to use direct property assignment
/// (e.g., partner.DisplayName = value) without triggering infinite recursion.
/// </summary>
public class ComputedFieldProtectionTests
{
    #region Helper Methods

    private static OdooEnvironment CreateTestEnvironment()
    {
        var pipelines = new PipelineRegistry();
        var cache = new ColumnarValueCache();
        // For protection mechanism tests, we don't need a full model registry
        var env = new OdooEnvironment(1, cache, null, pipelines);
        return env;
    }

    #endregion

    #region IsProtected / Protecting Tests

    [Fact]
    public void IsProtected_WhenNotProtected_ReturnsFalse()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var fieldToken = new FieldHandle(12345);
        var recordId = 1;

        // Act
        var isProtected = env.IsProtected(fieldToken, recordId);

        // Assert
        Assert.False(isProtected);
    }

    [Fact]
    public void IsProtected_WhenProtected_ReturnsTrue()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var fieldToken = new FieldHandle(12345);
        var recordId = 1;

        // Act
        using (env.Protecting(new[] { fieldToken }, new[] { recordId }))
        {
            var isProtected = env.IsProtected(fieldToken, recordId);

            // Assert
            Assert.True(isProtected);
        }
    }

    [Fact]
    public void Protecting_AfterDispose_RecordIsNoLongerProtected()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var fieldToken = new FieldHandle(12345);
        var recordId = 1;

        // Act
        using (env.Protecting(new[] { fieldToken }, new[] { recordId }))
        {
            // Record is protected inside the using block
            Assert.True(env.IsProtected(fieldToken, recordId));
        }

        // Assert - After dispose, record is no longer protected
        Assert.False(env.IsProtected(fieldToken, recordId));
    }

    [Fact]
    public void Protecting_MultipleRecords_AllAreProtected()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var fieldToken = new FieldHandle(12345);
        var recordIds = new[] { 1, 2, 3, 4, 5 };

        // Act & Assert
        using (env.Protecting(new[] { fieldToken }, recordIds))
        {
            foreach (var id in recordIds)
            {
                Assert.True(env.IsProtected(fieldToken, id), $"Record {id} should be protected");
            }
        }

        // After dispose
        foreach (var id in recordIds)
        {
            Assert.False(env.IsProtected(fieldToken, id), $"Record {id} should not be protected after dispose");
        }
    }

    [Fact]
    public void Protecting_MultipleFields_AllAreProtected()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var fields = new[] { new FieldHandle(111), new FieldHandle(222), new FieldHandle(333) };
        var recordId = 1;

        // Act & Assert
        using (env.Protecting(fields, new[] { recordId }))
        {
            foreach (var field in fields)
            {
                Assert.True(env.IsProtected(field, recordId), $"Field {field.Token} should be protected");
            }
        }

        // After dispose
        foreach (var field in fields)
        {
            Assert.False(env.IsProtected(field, recordId), $"Field {field.Token} should not be protected after dispose");
        }
    }

    [Fact]
    public void Protecting_DifferentField_NotProtected()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var protectedField = new FieldHandle(111);
        var otherField = new FieldHandle(222);
        var recordId = 1;

        // Act & Assert
        using (env.Protecting(new[] { protectedField }, new[] { recordId }))
        {
            Assert.True(env.IsProtected(protectedField, recordId));
            Assert.False(env.IsProtected(otherField, recordId));
        }
    }

    [Fact]
    public void Protecting_DifferentRecord_NotProtected()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var fieldToken = new FieldHandle(12345);
        var protectedRecordId = 1;
        var otherRecordId = 2;

        // Act & Assert
        using (env.Protecting(new[] { fieldToken }, new[] { protectedRecordId }))
        {
            Assert.True(env.IsProtected(fieldToken, protectedRecordId));
            Assert.False(env.IsProtected(fieldToken, otherRecordId));
        }
    }

    [Fact]
    public void Protecting_NestedScopes_WorkCorrectly()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var field1 = new FieldHandle(111);
        var field2 = new FieldHandle(222);
        var recordId = 1;

        // Act & Assert
        using (env.Protecting(new[] { field1 }, new[] { recordId }))
        {
            Assert.True(env.IsProtected(field1, recordId));
            Assert.False(env.IsProtected(field2, recordId));

            using (env.Protecting(new[] { field2 }, new[] { recordId }))
            {
                Assert.True(env.IsProtected(field1, recordId));
                Assert.True(env.IsProtected(field2, recordId));
            }

            // After inner dispose, field2 is no longer protected
            Assert.True(env.IsProtected(field1, recordId));
            Assert.False(env.IsProtected(field2, recordId));
        }

        // After outer dispose, neither is protected
        Assert.False(env.IsProtected(field1, recordId));
        Assert.False(env.IsProtected(field2, recordId));
    }

    #endregion

    #region SetComputedValue Integration Tests

    [Fact]
    public void SetComputedValue_WritesDirectlyToCache()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var modelToken = new ModelHandle(999);
        var fieldToken = new FieldHandle(12345);
        var recordId = 1;
        var expectedValue = "Test Value";

        // Act
        env.SetComputedValue(modelToken, recordId, fieldToken, expectedValue);

        // Assert
        var actualValue = env.Columns.GetValue<string>(modelToken, recordId, fieldToken);
        Assert.Equal(expectedValue, actualValue);
    }

    [Fact]
    public void SetComputedValue_ClearsRecomputeFlag()
    {
        // Arrange
        var env = CreateTestEnvironment();
        var modelToken = new ModelHandle(999);
        var fieldToken = new FieldHandle(12345);
        var recordId = 1;
        
        // Mark as needing recompute first
        env.ComputeTracker.MarkToRecompute(modelToken, recordId, fieldToken);
        Assert.True(env.ComputeTracker.NeedsRecompute(modelToken, recordId, fieldToken));

        // Act
        env.SetComputedValue(modelToken, recordId, fieldToken, "Test Value");

        // Assert - recompute flag should be cleared
        Assert.False(env.ComputeTracker.NeedsRecompute(modelToken, recordId, fieldToken));
    }

    #endregion
}