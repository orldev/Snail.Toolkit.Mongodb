using BenchmarkDotNet.Attributes;
using MongoDB.Driver;
using Snail.Toolkit.Mongodb.Extensions;

namespace Snail.Toolkit.Mongodb.Benchmarks;

/// <summary>A transaction around work that completes at once, through the driver's own session and transaction.</summary>
/// <remarks>
/// A transaction whose work sends nothing starts and commits without a server, so the client points at a closed port
/// and the whole path is the driver's real one: the session, the transaction, the retry loop and the commit.
/// </remarks>
[MemoryDiagnoser]
public class TransactionBenchmarks
{
    private static readonly Task<int> One = Task.FromResult(1);

    private static readonly Func<IClientSessionHandle, CancellationToken, Task<int>> Counted = static (_, _) => One;

    private static readonly Func<IClientSessionHandle, CancellationToken, Task> Done = static (_, _) => Task.CompletedTask;

    private MongoClient _client = null!;

    /// <summary>A client that has nothing to connect to.</summary>
    /// <returns>A task that completes once both helpers were checked.</returns>
    [GlobalSetup]
    public async Task Setup()
    {
        _client = new MongoClient("mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100");

        await VerifyAsync().ConfigureAwait(false);
    }

    /// <summary>Releases the client.</summary>
    [GlobalCleanup]
    public void Cleanup() => _client.Dispose();

    /// <summary>A transaction that returns a result.</summary>
    /// <returns>The result.</returns>
    [Benchmark(Description = "InTransactionAsync<int>")]
    public Task<int> WithResult() => _client.InTransactionAsync(Counted);

    /// <summary>A transaction that returns nothing.</summary>
    /// <returns>A task that completes once the transaction committed.</returns>
    [Benchmark(Description = "InTransactionAsync")]
    public Task WithoutResult() => _client.InTransactionAsync(Done);

    /// <summary>Fails the run unless both helpers run their work once, inside a transaction, and hand back its result.</summary>
    /// <returns>A task that completes once both helpers were checked.</returns>
    private async Task VerifyAsync()
    {
        var runs = 0;

        var result = await _client.InTransactionAsync((session, _) =>
            {
                runs += session.IsInTransaction ? 1 : 100;

                return Task.FromResult(42);
            })
            .ConfigureAwait(false);

        await _client.InTransactionAsync((session, _) =>
            {
                runs += session.IsInTransaction ? 1 : 100;

                return Task.CompletedTask;
            })
            .ConfigureAwait(false);

        if (result != 42 || runs != 2)
            throw new InvalidOperationException($"The transactions ran {runs} times and returned {result}.");
    }
}
