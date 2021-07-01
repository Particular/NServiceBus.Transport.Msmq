using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using NServiceBus.Transport.Msmq.Timeouts;

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
    }

    /// <inheritdoc />
    public async Task Store(TimeoutItem timeout)
    {
        using (var cn = await createSqlConnection().ConfigureAwait(false))
        using (var cmd = new SqlCommand(insertCommand, cn))
        {
            cmd.Parameters.AddWithValue("@id", timeout.Id);
            cmd.Parameters.AddWithValue("@destination", timeout.Destination);
            cmd.Parameters.AddWithValue("@time", timeout.Time);
            cmd.Parameters.AddWithValue("@headers", timeout.Headers);
            cmd.Parameters.AddWithValue("@state", timeout.State);
            await cn.OpenAsync().ConfigureAwait(false);
            _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }


    /// <inheritdoc />
    public async Task<bool> Remove(TimeoutItem timeout)
    {
        using (var cn = await createSqlConnection().ConfigureAwait(false))
        using (var cmd = new SqlCommand(removeCommand, cn))
        {
            cmd.Parameters.AddWithValue("@id", timeout.Id);
            await cn.OpenAsync().ConfigureAwait(false);
            var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            return affected == 1;
        }
    }

    /// <inheritdoc />
    public async Task<bool> BumpFailureCount(TimeoutItem timeout)
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

    /// <inheritdoc />
    public async Task Initialize(string queueName, CancellationToken cancellationToken)
    {
        if (tableName == null)
        {
            if (schema != null)
            {
                tableName = $"[{schema}]";
            }

            tableName += $"[{queueName}.timeouts]";
        }

        var creator = new TimeoutTableCreator(createSqlConnection, tableName);
        await creator.CreateIfNecessary(cancellationToken).ConfigureAwait(false);

        insertCommand = string.Format(SqlInsert, tableName);
        removeCommand = string.Format(SqlDelete, tableName);
        bumpFailureCountCommand = string.Format(SqlUpdate, tableName);
        nextCommand = string.Format(SqlGetNext, tableName);
        fetchCommand = string.Format(SqlFetch, tableName);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> Next()
    {
        using (var cn = await createSqlConnection().ConfigureAwait(false))
        using (var cmd = new SqlCommand(nextCommand, cn))
        {
            await cn.OpenAsync().ConfigureAwait(false);
            var result = (DateTime?)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return result.HasValue ? (DateTimeOffset?)new DateTimeOffset(result.Value, TimeSpan.Zero) : null;
        }
    }

    /// <inheritdoc />
    public async Task<TimeoutItem> FetchNextDueTimeout(DateTimeOffset at)
    {
        TimeoutItem result = null;
        using (var cn = await createSqlConnection().ConfigureAwait(false))
        using (var cmd = new SqlCommand(fetchCommand, cn))
        {
            cmd.Parameters.AddWithValue("@time", at.UtcDateTime);

            await cn.OpenAsync().ConfigureAwait(false);
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
}
