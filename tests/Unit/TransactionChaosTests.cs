using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// The transaction helper under a body that misbehaves. The client points at a closed port, so the driver's own
/// session, retry loop and abort run here without a server.
/// </summary>
public class TransactionChaosTests
{
    /// <summary>
    /// The driver starts a session and a transaction without asking the token, and a body that sends nothing
    /// commits without a server, so a token cancelled before the call used to run the body once. The helper asks
    /// the token first.
    /// </summary>
    [Fact]
    public async Task InTransactionAsync_WithACancelledToken_DoesNotRunTheBody()
    {
        using var client = new MongoClient(Given.UnreachableConnection);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var runs = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InTransactionAsync(
            (_, _) =>
            {
                runs++;

                return Task.CompletedTask;
            },
            cancellationToken: cancellation.Token));

        Assert.Equal(0, runs);
    }

    /// <summary>
    /// The driver retries a body that fails with a transient label for up to two minutes. A body that ignores the
    /// token and keeps failing must still stop as soon as the caller cancels: the driver aborts with the token
    /// between attempts, and that abort is what ends the loop.
    /// </summary>
    [Fact]
    public async Task InTransactionAsync_WhenCancelledWhileTheBodyKeepsFailingTransiently_StopsAtOnce()
    {
        using var client = new MongoClient(Given.UnreachableConnection);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var transaction = client.InTransactionAsync(
            async (_, _) =>
            {
                await Task.Delay(10, CancellationToken.None);
                throw Transient();
            },
            cancellationToken: cancellation.Token);

        var first = await Task.WhenAny(transaction, Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Same(transaction, first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transaction);
    }

    private static MongoException Transient()
    {
        var exception = new MongoException("The primary stepped down.");
        exception.AddErrorLabel("TransientTransactionError");

        return exception;
    }
}
