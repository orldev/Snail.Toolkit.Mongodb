namespace Snail.Toolkit.Mongodb.Tests.Unit;

public class MongoNamingTests
{
    private sealed record Order;

    private sealed record Page<T>;

    private sealed record Pair<TFirst, TSecond>;

    [Fact]
    public void ForType_UsesTheTypeName()
    {
        Assert.Equal("Order", MongoNaming.ForType(typeof(Order)));
    }

    /// <summary>
    /// The CLR name of a generic type is <c>Page`1</c>, which is a poor collection name.
    /// </summary>
    [Fact]
    public void ForType_UnwrapsGenerics()
    {
        Assert.Equal("PageOfOrder", MongoNaming.ForType(typeof(Page<Order>)));
    }

    [Fact]
    public void ForType_UnwrapsNestedGenerics()
    {
        Assert.Equal("PageOfPairOfOrderAndInt32", MongoNaming.ForType(typeof(Page<Pair<Order, int>>)));
    }

    [Fact]
    public void ForType_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MongoNaming.ForType(null!));
    }

    [Fact]
    public void Default_IsForType()
    {
        Assert.Equal(MongoNaming.ForType(typeof(Order)), MongoNaming.Default(typeof(Order)));
    }

    /// <summary>
    /// The convention is process-wide. As a settable property two components could each set it and
    /// the second won silently, so which collection a type mapped to depended on module
    /// initialisation order. The value chosen here is the default itself, so the assertion costs
    /// the rest of the suite nothing.
    /// </summary>
    [Fact]
    public void Use_ChoosesTheConventionOnceAndRefusesASecondTime()
    {
        MongoNaming.Use(MongoNaming.ForType);

        Assert.Equal("Order", MongoNaming.Default(typeof(Order)));
        Assert.Throws<InvalidOperationException>(() => MongoNaming.Use(type => type.Name));
    }

    [Fact]
    public void Use_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MongoNaming.Use(null!));
    }
}

public class MongoSetupTests
{
    [Fact]
    public void Defaults_AreUsableWithoutConfiguration()
    {
        var setup = new MongoSetup();

        Assert.Equal(MongoSetup.DefaultConventionPackName, setup.ConventionPackName);
        Assert.Null(setup.GridFsBucket);
        Assert.Null(setup.ConfigureClient);
        Assert.Null(setup.Conventions);
    }
}
