namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using DeliveryConstraints;
    using NUnit.Framework;
    using Performance.TimeToBeReceived;
    using Support;
    using Transport;

    [TestFixture]
    public class MsmqUtilitiesTests
    {
        [Test]
        public void Should_convert_a_message_back_even_if_special_characters_are_contained_in_the_headers()
        {
            var expected = $"Can u see this '{(char) 0x19}' character.";

            var message = MsmqUtilities.Convert(new OutgoingMessage("message id", new Dictionary<string, string>
            {
                {"NServiceBus.ExceptionInfo.Message", expected}
            }, new byte[0]), new DispatchProperties());
            var headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual(expected, headers["NServiceBus.ExceptionInfo.Message"]);
        }

        [Test]
        public void Should_convert_message_headers_that_contain_nulls_at_the_end()
        {
            var expected = "Hello World";

            Console.Out.WriteLine(sizeof(char));
            var message = MsmqUtilities.Convert(new OutgoingMessage("message id", new Dictionary<string, string>
            {
                {"NServiceBus.ExceptionInfo.Message", expected}
            }, new byte[0]), new DispatchProperties());
            var bufferWithNulls = new byte[message.Extension.Length + (10*sizeof(char))];

            Buffer.BlockCopy(message.Extension, 0, bufferWithNulls, 0, bufferWithNulls.Length - (10*sizeof(char)));

            message.Extension = bufferWithNulls;

            var headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual(expected, headers["NServiceBus.ExceptionInfo.Message"]);
        }

        [Test]
        public void Should_fetch_the_replyToAddress_from_responsequeue_for_backwards_compatibility()
        {
            var message = MsmqUtilities.Convert(
                new OutgoingMessage("message id", new Dictionary<string, string>(), new byte[0]),
                new DispatchProperties());

            message.ResponseQueue = new MessageQueue(new MsmqAddress("local", RuntimeEnvironment.MachineName).FullPath);
            var headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual("local@" + RuntimeEnvironment.MachineName, headers[Headers.ReplyToAddress]);
        }

        [Test]
        public void Should_not_override_replyToAddress()
        {
            var message = MsmqUtilities.Convert(
                new OutgoingMessage("message id", new Dictionary<string, string>
                    {
                        {Headers.ReplyToAddress, "SomeAddress"}
                    }, new byte[0]),
                new DispatchProperties());

            message.ResponseQueue = new MessageQueue(new MsmqAddress("local", RuntimeEnvironment.MachineName).FullPath);
            var headers = MsmqUtilities.ExtractHeaders(message);
            
            Assert.AreEqual("SomeAddress", headers[Headers.ReplyToAddress]);
        }

        [Test]
        public void Should_not_override_messageIntent()
        {
            var message = MsmqUtilities.Convert(
                new OutgoingMessage("message id", new Dictionary<string, string>
                {
                    {Headers.MessageIntent, MessageIntentEnum.Send.ToString()}
                }, new byte[0]),
                new DispatchProperties());

            message.AppSpecific = 3; //Send = 1, Publish = 2, Subscribe = 3, Unsubscribe = 4 and Reply = 5 
            var headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual("Send", headers[Headers.MessageIntent]);
        }

        [Test]
        public void Should_set_messageIntent_if_header_not_present()
        {
            var message = MsmqUtilities.Convert(
                new OutgoingMessage("message id", new Dictionary<string, string>
                (), new byte[0]),
                new DispatchProperties());

            message.AppSpecific = 3; //Send = 1, Publish = 2, Subscribe = 3, Unsubscribe = 4 and Reply = 5 
            var headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual("Subscribe", headers[Headers.MessageIntent]);
        }

        [Test]
        public void Should_use_the_TTBR_in_the_send_options_if_set()
        {
            var properties = new DispatchProperties
            {
                DiscardIfNotReceivedBefore = new DiscardIfNotReceivedBefore(TimeSpan.FromDays(1))
            };


            var message = MsmqUtilities.Convert(new OutgoingMessage("message id", new Dictionary<string, string>(), new byte[0]), properties);

            Assert.AreEqual(TimeSpan.FromDays(1), message.TimeToBeReceived);
        }

        [Test]
        public void Should_use_the_non_durable_setting()
        {
            Assert.Fail("TODO implement non-durable delivery");
            // var nonDurableDeliveryConstraint = new List<DeliveryConstraint>
            // {
            //     new NonDurableDelivery()
            // };
            var nonDurableDeliveryConstraint = new DispatchProperties();

            Assert.False(MsmqUtilities.Convert(new OutgoingMessage("message id", new Dictionary<string, string>(), new byte[0]), nonDurableDeliveryConstraint).Recoverable);
            Assert.True(MsmqUtilities.Convert(new OutgoingMessage("message id", new Dictionary<string, string>(), new byte[0]), new DispatchProperties()).Recoverable);
        }

        [Test]
        public void Should_deserialize_if_trailing_bogus_data()
        {
            var expected = "Hello World";

            var message = MsmqUtilities.Convert(new OutgoingMessage("message id", new Dictionary<string, string>
            {
                {"NServiceBus.ExceptionInfo.Message", expected}
            }, new byte[0]), new DispatchProperties());

            var r = new Random();

            var bufferWithNulls = new byte[message.Extension.Length + (10*sizeof(char))];
            r.NextBytes(bufferWithNulls);

            Buffer.BlockCopy(message.Extension, 0, bufferWithNulls, 0, bufferWithNulls.Length - (10*sizeof(char)));

            message.Extension = bufferWithNulls;

            var headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual(expected, headers["NServiceBus.ExceptionInfo.Message"]);
        }
    }
}
