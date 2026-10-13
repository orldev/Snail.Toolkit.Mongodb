using System.Reflection;

namespace Snail.Toolkit.Mongodb.Tests;

/// <summary>Answers the calls of a driver interface with a handler.</summary>
public class Stub : DispatchProxy
{
    private Func<MethodInfo, object?[], object?> _handler = null!;

    /// <summary>A stand-in for <typeparamref name="T"/> whose every call goes to <paramref name="handler"/>.</summary>
    public static T Of<T>(Func<MethodInfo, object?[], object?> handler)
        where T : class
    {
        var stand = Create<T, Stub>();

        ((Stub)(object)stand)._handler = handler;

        return stand;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        _handler(targetMethod!, args ?? []);
}
