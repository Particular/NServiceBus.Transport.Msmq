using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 
/// </summary>
public interface ITimeoutStorage
{
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    Task<DateTimeOffset> Next();
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
    /// <param name="at"></param>
    /// <returns></returns>
    Task<List<TimeoutItem>> FetchDueTimeouts(DateTimeOffset at);
    ///// <summary>
    ///// 
    ///// </summary>
    ///// <returns></returns>
    //Task Begin();
    ///// <summary>
    ///// 
    ///// </summary>
    ///// <returns></returns>
    //Task Commit();
}