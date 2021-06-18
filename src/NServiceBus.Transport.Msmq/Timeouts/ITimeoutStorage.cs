using System;
using System.Threading;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Transport;

/// <summary>
///
/// </summary>
public interface ITimeoutStorage
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="endpointName"></param>
    /// <param name="transportTransactionMode"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task Initialize(string endpointName, TransportTransactionMode transportTransactionMode, CancellationToken cancellationToken);
    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    Task<DateTimeOffset?> Next();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="transportTransaction"></param>
    /// <returns></returns>
    Task Store(TimeoutItem entity, TransportTransaction transportTransaction);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="transaction"></param>
    /// <returns></returns>
    Task<bool> Remove(TimeoutItem entity, TransportTransaction transaction);

    /// <summary>
    ///
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    Task<bool> BumpFailureCount(TimeoutItem timeout);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="at"></param>
    /// <param name="transaction"></param>
    /// <returns></returns>
    Task<TimeoutItem> FetchNextDueTimeout(DateTimeOffset at, TransportTransaction transaction);
    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    TransportTransaction PrepareTransaction();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="transaction"></param>
    /// <returns></returns>
    Task BeginTransaction(TransportTransaction transaction);

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    Task CommitTransaction(TransportTransaction transaction);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="transaction"></param>
    /// <returns></returns>
    Task ReleaseTransaction(TransportTransaction transaction);
}
