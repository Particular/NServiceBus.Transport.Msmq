using System;

/// <summary>
/// 
/// </summary>
public class TimeoutItem
{
    /// <summary>
    /// 
    /// </summary>
    public DateTime Time { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public string Id { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public byte[] State { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public byte[] Headers { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public string Destination { get; set; }
}

//
// class TimeoutProcessor
// {
//     MessageQueue inputQueue
//
//     public void Init()
//     {
//         inputQueue = new MessageQueue("timeouts", false, true, QueueAccessMode.Receive);
//     }
// }
//
//
// class TimeoutDispatcher
// {
//     bool UseConnectionCache;
//     
//     public void Dispath(Timeout timeout)
//     {
//         var destinationAddress = MsmqAddress.Parse(timeout.Destination);
//         
//         using (var q = new MessageQueue(destinationAddress.FullPath, false, UseConnectionCache, QueueAccessMode.Send))
//         {
//             using (var toSend = MsmqUtilities.Convert(message, dispatchProperties))
//             {
//                 var useDeadLetterQueue = dispatchProperties.ShouldUseDeadLetterQueue();
//                 if (useDeadLetterQueue.HasValue)
//                 {
//                     toSend.UseDeadLetterQueue = useDeadLetterQueue.Value;
//                 }
//                 else
//                 {
//                     var ttbrRequested = toSend.TimeToBeReceived < MessageQueue.InfiniteTimeout;
//                     toSend.UseDeadLetterQueue = ttbrRequested
//                         ? transportSettings.UseDeadLetterQueueForMessagesWithTimeToBeReceived
//                         : transportSettings.UseDeadLetterQueue;
//                 }
//
//                 toSend.UseJournalQueue = dispatchProperties.ShouldUseJournalQueue() ??
//                                          transportSettings.UseJournalQueue;
//
//                 toSend.TimeToReachQueue = transportSettings.TimeToReachQueue;
//
//                 if (message.Headers.TryGetValue(Headers.ReplyToAddress, out var replyToAddress))
//                 {
//                     toSend.ResponseQueue = new MessageQueue(MsmqAddress.Parse(replyToAddress).FullPath);
//                 }
//
//                 var label = GetLabel(message);
//
//                 if (transportOperation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
//                 {
//                     q.Send(toSend, label, GetIsolatedTransactionType());
//                     return;
//                 }
//
//                 if (TryGetNativeTransaction(transaction, out var activeTransaction))
//                 {
//                     q.Send(toSend, label, activeTransaction);
//                     return;
//                 }
//
//                 q.Send(toSend, label, GetTransactionTypeForSend());
//             }
//         }
//     }
// }