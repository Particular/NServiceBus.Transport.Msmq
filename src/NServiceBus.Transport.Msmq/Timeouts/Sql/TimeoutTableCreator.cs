namespace NServiceBus.Transport.Msmq.Timeouts
{
    using System.Data;
    using System.Data.SqlClient;
    using System.Threading.Tasks;
    using System.Threading;
    
    /// <summary>
    /// 
    /// </summary>
    public delegate Task<SqlConnection> CreateSqlConnection();

    class TimeoutTableCreator
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="createSqlConnection"></param>
        /// <param name="tableName"></param>
        public TimeoutTableCreator(CreateSqlConnection createSqlConnection, string tableName)
        {
            this.tableName = tableName;
            this.createSqlConnection = createSqlConnection;
        }

        public async Task CreateIfNecessary(CancellationToken cancellationToken = default)
        {
            var sql = string.Format(CreateScript, tableName);
            using (var connection = await createSqlConnection().ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await Execute(sql, connection, cancellationToken).ConfigureAwait(false);
            }
        }

        static async Task Execute(string sql, SqlConnection connection, CancellationToken cancellationToken)
        {
            try
            {
                using (var transaction = connection.BeginTransaction())
                {
                    using (var command = new SqlCommand(sql, connection, transaction))
                    {
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    transaction.Commit();
                }
            }
            catch (SqlException e) when (e.Number == 2714 || e.Number == 1913) //Object already exists
            {
                //Table creation scripts are based on sys.objects metadata views.
                //It looks that these views are not fully transactional and might
                //not return information on already created table under heavy load.
                //This in turn can result in executing table create or index create queries
                //for objects that already exists. These queries will fail with
                // 2714 (table) and 1913 (index) error codes.
            }
        }

        CreateSqlConnection createSqlConnection;
        string tableName;

        const string CreateScript = @"
if not exists (
    select * from sys.objects
    where
        object_id = object_id('{0}')
        and type in ('U')
)
begin
    create table {0} (
 	    Id nvarchar(250) not null primary key,
        Destination nvarchar(200),
        State varbinary(max),
        Time datetime,
        Headers varbinary(max) not null,
        PersistenceVersion varchar(23) not null -- Backwards compatibility with SQLP schema
        )
end

if not exists
(
    select *
    from sys.indexes
    where
        name = 'Index_Time' and
        object_id = object_id('{0}')
)
begin
    create index Index_Time on {0} (Time);
end
";
    }
}