namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using NServiceBus.Extensibility;
    using NUnit.Framework;

    public class MsmqFailureInfoStorageTests
    {
        [Test]
        public void When_recording_failure_initially_should_store_one_failed_attempt_exception_and_context()
        {
            var messageId = Guid.NewGuid().ToString("D");
            var exception = new Exception();
            var context = new ContextBag();

            var storage = GetFailureInfoStorage();

            storage.RecordFailureInfoForMessage(messageId, exception, context);

            storage.TryGetFailureInfoForMessage(messageId, out var failureInfo);

            Assert.Multiple(() =>
            {
                Assert.That(failureInfo, Is.Not.Null);
                Assert.That(failureInfo.NumberOfProcessingAttempts, Is.EqualTo(1));
                Assert.That(failureInfo.Exception, Is.SameAs(exception));
                Assert.That(failureInfo.Context, Is.SameAs(context));
            });
        }

        [Test]
        public void When_recording_failure_many_times_should_store_number_of_attempts_and_last_exception()
        {
            var messageId = Guid.NewGuid().ToString("D");
            var secondException = new Exception();

            var storage = GetFailureInfoStorage();

            storage.RecordFailureInfoForMessage(messageId, new Exception(), new ContextBag());
            storage.RecordFailureInfoForMessage(messageId, secondException, new ContextBag());

            storage.TryGetFailureInfoForMessage(messageId, out var failureInfo);

            Assert.That(failureInfo, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(failureInfo.NumberOfProcessingAttempts, Is.EqualTo(2));
                Assert.That(failureInfo.Exception, Is.SameAs(secondException));
            });
        }

        [Test]
        public void When_clearing_failure_should_return_null_on_subsequent_retrieval()
        {
            var messageId = Guid.NewGuid().ToString("D");

            var storage = GetFailureInfoStorage();

            storage.RecordFailureInfoForMessage(messageId, new Exception(), new ContextBag());

            storage.TryGetFailureInfoForMessage(messageId, out var failureInfo);
            Assert.That(failureInfo, Is.Not.Null);

            storage.ClearFailureInfoForMessage(messageId);

            storage.TryGetFailureInfoForMessage(messageId, out failureInfo);
            Assert.That(failureInfo, Is.Null);
        }

        [Test]
        public void When_recording_more_than_max_number_of_failures_should_remove_least_recently_used_entry()
        {
            const int MaxElements = 50;
            var storage = new MsmqFailureInfoStorage(maxElements: MaxElements);

            var lruMessageId = Guid.NewGuid().ToString("D");

            storage.RecordFailureInfoForMessage(lruMessageId, new Exception(), new ContextBag());

            for (var i = 0; i < MaxElements; ++i)
            {
                var messageId = Guid.NewGuid().ToString("D");
                var exception = new Exception();

                storage.RecordFailureInfoForMessage(messageId, exception, new ContextBag());
            }

            storage.TryGetFailureInfoForMessage(lruMessageId, out var failureInfo);

            Assert.That(failureInfo, Is.Null);
        }

        [Test]
        public void When_recording_a_failure_for_a_message_it_should_not_be_treated_as_least_recently_used()
        {
            const int MaxElements = 50;
            var storage = new MsmqFailureInfoStorage(MaxElements);

            var lruMessageId = Guid.NewGuid().ToString("D");

            storage.RecordFailureInfoForMessage(lruMessageId, new Exception(), new ContextBag());

            var messageIds = new List<string>(MaxElements);
            for (var i = 0; i < MaxElements; ++i)
            {
                messageIds.Add(Guid.NewGuid().ToString("D"));
            }

            for (var i = 0; i < MaxElements - 1; ++i)
            {
                storage.RecordFailureInfoForMessage(messageIds[i], new Exception(), new ContextBag());
            }

            storage.RecordFailureInfoForMessage(lruMessageId, new Exception(), new ContextBag());

            storage.RecordFailureInfoForMessage(messageIds[MaxElements - 1], new Exception(), new ContextBag());

            storage.TryGetFailureInfoForMessage(lruMessageId, out var failureInfo);

            Assert.That(failureInfo, Is.Not.Null);
        }

        static MsmqFailureInfoStorage GetFailureInfoStorage()
        {
            return new MsmqFailureInfoStorage(10);
        }
    }
}