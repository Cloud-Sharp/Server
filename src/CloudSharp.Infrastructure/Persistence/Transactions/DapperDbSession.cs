using System.Data;
using System.Data.Common;
using CloudSharp.Infrastructure.Persistence.DbContext;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CloudSharp.Infrastructure.Persistence.Transactions;

/// <summary>
/// Dapper 명령이 현재 <see cref="AppDbContext"/>의 연결과 트랜잭션을 사용하도록 구성한다.
/// </summary>
internal sealed class DapperDbSession(AppDbContext dbContext)
{
    /// <summary>
    /// DbContext가 소유하는 데이터베이스 연결을 반환한다.
    /// 호출자는 이 연결을 직접 폐기하면 안 된다.
    /// </summary>
    public DbConnection Connection => dbContext.Database.GetDbConnection();

    /// <summary>
    /// 현재 EF Core 트랜잭션을 자동으로 결합한 Dapper 명령을 만든다.
    /// </summary>
    public CommandDefinition CreateCommand(
        string commandText,
        object? parameters = null,
        int? commandTimeout = null,
        CommandType? commandType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        return new CommandDefinition(
            commandText,
            parameters,
            transaction,
            commandTimeout,
            commandType,
            cancellationToken: cancellationToken);
    }
}
