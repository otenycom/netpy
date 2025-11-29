using Odoo.Base.Models;
using Odoo.Core;
using Odoo.Core.Modules;
using Odoo.Core.Pipeline;
using Odoo.Generated.OdooTests;
using Odoo.Sale.Models;
using Xunit;
using OdooBase = Odoo.Generated.OdooBase;

namespace Odoo.Tests;

/// <summary>
/// Tests for RecordValueField&lt;T&gt; and typed record creation.
///
/// These tests verify:
/// - RecordValueField&lt;T&gt; properly tracks IsSet status
/// - Implicit conversion from T sets IsSet=true
/// - ToDictionary() only includes fields with IsSet=true
/// - Batch Create operations work correctly
/// </summary>
public class RecordValueFieldTests
{
    #region Test Setup

    private static OdooEnvironment CreateConfiguredEnvironment()
    {
        var pipelineRegistry = new PipelineRegistry();
        var registryBuilder = new RegistryBuilder();

        registryBuilder.ScanAssembly(typeof(IPartnerBase).Assembly);
        registryBuilder.ScanAssembly(typeof(IPartnerSaleExtension).Assembly);
        var modelRegistry = registryBuilder.Build();

        var baseRegistrar = new OdooBase.ModuleRegistrar();
        baseRegistrar.RegisterPipelines(pipelineRegistry);

        var testRegistrar = new ModuleRegistrar();
        testRegistrar.RegisterPipelines(pipelineRegistry);
        testRegistrar.RegisterFactories(modelRegistry);

        pipelineRegistry.CompileAll();

        return new OdooEnvironment(userId: 1, null, modelRegistry, pipelineRegistry);
    }

    #endregion

    #region RecordValueField Basic Tests

    [Fact]
    public void RecordValueField_DefaultIsNotSet()
    {
        // Arrange & Act
        var field = new RecordValueField<string>();

        // Assert
        Assert.False(field.IsSet);
        Assert.Null(field.Value);
    }

    [Fact]
    public void RecordValueField_ImplicitConversion_SetsIsSet()
    {
        // Arrange & Act
        RecordValueField<string> field = "Hello";

        // Assert
        Assert.True(field.IsSet);
        Assert.Equal("Hello", field.Value);
    }

    [Fact]
    public void RecordValueField_ImplicitConversion_WorksWithNull()
    {
        // Arrange & Act
        RecordValueField<string?> field = (string?)null;

        // Assert - null is a valid explicit value, so IsSet should be true
        Assert.True(field.IsSet);
        Assert.Null(field.Value);
    }

    [Fact]
    public void RecordValueField_ImplicitConversion_WorksWithValueTypes()
    {
        // Arrange & Act
        RecordValueField<int> intField = 42;
        RecordValueField<bool> boolField = true;
        RecordValueField<decimal> decimalField = 99.99m;

        // Assert
        Assert.True(intField.IsSet);
        Assert.Equal(42, intField.Value);

        Assert.True(boolField.IsSet);
        Assert.True(boolField.Value);

        Assert.True(decimalField.IsSet);
        Assert.Equal(99.99m, decimalField.Value);
    }

    [Fact]
    public void RecordValueField_Set_UpdatesValue()
    {
        // Arrange
        var field = new RecordValueField<string>();

        // Act
        field.Set("Test");

        // Assert
        Assert.True(field.IsSet);
        Assert.Equal("Test", field.Value);
    }

    [Fact]
    public void RecordValueField_Clear_ResetsIsSet()
    {
        // Arrange
        RecordValueField<string> field = "Hello";
        Assert.True(field.IsSet);

        // Act
        field.Clear();

        // Assert
        Assert.False(field.IsSet);
        Assert.Null(field.Value);
    }

    #endregion

    #region Values Class ToDictionary Tests

    [Fact]
    public void Values_ToDictionary_OnlyIncludesSetFields()
    {
        // Arrange - Only set Name, leave others at default
        var values = new ResPartnerValues { Name = "Alice" };

        // Act
        var dict = values.ToDictionary();

        // Assert - Only "name" should be in dictionary
        Assert.Single(dict);
        Assert.True(dict.ContainsKey("name"));
        Assert.Equal("Alice", dict["name"]);
    }

    [Fact]
    public void Values_ToDictionary_IncludesMultipleSetFields()
    {
        // Arrange
        var values = new ResPartnerValues
        {
            Name = "Test Corp",
            IsCompany = true,
            Email = "info@test.com",
        };

        // Act
        var dict = values.ToDictionary();

        // Assert - Should have 3 fields
        Assert.Equal(3, dict.Count);
        Assert.Equal("Test Corp", dict["name"]);
        Assert.Equal(true, dict["is_company"]);
        Assert.Equal("info@test.com", dict["email"]);
    }

    [Fact]
    public void Values_ToDictionary_EmptyWhenNoFieldsSet()
    {
        // Arrange
        var values = new ResPartnerValues();

        // Act
        var dict = values.ToDictionary();

        // Assert
        Assert.Empty(dict);
    }

    [Fact]
    public void Values_GetSetFields_ReturnsCorrectFieldNames()
    {
        // Arrange
        var values = new ResPartnerValues { Name = "Test", CreditLimit = 1000m };

        // Act
        var setFields = values.GetSetFields().ToList();

        // Assert
        Assert.Equal(2, setFields.Count);
        Assert.Contains("name", setFields);
        Assert.Contains("credit_limit", setFields);
    }

    #endregion

    #region Create with Typed Values Tests

    [Fact]
    public void Create_WithTypedValues_CreatesRecord()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act
        var partner = env.Create(new ResPartnerValues { Name = "TypedCreate Test" });

        // Assert
        Assert.NotNull(partner);
        Assert.Equal("TypedCreate Test", partner.Name);
    }

    [Fact]
    public void Create_WithTypedValues_SetsMultipleFields()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act
        var partner = env.Create(
            new ResPartnerValues
            {
                Name = "Multi Field Test",
                IsCompany = true,
                Email = "multi@test.com",
                IsCustomer = true,
                CreditLimit = 2500.00m,
            }
        );

        // Assert
        Assert.Equal("Multi Field Test", partner.Name);
        Assert.True(partner.IsCompany);
        Assert.Equal("multi@test.com", partner.Email);
        // Cast to IPartnerSaleExtension to access sale module fields
        var partnerSale = (IPartnerSaleExtension)partner;
        Assert.True(partnerSale.IsCustomer);
        Assert.Equal(2500.00m, partnerSale.CreditLimit);
    }

    [Fact]
    public void Create_WithTypedValues_OnlySetsProvidedFields()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Only set Name
        var partner = env.Create(new ResPartnerValues { Name = "Only Name" });

        // Assert - Other fields should have default values
        Assert.Equal("Only Name", partner.Name);
        Assert.False(partner.IsCompany); // Default is false
    }

    #endregion

    #region Batch Create Tests

    [Fact]
    public void BatchCreate_MultipleRecords_ReturnsRecordSet()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var valuesList = new[]
        {
            new ResPartnerValues { Name = "Batch 1" },
            new ResPartnerValues { Name = "Batch 2" },
            new ResPartnerValues { Name = "Batch 3" },
        };

        // Act
        var recordSet = env.CreateBatch(valuesList);

        // Assert
        Assert.Equal(3, recordSet.Count);
        Assert.Equal("Batch 1", recordSet[0].Name);
        Assert.Equal("Batch 2", recordSet[1].Name);
        Assert.Equal("Batch 3", recordSet[2].Name);
    }

    [Fact]
    public void BatchCreate_WithDifferentFields_EachRecordHasOwnValues()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var valuesList = new[]
        {
            new ResPartnerValues { Name = "Individual 1", IsCompany = false },
            new ResPartnerValues
            {
                Name = "Company 1",
                IsCompany = true,
                CreditLimit = 5000m,
            },
            new ResPartnerValues
            {
                Name = "Customer 1",
                IsCustomer = true,
                Email = "customer@example.com",
            },
        };

        // Act
        var recordSet = env.CreateBatch(valuesList);

        // Assert - Each record should have its own specific values
        Assert.Equal(3, recordSet.Count);

        var individual = recordSet[0];
        Assert.Equal("Individual 1", individual.Name);
        Assert.False(individual.IsCompany);

        // Cast to IPartnerSaleExtension to access sale module fields
        var company = (IPartnerSaleExtension)recordSet[1];
        Assert.Equal("Company 1", company.Name);
        Assert.True(company.IsCompany);
        Assert.Equal(5000m, company.CreditLimit);

        var customer = (IPartnerSaleExtension)recordSet[2];
        Assert.Equal("Customer 1", customer.Name);
        Assert.True(customer.IsCustomer);
        Assert.Equal("customer@example.com", customer.Email);
    }

    [Fact]
    public void BatchCreate_EmptyCollection_ReturnsEmptyRecordSet()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var valuesList = Array.Empty<ResPartnerValues>();

        // Act
        var recordSet = env.CreateBatch(valuesList);

        // Assert
        Assert.True(recordSet.IsEmpty);
        Assert.Equal(0, recordSet.Count);
    }

    [Fact]
    public void BatchCreate_ComputedFieldsWorkCorrectly()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var valuesList = new[]
        {
            new ResPartnerValues { Name = "Alice Individual" },
            new ResPartnerValues { Name = "Bob Company", IsCompany = true },
        };

        // Act
        var recordSet = env.CreateBatch(valuesList);

        // Assert - DisplayName should be computed correctly for each
        Assert.Equal("Alice Individual", recordSet[0].DisplayName);
        Assert.Equal("Bob Company | Company", recordSet[1].DisplayName);
    }

    #endregion

    #region IRecordValues Interface Tests

    [Fact]
    public void Values_ImplementsIRecordValues()
    {
        // Arrange
        var values = new ResPartnerValues { Name = "Interface Test" };

        // Act & Assert - Should be assignable to IRecordValues
        IRecordValues recordValues = values;
        Assert.Equal("res.partner", recordValues.ModelName);
        Assert.Single(recordValues.ToDictionary());
    }

    [Fact]
    public void Values_ImplementsIRecordValuesGeneric()
    {
        // Arrange
        var values = new ResPartnerValues { Name = "Generic Test" };

        // Act & Assert - Should implement IRecordValues<T> for one of the model interfaces
        // The unified wrapper can have multiple interfaces, so check the type implements a generic version
        var valuesType = values.GetType();
        var genericInterface = valuesType
            .GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRecordValues<>)
            );

        Assert.NotNull(genericInterface);
        Assert.Equal("res.partner", values.ModelName);
    }

    #endregion
}
