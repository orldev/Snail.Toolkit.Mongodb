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
