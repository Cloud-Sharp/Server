using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CloudSharp.Api.IntegrationTests.TestHost;

internal sealed class PostCommitRedisStopInterceptor(Func<Task> onCommitted)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly object _gate = new();
    private bool _armed;
    private bool _wroteInTransaction;
    private bool _triggered;

    internal void Arm()
    {
        lock (_gate)
        {
            _armed = true;
            _wroteInTransaction = false;
            _triggered = false;
        }
    }

    public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _wroteInTransaction = false;
        }

        return ValueTask.FromResult(result);
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _wroteInTransaction = true;
        }

        return ValueTask.FromResult(result);
    }

    public async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        bool shouldStop;
        lock (_gate)
        {
            shouldStop = _armed && _wroteInTransaction && !_triggered;
            if (shouldStop)
            {
                _triggered = true;
            }

            _wroteInTransaction = false;
        }

        if (shouldStop)
        {
            await onCommitted();
        }
    }

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _wroteInTransaction = false;
        }

        return Task.CompletedTask;
    }
}
