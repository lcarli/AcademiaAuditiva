using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// When armed, fails the commit of a transaction that ran a DELETE on
/// AspNetUsers, after SQL Server has executed every statement in it. Rows
/// deleted by that same transaction must come back with the rollback; rows
/// committed by an earlier SaveChanges would stay deleted.
/// </summary>
public sealed class FailCommitAfterUserDelete : IDbCommandInterceptor, IDbTransactionInterceptor
{
    public const string Message = "Simulated failure while committing the account deletion.";

    private bool _armed;
    private bool _userDeleted;

    public void Arm() => (_armed, _userDeleted) = (true, false);

    public void Disarm() => (_armed, _userDeleted) = (false, false);

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Track(command);
        return result;
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Track(command);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Track(command);
        return result;
    }

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Track(command);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult TransactionCommitting(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        ThrowIfUserDeleted();
        return result;
    }

    public ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUserDeleted();
        return ValueTask.FromResult(result);
    }

    private void Track(DbCommand command)
    {
        if (_armed && command.CommandText.Contains("DELETE FROM [AspNetUsers]", StringComparison.Ordinal))
        {
            _userDeleted = true;
        }
    }

    private void ThrowIfUserDeleted()
    {
        if (_armed && _userDeleted)
        {
            throw new InvalidOperationException(Message);
        }
    }
}
