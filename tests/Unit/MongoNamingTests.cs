namespace Snail.Toolkit.Mongodb.Tests.Unit;

public class MongoNamingTests
{
    private sealed record Order;

    private sealed record Page<T>;

    private sealed record Pair<TFirst, TSecond>;

    private sealed class Catalogue<T>
    {
        internal sealed record Entry;
    }

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
    /// A type nested inside a generic one reports <c>IsGenericType</c> true while its own name
    /// carries no arity marker, so slicing at the backtick sliced at -1 and threw. Two such types
    /// under different arguments must also not collapse onto one collection name, or documents of
    /// two shapes end up in the same collection.
    /// </summary>
    [Fact]
    public void ForType_WhenNestedInAGenericType_NamesItAfterTheArguments()
    {
        Assert.Equal("EntryOfOrder", MongoNaming.ForType(typeof(Catalogue<Order>.Entry)));

        Assert.NotEqual(
            MongoNaming.ForType(typeof(Catalogue<Order>.Entry)),
            MongoNaming.ForType(typeof(Catalogue<Page<Order>>.Entry)));
    }

    /// <summary>
    /// The convention is process-wide. As a settable property two components could each set it and
    /// the second won silently, so which collection a type mapped to depended on module
    /// initialisation order. The value chosen here is the default itself, so the assertion costs
    /// the rest of the suite nothing.
    /// </summary>
    [Fact]
    public void Use_UnderParallelCallers_AcceptsExactlyOne()
    {
        var accepted = 0;
        var refused = 0;

        Parallel.For(0, 64, _ =>
        {
            try
            {
                MongoNaming.Use(MongoNaming.ForType);
                Interlocked.Increment(ref accepted);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref refused);
            }
        });

        Assert.Equal(1, accepted);
        Assert.Equal(63, refused);
        Assert.Equal("Order", MongoNaming.Default(typeof(Order)));
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
