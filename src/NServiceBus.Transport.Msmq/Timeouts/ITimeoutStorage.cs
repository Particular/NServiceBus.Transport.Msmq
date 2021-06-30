using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///
/// </summary>
public interface ITimeoutStorage
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="endpointName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task Initialize(string endpointName, CancellationToken cancellationToken);
    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    Task<DateTimeOffset?> Next();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    Task Store(TimeoutItem entity);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    Task<bool> Remove(TimeoutItem entity);

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
    /// <returns></returns>
    Task<TimeoutItem> FetchNextDueTimeout(DateTimeOffset at);
    ///// <summary>
    /////
    ///// </summary>
    ///// <returns></returns>
    //TransportTransaction CreateTransaction();

    ///// <summary>
    ///// 
    ///// </summary>
    ///// <param name="transaction"></param>
    ///// <returns></returns>
    //Task BeginTransaction(TransportTransaction transaction);

    ///// <summary>
    /////
    ///// </summary>
    ///// <returns></returns>
    //Task CommitTransaction(TransportTransaction transaction);

    ///// <summary>
    ///// 
    ///// </summary>
    ///// <param name="transaction"></param>
    ///// <returns></returns>
    //Task DisposeTransaction(TransportTransaction transaction);
}
