namespace NServiceBus
{
    using System;
    using System.Data;
    using System.Data.SqlClient;
    using System.Threading;
    using System.Threading.Tasks;
    using Transport.Msmq.Timeouts;

    //TODO: Either we should expect that the connection created by the factory is open of we should make it non-async
    /// <summary>
    /// Factory method for creating SQL Server connections.
    /// </summary>
    public delegate Task<SqlConnection> CreateSqlConnection();

    /// <summary>
    /// Implementation of the delayed message store based on the SQL Server.
    /// </summary>
    public class SqlServerDelayedMessageStore : IDelayedMessageStore
    {
        string schema;
        string tableName;
        CreateSqlConnection createSqlConnection;

        string insertCommand;
        string removeCommand;
        string bumpFailureCountCommand;
        string nextCommand;
        string fetchCommand;

        /// <summary>
        /// Creates a new instance of the SQL Server delayed message store.
        /// </summary>
        /// <param name="connectionString">Connection string to the SQL Server database.</param>
        /// <param name="schema">(optional) schema to use. Defaults to dbo</param>
        /// <param name="tableName">(optional) name of the table where delayed messages are stored. Defaults to name of the endpoint with .Delayed suffix.</param>
        public SqlServerDelayedMessageStore(string connectionString, string schema = null, string tableName = null)
            : this(() => Task.FromResult(new SqlConnection(connectionString)), schema, tableName)
        {
        }

        /// <summary>
        /// Creates a new instance of the SQL Server delayed message store.
        /// </summary>
        /// <param name="connectionFactory">Factory for database connections.</param>
        /// <param name="schema">(optional) schema to use. Defaults to dbo</param>
        /// <param name="tableName">(optional) name of the table where delayed messages are stored. Defaults to name of the endpoint with .Delayed suffix.</param>
        public SqlServerDelayedMessageStore(CreateSqlConnection connectionFactory, string schema = null, string tableName = null)
        {
            createSqlConnection = connectionFactory;
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
        public async Task<bool> IncrementFailureCount(TimeoutItem timeout)
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
        public async Task Initialize(string queueName, TransportTransactionMode transactionMode, CancellationToken cancellationToken)
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

            insertCommand = string.Format(SqlConstants.SqlInsert, tableName);
            removeCommand = string.Format(SqlConstants.SqlDelete, tableName);
            bumpFailureCountCommand = string.Format(SqlConstants.SqlUpdate, tableName);
            nextCommand = string.Format(SqlConstants.SqlGetNext, tableName);
            fetchCommand = string.Format(SqlConstants.SqlFetch, tableName);
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
}