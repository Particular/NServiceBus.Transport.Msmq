// using System;
// using System.Collections.Generic;
// using System.Data;
// using System.Data.SqlClient;
// using System.Threading.Tasks;
//
// class InMemoryTimeoutStorage : ITimeoutStorage, IDisposable
// {
//     const string CS = "Server=.;Database=test2;Trusted_Connection=True;";
//
//     SqlConnection cn;
//     SqlTransaction tx;
//
//     public SqlTimeoutStorage()
//     {
//         cn = new SqlConnection(CS);
//     }
//
//     public async Task Store(Timeout timeout)
//     {
//         var cmd = new SqlCommand(SqlInsert);
//         cmd.Parameters["@id"].Value = timeout.Id;
//         cmd.Parameters["@destination"].Value = timeout.Destination;
//         cmd.Parameters["@time"].Value = timeout.Time;
//         cmd.Parameters["@headers"].Value = timeout.Headers;
//         cmd.Parameters["@state"].Value = timeout.State;
//         _ = await cmd.ExecuteNonQueryAsync();
//     }
//
//     public async Task<bool> Remove(Timeout timeout)
//     {
//         var cmd = new SqlCommand(SqlDelete, cn, tx);
//         cmd.Parameters["@id"].Value = timeout.Id;
//         var affected = await cmd.ExecuteNonQueryAsync();
//         return affected == 1;
//     }
//
//     public async Task<DateTimeOffset> Next()
//     {
//         var cmd = new SqlCommand("Select top 1 Time FROM timeout ORDER BY Time", cn, tx);
//         return new DateTimeOffset((DateTime)await cmd.ExecuteScalarAsync(), TimeSpan.Zero);
//     }
//
//     public Task Begin()
//     {
//         tx = cn.BeginTransaction();
//         return Task.CompletedTask;
//     }
//
//     public Task Commit()
//     {
//         tx.Commit();
//         return Task.CompletedTask;
//     }
//
//     public void Dispose()
//     {
//         cn?.Dispose();
//     }
//
//     public async Task<List<Timeout>> FetchDueTimeouts(DateTimeOffset at)
//     {
//         var result = new List<Timeout>(100);
//         var cmd = new SqlCommand(SqlFetch);
//         cmd.Parameters["@time"].Value = at.UtcDateTime;
//         var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult);
//         while (await reader.ReadAsync())
//         {
//             result.Add(new Timeout
//             {
//                 Id = (string)reader[0],
//                 Destination = (string)reader[1],
//                 Time = (DateTime)reader[2],
//                 Headers = (byte[])reader[3],
//                 State = (byte[])reader[4]
//             });
//         }
//
//         return result;
//     }
//
//     const string SqlInsert = "INSERT INTO timeout (Id,Destination,Time,Headers,State) Values (@Id,@Destination,@Time,@Headers,@State);";
//     const string SqlDelete = "DELETE timeout WHERE Id = @Id";
//     const string SqlFetch = "Select top 100 * FROM timeout WITH  (updlock, rowlock) WHERE Time<@time ORDER BY Time, Id";
// }
