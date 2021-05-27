using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class DummyTimeoutStorage : ITimeoutStorage
{
    public Task Store(TimeoutItem timeout)
    {
        return Task.CompletedTask;
    }

    public Task<bool> Remove(TimeoutItem timeout)
    {
        return Task.FromResult(true);
    }

    public Task<DateTimeOffset> Next()
    {
        return Task.FromResult(DateTimeOffset.UtcNow.AddYears(1));
    }

    public Task<List<TimeoutItem>> FetchDueTimeouts(DateTimeOffset at)
    {
        var result = new List<TimeoutItem>(100);
        return Task.FromResult(result);
    }
}
