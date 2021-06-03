using System;
using System.Collections.Generic;
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
    const string SqlInsertFormat = "INSERT INTO {0} (Id,Destination,Time,Headers,State,PersistenceVersion) Values (@id,@destination,@time,@headers,@state,'1');";

    /// <summary>
    /// 
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public Task<bool> Remove(TimeoutItem timeout)
    {
        return Task.FromResult(true);
        // using (var cn = new SqlConnection(CS))
        // using (var cmd = new SqlCommand(SqlDelete, cn, tx))
        // {
        //     cmd.Parameters["@id"].Value = timeout.Id;
        //     var affected = await cmd.ExecuteNonQueryAsync();
        //     return affected == 1;
        // }
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
    public Task<DateTimeOffset?> Next()
    //public async Task<DateTimeOffset> Next()
    {
        //var next = await connection.ExecuteScalarAsync<DateTime?>("Select top 1 Time FROM timeout ORDER BY Time").ConfigureAwait(false);

        
        return Task.FromResult((DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(1));
        //var cmd = new SqlCommand("Select top 1 Time FROM timeout ORDER BY Time", cn, tx);
        //return new DateTimeOffset((DateTime)await cmd.ExecuteScalarAsync(), TimeSpan.Zero);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="at"></param>
    /// <returns></returns>
    public Task<List<TimeoutItem>> FetchDueTimeouts(DateTimeOffset at)
    {
        //                    var timeouts = (await connection.QueryAsync<Timeout>("Select top 100 * FROM timeout WITH  (updlock, rowlock) WHERE Time<@Time ORDER BY Time, Id", new {Time = now}).ConfigureAwait(false)).ToList();

        
        
        var result = new List<TimeoutItem>(100);
        // var cmd = new SqlCommand(SqlFetch);
        // cmd.Parameters["@time"].Value = at.UtcDateTime;
        // var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult);
        // while (await reader.ReadAsync())
        // {
        //     result.Add(new Timeout
        //     {
        //         Id = (string)reader[0],
        //         Destination = (string)reader[1],
        //         Time = (DateTime)reader[2],
        //         Headers = (byte[])reader[3],
        //         State = (byte[])reader[4]
        //     });
        // }

        return Task.FromResult(result);
    }

    const string SqlDelete = "DELETE timeout WHERE Id = @Id";
    const string SqlFetch = "Select top 100 * FROM timeout WITH  (updlock, rowlock) WHERE Time<@time ORDER BY Time, Id";
    
    const string SqlCreate = @"

CREATE TABLE dbo.Timeout
	(
	    Id uniqueidentifier not null primary key,
	    Destination nvarchar(200), 
	    State varbinary(max), 
	    Time datetime,  
	    Headers nvarchar(max) not null,
	    PersistenceVersion varchar(23) not null
	);
	";
}
