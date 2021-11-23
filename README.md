# NServiceBus.Transport.Msmq

MSMQ transport for NServiceBus

NOTE: As Microsoft is not making MSMQ available for .NET Core, building new systems using MSMQ is not recommended. See https://particular.net/blog/msmq-is-dead

Documentation can be found at <https://docs.particular.net/transports/msmq/>.

## How to test locally

* MSMQ needs to be installed and enabled on the current machine.See the [MSMQ configuration documentation](https://docs.particular.net/transports/msmq/?version=msmqtransport_2#msmq-configuration) on how to install MSMQ.
* A MSSQL Server needs to be accessible either via:
  * A local SQL Express installation containing a `nservicebus` database.
  * A custom SQL Server connection string in provided via the `SqlServerConnectionString` environment variable
