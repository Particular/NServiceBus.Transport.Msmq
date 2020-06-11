using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NServiceBus.Transport.Msmq
{
    class SoftTimeToBeReceivedStrategy : TimeToBeReceivedStrategy
    {
        public override bool DiscardDueToElapsedTtbr(Dictionary<string, string> headers)
        {
            if(!TryGetTtbr(headers, out var ttbr))
            {
                return false;
            }

            if(!TryGetTimeSent(headers, out var timeSent))
            {
                return false;
            }

            var cutOff = timeSent + ttbr;
            var receiveTime = DateTime.UtcNow;

            if(cutOff < receiveTime)
            {
                if (Logger.IsDebugEnabled)
                {
                    Logger.Debug($"Discarding message due to expired Time to be Received header. Time Sent: {timeSent} TTBR: {ttbr} Cut Off: {cutOff} Receive Time: {receiveTime} ");
                }
                return true;
            }

            return false;
        }

        bool TryGetTtbr(Dictionary<string, string> headers, out TimeSpan ttbr)
        {
            ttbr = TimeSpan.Zero;
            return headers.TryGetValue(Headers.TimeToBeReceived, out var ttbrString)
                && TimeSpan.TryParse(ttbrString, out ttbr);
        }

        bool TryGetTimeSent(Dictionary<string, string> headers, out DateTime timeSent)
        {
            if(headers.TryGetValue(Headers.TimeSent, out var timeSentString))
            {
                timeSent = ToUtcDateTime(timeSentString);
                return true;
            }

            timeSent = DateTime.MinValue;
            return false;
        }

        const string Format = "yyyy-MM-dd HH:mm:ss:ffffff Z";

        public static DateTime ToUtcDateTime(string wireFormattedString)
        {
            return DateTime.ParseExact(wireFormattedString, Format, CultureInfo.InvariantCulture)
               .ToUniversalTime();
        }

        ILog Logger = LogManager.GetLogger<SoftTimeToBeReceivedStrategy>();
    }
}