using ERPApiHub.Application.Ingestion;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class AllowedDoctypeValidatorTests
{
    [Fact]
    public void IsAllowed_WhenDoctypeInList_ReturnsTrue()
    {
        var validator = new AllowedDoctypeValidator(new[] { "Customer", "Sales Order", "Item" });
        Assert.True(validator.IsAllowed("Customer"));
    }

    [Fact]
    public void IsAllowed_WhenDoctypeNotInList_ReturnsFalse()
    {
        var validator = new AllowedDoctypeValidator(new[] { "Customer", "Sales Order" });
        Assert.False(validator.IsAllowed("Purchase Order"));
    }

    [Fact]
    public void IsAllowed_IsCaseInsensitive()
    {
        var validator = new AllowedDoctypeValidator(new[] { "Customer" });
        Assert.True(validator.IsAllowed("customer"));
        Assert.True(validator.IsAllowed("CUSTOMER"));
    }

    [Fact]
    public void IsAllowed_WhenEmptyList_ReturnsFalse()
    {
        var validator = new AllowedDoctypeValidator(Array.Empty<string>());
        Assert.False(validator.IsAllowed("Customer"));
    }
}