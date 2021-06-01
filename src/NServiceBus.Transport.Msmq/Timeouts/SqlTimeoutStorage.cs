using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

/// <summary>
/// 
/// </summary>
public class SqlTimeoutStorage : ITimeoutStorage
{
    string CS = "Server=.;Database=test2;Trusted_Connection=True;";

    /// <summary>
    /// 
    /// </summary>
    /// <param name="connectionString"></param>
    public SqlTimeoutStorage(string connectionString)
    {
        CS = connectionString;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="timeout"></param>
    public async Task Store(TimeoutItem timeout)
    {
        using (var cn = new SqlConnection(CS))
        {
            using (var cmd = new SqlCommand(SqlInsert, cn))
            {
                cmd.Parameters["@id"].Value = timeout.Id;
                cmd.Parameters["@destination"].Value = timeout.Destination;
                cmd.Parameters["@time"].Value = timeout.Time;
                cmd.Parameters["@headers"].Value = timeout.Headers;
                cmd.Parameters["@state"].Value = timeout.State;
                _ = await cmd.ExecuteNonQueryAsync();
            }
        }
    }

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
    /// <returns></returns>
    public Task<DateTimeOffset> Next()
    //public async Task<DateTimeOffset> Next()
    {
        return Task.FromResult(DateTimeOffset.UtcNow.AddHours(1));
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

    const string SqlInsert = "INSERT INTO timeout (Id,Destination,Time,Headers,State) Values (@Id,@Destination,@Time,@Headers,@State);";
    const string SqlDelete = "DELETE timeout WHERE Id = @Id";
    const string SqlFetch = "Select top 100 * FROM timeout WITH  (updlock, rowlock) WHERE Time<@time ORDER BY Time, Id";
    

    // Id uniqueidentifier not null primary key,        Destination nvarchar(200),        SagaId uniqueidentifier,        State varbinary(max),        Time datetime,        Headers nvarchar(max) not null,        PersistenceVersion varchar(23) not null
    
    const string SqlCreate = @"CREATE TABLE dbo.Timeout
	(
	    Id uniqueidentifier not null primary key,
	    Destination nvarchar(200), 
	    State varbinary(max), 
	    Time datetime,  
	    Headers nvarchar(max) not null,    
	    PersistenceVersion varchar(23) not null
	);


	"
}
