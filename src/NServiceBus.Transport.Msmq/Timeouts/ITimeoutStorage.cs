using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface ITimeoutStorage
{
    Task<DateTimeOffset> Next();
    Task Store(Timeout entity);
    Task<bool> Remove(Timeout entity);
    Task<List<Timeout>> FetchDueTimeouts(DateTimeOffset at);
    Task Begin();
    Task Commit();
}