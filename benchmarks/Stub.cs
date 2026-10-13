using System.Reflection;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Benchmarks;

/// <summary>Answers the calls of a driver interface with a handler, standing in for an object that would reach a server.</summary>
/// <remarks>Public and unsealed because <see cref="DispatchProxy"/> derives the stand-in from it.</remarks>
public class Stub : DispatchProxy
{
    private Func<MethodInfo, object?[], object?> _handler = null!;

    /// <summary>A stand-in for <typeparamref name="T"/> whose every call goes to <paramref name="handler"/>.</summary>
    /// <param name="handler">Answers a call from the method called and its arguments.</param>
    /// <typeparam name="T">The interface to stand in for.</typeparam>
    /// <returns>The stand-in.</returns>
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

/// <summary>A cursor over one batch that is already in memory, as the driver's cursor is after its first reply.</summary>
/// <param name="batch">The documents of the batch.</param>
/// <typeparam name="T">The document type.</typeparam>
public sealed class Batch<T>(List<T> batch) : IAsyncCursor<T>
{
    private static readonly Task<bool> Moved = Task.FromResult(true);

    private static readonly Task<bool> Exhausted = Task.FromResult(false);

    private bool _isRead;

    /// <inheritdoc />
    public IEnumerable<T> Current => batch;

    /// <inheritdoc />
    public bool MoveNext(CancellationToken cancellationToken = default)
    {
        if (_isRead)
            return false;

        _isRead = true;

        return true;
    }

    /// <inheritdoc />
    public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) =>
        MoveNext(cancellationToken) ? Moved : Exhausted;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
