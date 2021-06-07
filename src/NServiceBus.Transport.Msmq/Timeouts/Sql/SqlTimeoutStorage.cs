using System;
using System.Collections.Generic;
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
    string schema;
    string tableName;
    CreateSqlConnection createSqlConnection;

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

    /// <summary>
    ///
    /// </summary>
    /// <param name="timeout"></param>
    public async Task Store(TimeoutItem timeout)
    {
        using (var cn = await createSqlConnection().ConfigureAwait(false))
        {
            await cn.OpenAsync().ConfigureAwait(false);
            var sql = string.Format(SqlInsertFormat, tableName);
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@id", timeout.Id);
                cmd.Parameters.AddWithValue("@destination", timeout.Destination);
                cmd.Parameters.AddWithValue("@time", timeout.Time);
                cmd.Parameters.AddWithValue("@headers", timeout.Headers);
                cmd.Parameters.AddWithValue("@state", timeout.State);
                _ = await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    const string SqlInsertFormat =
        "INSERT INTO {0} (Id,Destination,Time,Headers,State,PersistenceVersion) Values (@id,@destination,@time,@headers,@state,'1');";

    /// <summary>
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public async Task<bool> Remove(TimeoutItem timeout)
    {
        var sql = string.Format(SqlDelete, tableName);
        using (var cn = await createSqlConnection())
        using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@id", timeout.Id);
            await cn.OpenAsync();
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected == 1;
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public async Task<bool> BumpFailureCount(TimeoutItem timeout)
    {
        var sql = string.Format(SqlUpdate, tableName);
        using (var cn = await createSqlConnection())
        using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@id", timeout.Id);
            await cn.OpenAsync();
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected == 1;
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="queueName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task Initialize(string queueName, CancellationToken cancellationToken)
    {
        if (tableName == null)
        {
            if (schema != null)
            {
                tableName = $"[{schema}]";
            }

            tableName += $"[{queueName}Timeouts]";
        }

        var creator = new TimeoutTableCreator(createSqlConnection, tableName);
        return creator.CreateIfNecessary(cancellationToken);
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public async Task<DateTimeOffset?> Next()
    {
        var sql = string.Format("Select top 1 Time FROM {0} ORDER BY Time", tableName);
        using (var cn = await createSqlConnection())
        using (var cmd = new SqlCommand(sql, cn))
        {
            await cn.OpenAsync().ConfigureAwait(false);
            var result = (DateTime?) await cmd.ExecuteScalarAsync();
            return result.HasValue ? (DateTimeOffset?) new DateTimeOffset(result.Value, TimeSpan.Zero) : null;
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="at"></param>
    /// <returns></returns>
    public async Task<List<TimeoutItem>> FetchDueTimeouts(DateTimeOffset at)
    {
        var sql = string.Format(SqlFetch, tableName);

        var result = new List<TimeoutItem>(100);

        using (var cn = await createSqlConnection())
        using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@time", at.UtcDateTime);

            await cn.OpenAsync().ConfigureAwait(false);
            var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult);
            while (await reader.ReadAsync())
            {
                result.Add(new TimeoutItem
                {
                    Id = (string) reader[0],
                    Destination = (string) reader[1],
                    Time = (DateTime) reader[2],
                    Headers = (byte[]) reader[3],
                    State = (byte[]) reader[4]
                });
            }
        }

        return result;
    }

    const string SqlFetch = "Select top 100 Id,Destination,Time,Headers,State FROM {0} WITH  (updlock, rowlock) WHERE Time<@time ORDER BY Time, Id";

    const string SqlDelete = "DELETE {0} WHERE Id = @id";
    const string SqlUpdate = "UPDATE {0} SET NrOfRetries = NrOfRetries + 1 WHERE Id = @id";
}