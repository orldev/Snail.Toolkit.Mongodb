namespace Snail.Toolkit.Mongodb.Extensions;

/// <summary>
/// Transaction helpers over the driver's own <see cref="IMongoClient"/>.
/// </summary>
public static class MongoClientExtensions
{
    /// <summary>
    /// Runs <paramref name="body"/> inside a transaction, retrying it on transient transaction
    /// errors and committing when it returns.
    /// </summary>
    /// <remarks>
    /// Requires a replica set or a sharded cluster; a standalone server does not support transactions.
    /// Pass the session on to every operation that must take part, for example
    /// <c>collection.InsertOneAsync(session, document, cancellationToken: ct)</c> — work that ignores
    /// the session is not part of the transaction.
    /// </remarks>
    /// <param name="client">The client.</param>
    /// <param name="body">The work to run. It may be invoked more than once.</param>
    /// <param name="transactionOptions">Read concern, write concern and read preference for the transaction.</param>
    /// <param name="sessionOptions">Options for the session backing the transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <returns>A task whose result is what <paramref name="body"/> returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="body"/> is null.</exception>
    public static async Task<TResult> InTransactionAsync<TResult>(
        this IMongoClient client,
        Func<IClientSessionHandle, CancellationToken, Task<TResult>> body,
        TransactionOptions? transactionOptions = null,
        ClientSessionOptions? sessionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(body);

        using var session = await client.StartSessionAsync(sessionOptions, cancellationToken)
            .ConfigureAwait(false);

        return await session.WithTransactionAsync(
                async (handle, token) => await body(handle, token).ConfigureAwait(false),
                transactionOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="body"/> inside a transaction, retrying it on transient transaction
    /// errors and committing when it returns.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="body">The work to run. It may be invoked more than once.</param>
    /// <param name="transactionOptions">Read concern, write concern and read preference for the transaction.</param>
    /// <param name="sessionOptions">Options for the session backing the transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="body"/> is null.</exception>
    public static Task InTransactionAsync(
        this IMongoClient client,
        Func<IClientSessionHandle, CancellationToken, Task> body,
        TransactionOptions? transactionOptions = null,
        ClientSessionOptions? sessionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        return client.InTransactionAsync<object?>(
            async (handle, token) =>
            {
                await body(handle, token).ConfigureAwait(false);
                return null;
            },
            transactionOptions,
            sessionOptions,
            cancellationToken);
    }
}
