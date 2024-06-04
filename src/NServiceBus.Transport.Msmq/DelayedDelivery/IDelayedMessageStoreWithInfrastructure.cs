namespace NServiceBus
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An <see cref="IDelayedMessageStore" /> that sets up infrastructure.
    /// </summary>
    public interface IDelayedMessageStoreWithInfrastructure : IDelayedMessageStore
    {
        /// <summary>
        /// Create tables or other infrastructure. e.g. creates required database artifacts etc.
        /// Called after <see cref="IDelayedMessageStore.Initialize(string, TransportTransactionMode, CancellationToken)"/>.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token set if the endpoint begins to shut down while the SetupInfrastructure method is executing.</param>
        Task SetupInfrastructure(CancellationToken cancellationToken = default);
    }
}