using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Transport;
using NServiceBus.Transport.Msmq.Timeouts;
using IsolationLevel = System.Transactions.IsolationLevel;

/// <summary>
///
/// </summary>
public class SqlTimeoutStorage : ITimeoutStorage
{
    const string SqlInsert = "INSERT INTO {0} (Id, Destination, Time, Headers, State) VALUES (@id, @destination, @time, @headers, @state);";
    const string SqlFetch = "SELECT TOP 1 Id, Destination, Time, Headers, State, RetryCount FROM {0} WITH (READPAST, UPDLOCK, ROWLOCK) WHERE Time < @time ORDER BY Time";
    const string SqlDelete = "DELETE {0} WHERE Id = @id";
    const string SqlUpdate = "UPDATE {0} SET RetryCount = RetryCount + 1 WHERE Id = @id";
    const string SqlGetNext = "SELECT TOP 1 Time FROM {0} ORDER BY Time";

    string schema;
    string tableName;
    CreateSqlConnection createSqlConnection;
    TransportTransactionMode transactionMode;

    string insertCommand;
    string removeCommand;
    string bumpFailureCountCommand;
    string nextCommand;
    string fetchCommand;

    /// <summary>
    ///
    /// </summary>
    /// <param name="connectionString"></param>
    /// <param name="schema"></param>
    /// <param name="tableName"></param>
    public SqlTimeoutStorage(string connectionString, string schema = null, string tableName = null)
    {
        createSqlConnection = () => Task.FromResult(new SqlConnection(connectionString));
        this.tableName = tableName;
        this.schema = schema;

        insertCommand = string.Format(SqlInsert, tableName);
        removeCommand = string.Format(SqlDelete, tableName);
        bumpFailureCountCommand = string.Format(SqlUpdate, tableName);
        nextCommand = string.Format(SqlGetNext, tableName);
        fetchCommand = string.Format(SqlFetch, tableName);
    }

    /// <inheritdoc />
    public async Task Store(TimeoutItem timeout, TransportTransaction transaction)
    {
        using (var cmd = CreateCommand(insertCommand, transaction))
        {
            cmd.Parameters.AddWithValue("@id", timeout.Id);
            cmd.Parameters.AddWithValue("@destination", timeout.Destination);
            cmd.Parameters.AddWithValue("@time", timeout.Time);
            cmd.Parameters.AddWithValue("@headers", timeout.Headers);
            cmd.Parameters.AddWithValue("@state", timeout.State);
            _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }


    /// <inheritdoc />
    public async Task<bool> Remove(TimeoutItem timeout, TransportTransaction transaction)
    {
        using (var cmd = CreateCommand(removeCommand, transaction))
        {
            cmd.Parameters.AddWithValue("@id", timeout.Id);
            var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            return affected == 1;
        }
    }

    /// <inheritdoc />
    public async Task<bool> BumpFailureCount(TimeoutItem timeout)
    {
        using (new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
        {
            using (var cn = await createSqlConnection().ConfigureAwait(false))
            using (var cmd = new SqlCommand(bumpFailureCountCommand, cn))
            {
                cmd.Parameters.AddWithValue("@id", timeout.Id);
                await cn.OpenAsync().ConfigureAwait(false);
                var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                return affected == 1;
            }
        }
    }

    /// <inheritdoc />
    public Task Initialize(string queueName, TransportTransactionMode transportTransactionMode, CancellationToken cancellationToken)
    {
        if (tableName == null)
        {
            if (schema != null)
            {
                tableName = $"[{schema}]";
            }

            tableName += $"[{queueName}Timeouts]";
        }

        transactionMode = transportTransactionMode;

        var creator = new TimeoutTableCreator(createSqlConnection, tableName);
        return creator.CreateIfNecessary(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> Next()
    {
        using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
        {
            using (var cn = await createSqlConnection().ConfigureAwait(false))
            using (var cmd = new SqlCommand(nextCommand, cn))
            {
                await cn.OpenAsync().ConfigureAwait(false);
                var result = (DateTime?)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return result.HasValue ? (DateTimeOffset?)new DateTimeOffset(result.Value, TimeSpan.Zero) : null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<TimeoutItem> FetchNextDueTimeout(DateTimeOffset at, TransportTransaction transaction)
    {
        TimeoutItem result = null;
        using (var cmd = CreateCommand(fetchCommand, transaction))
        {
            cmd.Parameters.AddWithValue("@time", at.UtcDateTime);

            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false))
            {
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    result = new TimeoutItem
                    {
                        Id = (string)reader[0],
                        Destination = (string)reader[1],
                        Time = (DateTime)reader[2],
                        Headers = (byte[])reader[3],
                        State = (byte[])reader[4],
                        NumberOfRetries = (int)reader[5]
                    };
                }
            }
        }

        return result;
    }

    static SqlCommand CreateCommand(string commandText, TransportTransaction transaction)
    {
        var cn = transaction.Get<SqlConnection>();
        transaction.TryGet(out SqlTransaction sqlTransaction);

        var command = new SqlCommand(commandText, cn, sqlTransaction);
        return command;
    }

    /// <inheritdoc />
    public TransportTransaction CreateTransaction()
    {
        Log.Info("TransportTransactionMode = {transactionMode}");
        if (transactionMode == TransportTransactionMode.TransactionScope)
        {
            Log.Info("Creating TransactionScope");
            //HINT: Can't make async because the scope would be lost
            var scope = new TransactionScope(TransactionScopeOption.RequiresNew,
                new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                TransactionScopeAsyncFlowOption.Enabled);

            var transaction = new TransportTransaction();
            transaction.Set(scope);
            transaction.Set(Transaction.Current);
            transaction.Set(new TransactionRelease(transaction)); //HINT: Makes sure we release the transaction because the storage owns it

            return transaction;
        }

        return new TransportTransaction();
    }

    /// <inheritdoc />
    public async Task BeginTransaction(TransportTransaction transaction)
    {
        var connection = await createSqlConnection().ConfigureAwait(false);
        await connection.OpenAsync().ConfigureAwait(false);
        transaction.Set(connection);

        if (transaction.TryGet(out Transaction distributedTransaction))
        {
            connection.EnlistTransaction(distributedTransaction);
        }
        else
        {
            var sqlTransaction = connection.BeginTransaction();
            transaction.Set(sqlTransaction);
        }
    }

    /// <inheritdoc />
    public Task CommitTransaction(TransportTransaction transaction)
    {
        var connection = transaction.Get<SqlConnection>();

        if (transaction.TryGet(out TransactionScope scope)) // SHould ONLY happen in the poller
        {
            connection.Dispose();
            transaction.Remove<SqlConnection>();

            scope.Complete();
        }
        else if (transaction.TryGet(out SqlTransaction sqlTransaction))
        {
            sqlTransaction.Commit();
        }

        return Task.CompletedTask;
    }

    static readonly ILog Log = LogManager.GetLogger<SqlTimeoutStorage>();

    /// <inheritdoc />
    public Task DisposeTransaction(TransportTransaction transaction)
    {
        if (transaction.TryGet(out SqlTransaction sqlTransaction))
        {
            Log.Info("Dispose SqlTransaction");
            sqlTransaction.Dispose();
        }
        if (transaction.TryGet(out SqlConnection connection))
        {
            Log.Info("Dispose SqlConnection");
            connection.Dispose();
        }
        if (transaction.TryGet(out TransactionRelease release))
        {
            release.Dispose();
        }
        return Task.CompletedTask;
    }

    class TransactionRelease : IDisposable
    {
        TransportTransaction transaction;

        public TransactionRelease(TransportTransaction transaction)
        {
            this.transaction = transaction;
        }

        public void Dispose()
        {
            if (transaction.TryGet(out TransactionScope scope))
            {
                scope.Dispose();
            }
        }
    }
}
