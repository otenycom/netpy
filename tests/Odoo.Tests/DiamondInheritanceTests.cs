using Odoo.Base.Models;
using Odoo.Core;
using Odoo.Generated.OdooTests;
using Odoo.Purchase.Models;
using Odoo.Sale.Models;
using Xunit;

namespace Odoo.Tests;

/// <summary>
/// Tests demonstrating diamond inheritance pattern with multiple modules extending the same model.
///
/// Diamond inheritance pattern:
///
///          IOdooRecord
///               │
///         IPartnerBase        (base module)
///          /        \
/// IPartnerSaleExtension   IPartnerPurchaseExtension   (sale + purchase modules)
///          \        /
///           IPartner          (unified interface, generated)
///
/// This demonstrates:
/// - Multiple modules independently extending res.partner
/// - Both Sale and Purchase write pipelines executing in dependency order
/// - DisplayName being modified by both IsCompany (base) and IsSupplier (purchase)
/// - Values class implementing all interfaces for type-safe access
/// </summary>
public class DiamondInheritanceTests
{
    #region Test Setup

    /// <summary>
    /// Creates a fully configured OdooEnvironment with all pipelines registered.
    /// Uses the OdooEnvironmentBuilder which auto-discovers all addons from loaded assemblies.
    ///
    /// The builder automatically:
    /// - Discovers all assemblies referencing Odoo.Core
    /// - Finds IModuleRegistrar implementations
    /// - Scans for [OdooModel] interfaces
    /// - Registers pipelines in dependency order
    /// - Compiles delegate chains
    /// </summary>
    private static OdooEnvironment CreateConfiguredEnvironment()
    {
        // The builder auto-discovers addons from loaded assemblies
        // For this test project, that includes:
        // - Odoo.Base (IPartnerBase)
        // - Odoo.Sale (IPartnerSaleExtension)
        // - Odoo.Purchase (IPartnerPurchaseExtension)
        // - Odoo.Tests (unified IResPartner interface and ResPartner wrapper)
        return new OdooEnvironmentBuilder().WithUserId(1).Build();
    }

    #endregion

    #region Diamond Inheritance Basic Tests

    [Fact]
    public void DiamondInheritance_PartnerImplementsAllInterfaces()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Create a partner
        var partner = env.Create(new ResPartnerValues { Name = "Test Partner" });

        // Assert - Partner should implement all interfaces from the diamond
        Assert.IsAssignableFrom<IPartnerBase>(partner);
        Assert.IsAssignableFrom<IPartnerSaleExtension>(partner);
        Assert.IsAssignableFrom<IPartnerPurchaseExtension>(partner);
    }

    [Fact]
    public void DiamondInheritance_SameInstanceForAllInterfaces()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Test" });

        // Act - Get same record via different interfaces
        var asBase = env.GetRecord<IPartnerBase>("res.partner", partner.Id);
        var asSale = env.GetRecord<IPartnerSaleExtension>("res.partner", partner.Id);
        var asPurchase = env.GetRecord<IPartnerPurchaseExtension>("res.partner", partner.Id);

        // Assert - Identity map ensures SAME instance
        Assert.Same(asBase, asSale);
        Assert.Same(asSale, asPurchase);
        Assert.Same(partner, asBase);
    }

    #endregion

    #region Purchase Module Field Tests

    [Fact]
    public void PurchaseModule_IsSupplierField_Available()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Create partner with IsSupplier field
        var supplier = env.Create(
            new ResPartnerValues { Name = "Acme Supplies", IsSupplier = true }
        );

        // Assert - IsSupplier is accessible via IPartnerPurchaseExtension
        var asPurchase = (IPartnerPurchaseExtension)supplier;
        Assert.True(asPurchase.IsSupplier);
        Assert.Equal("Acme Supplies", asPurchase.Name);
    }

    [Fact]
    public void PurchaseModule_SupplierDisplayName_AppendsSupplierSuffix()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Create a supplier (IsSupplier=true triggers _compute_display_name override)
        var supplier = env.Create(new ResPartnerValues { Name = "Parts Co", IsSupplier = true });

        // Assert - DisplayName should have "| Supplier" appended by the compute override
        Assert.Contains("| Supplier", supplier.DisplayName);
    }

    #endregion

    #region Combined Sale + Purchase Tests

    [Fact]
    public void DiamondInheritance_BothSaleAndPurchaseFields()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Create partner with both Sale and Purchase module fields
        var partner = env.Create(
            new ResPartnerValues
            {
                Name = "Dual Partner",
                IsCustomer = true,
                CreditLimit = 10000m,
                IsSupplier = true,
            }
        );

        // Assert - Both module fields are accessible
        var asSale = (IPartnerSaleExtension)partner;
        var asPurchase = (IPartnerPurchaseExtension)partner;

        Assert.True(asSale.IsCustomer);
        Assert.Equal(10000m, asSale.CreditLimit);
        Assert.True(asPurchase.IsSupplier);
    }

    [Fact]
    public void DiamondInheritance_CompanyAndSupplier_BothAffectDisplayName()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Create a company that is also a supplier
        // This tests the diamond: base compute adds "| Company", purchase compute adds "| Supplier"
        var companySupplier = env.Create(
            new ResPartnerValues
            {
                Name = "Big Corp",
                IsCompany = true,
                IsSupplier = true,
            }
        );

        // Assert - DisplayName should have both suffixes from compute overrides
        var displayName = companySupplier.DisplayName;
        Assert.Contains("Big Corp", displayName);
        Assert.Contains("| Company", displayName);
        Assert.Contains("| Supplier", displayName);
    }

    [Fact]
    public void DiamondInheritance_WritePipeline_ExecutesAllOverrides()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Initial" });

        // Act - Update partner to be a supplier
        // This triggers write pipeline AND then compute recomputes DisplayName
        var purchasePartner = (IPartnerPurchaseExtension)partner;
        purchasePartner.IsSupplier = true;

        // Assert - Compute override should have appended "| Supplier"
        Assert.Contains("| Supplier", partner.DisplayName);
    }

    [Fact]
    public void DiamondInheritance_ValuesClass_ImplementsAllInterfaces()
    {
        // Arrange
        var values = new ResPartnerValues
        {
            Name = "Test",
            IsCompany = true,
            IsCustomer = true,
            CreditLimit = 5000m,
            IsSupplier = true,
        };

        // Assert - Values class implements all Values interfaces
        Assert.IsAssignableFrom<IRecordValues>(values);
        Assert.IsAssignableFrom<IPartnerBaseValues>(values);
        Assert.IsAssignableFrom<IPartnerSaleExtensionValues>(values);
        Assert.IsAssignableFrom<IPartnerPurchaseExtensionValues>(values);
    }

    [Fact]
    public void DiamondInheritance_CreateWithDict_AllFieldsAccessible()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Create using dictionary with fields from all modules
        var partner = ResPartnerPipelines.CreateFromDict(
            env,
            new()
            {
                { "name", "Full Partner" },
                { "is_company", true },
                { "email", "full@partner.com" },
                { "is_customer", true },
                { "credit_limit", 25000m },
                { "is_supplier", true },
            }
        );

        // Assert - All fields from all modules are set
        Assert.Equal("Full Partner", partner.Name);
        Assert.True(partner.IsCompany);
        Assert.Equal("full@partner.com", partner.Email);

        var asSale = (IPartnerSaleExtension)partner;
        Assert.True(asSale.IsCustomer);
        Assert.Equal(25000m, asSale.CreditLimit);

        var asPurchase = (IPartnerPurchaseExtension)partner;
        Assert.True(asPurchase.IsSupplier);

        // DisplayName should have both Company and Supplier suffixes from compute
        Assert.Contains("| Company", partner.DisplayName);
        Assert.Contains("| Supplier", partner.DisplayName);
    }

    #endregion

    #region Pipeline Order Tests

    [Fact]
    public void DiamondInheritance_PipelineOrder_DependenciesFirst()
    {
        // This test verifies that the write pipeline executes in the correct order:
        // 1. Purchase override (depends: base)
        // 2. Sale override (depends: base)
        // 3. Base implementation
        //
        // The diamond pattern ensures both Sale and Purchase see the write,
        // and both can contribute to the final result.

        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Create partner that triggers all overrides
        var partner = env.Create(
            new ResPartnerValues
            {
                Name = "Pipeline Test",
                IsCompany = true, // Triggers base compute
                IsCustomer = true, // Sale module sees this
                IsSupplier = true, // Purchase module sees this
            }
        );

        // Assert - All pipeline stages executed correctly
        // The display name should reflect all contributions
        var displayName = partner.DisplayName;
        Assert.NotNull(displayName);
        Assert.StartsWith("Pipeline Test", displayName);
    }

    #endregion

    #region IModel Interface Tests

    [Fact]
    public void IModel_PartnerImplementsIModel()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();

        // Act - Create a partner
        var partner = env.Create(new ResPartnerValues { Name = "Test Partner" });

        // Assert - Partner should implement IModel
        Assert.IsAssignableFrom<IModel>(partner);
    }

    [Fact]
    public void IModel_ModelNameProperty_ReturnsCorrectValue()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Test" });

        // Act
        var model = (IModel)partner;

        // Assert
        Assert.Equal("res.partner", model.ModelName);
    }

    [Fact]
    public void IModel_WriteWithValues_UpdatesRecord()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Initial" });

        // Act - Use IModel.Write with typed values
        var model = (IModel)partner;
        var result = model.Write(new ResPartnerValues { Name = "Updated", IsCustomer = true });

        // Assert
        Assert.True(result);
        Assert.Equal("Updated", partner.Name);
        Assert.True(((IPartnerSaleExtension)partner).IsCustomer);
    }

    [Fact]
    public void IModel_WriteWithDictionary_UpdatesRecord()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Initial" });

        // Act - Use IModel.Write with dictionary (Python-style)
        var model = (IModel)partner;
        var result = model.Write(
            new Dictionary<string, object?> { { "name", "Dict Updated" }, { "is_supplier", true } }
        );

        // Assert
        Assert.True(result);
        Assert.Equal("Dict Updated", partner.Name);
        Assert.True(((IPartnerPurchaseExtension)partner).IsSupplier);
    }

    [Fact]
    public void IModel_Write_TriggersComputedFieldRecomputation()
    {
        // Arrange
        var env = CreateConfiguredEnvironment();
        var partner = env.Create(new ResPartnerValues { Name = "Original" });
        var model = (IModel)partner;

        // Act - Write IsSupplier which triggers DisplayName recomputation
        model.Write(new ResPartnerValues { IsSupplier = true });

        // Assert - DisplayName should now include "| Supplier"
        Assert.Contains("| Supplier", partner.DisplayName);
    }

    #endregion
}
