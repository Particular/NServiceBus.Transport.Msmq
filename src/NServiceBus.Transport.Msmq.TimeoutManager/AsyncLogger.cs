using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

class AsyncLogger
{
    static readonly BlockingCollection<(ConsoleColor, string)> Logs = new BlockingCollection<(ConsoleColor, string)>();

    static AsyncLogger()
    {
        _ = Task.Run(MessageLoop);
    }

    static ConsoleColor current;

    public static void WL(string format, params object[] args)
    {
        var now = DateTime.UtcNow;
        var value = string.Format(now.ToString("s") + ": " + format, args);
        Logs.Add((ConsoleColor.Gray, value));
    }

    public static void WL(ConsoleColor color, string format, params object[] args)
    {
        var now = DateTime.UtcNow;
        var value = string.Format(now.ToString("s") + ": " + format, args);
        Logs.Add((color, value));
    }

    static void MessageLoop()
    {
        while (Logs.TryTake(out var item, -1))
        {
            if (current != item.Item1)
            {
                Console.ForegroundColor = current = item.Item1;
            }
            Console.WriteLine(item.Item2);
        }
    }
}