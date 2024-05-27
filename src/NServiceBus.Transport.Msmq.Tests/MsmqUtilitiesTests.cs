namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Xml;
    using NUnit.Framework;
    using Particular.Msmq;
    using Support;

    [TestFixture]
    public class MsmqUtilitiesTests
    {
        [Test]
        public void Should_convert_a_message_back_even_if_special_characters_are_contained_in_the_headers()
        {
            string expected = $"Can u see this '{(char)0x19}' character.";

            Message message =
                MsmqUtilities.Convert(
                    new OutgoingMessage("message id",
                        new Dictionary<string, string> { { "NServiceBus.ExceptionInfo.Message", expected } }, Array.Empty<byte>()));
            Dictionary<string, string> headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual(expected, headers["NServiceBus.ExceptionInfo.Message"]);
        }

        [Test]
        public void Should_convert_message_headers_that_contain_nulls_at_the_end()
        {
            string expected = "Hello World";

            Console.Out.WriteLine(sizeof(char));
            Message message =
                MsmqUtilities.Convert(
                    new OutgoingMessage("message id",
                        new Dictionary<string, string> { { "NServiceBus.ExceptionInfo.Message", expected } }, Array.Empty<byte>()));
            byte[] bufferWithNulls = new byte[message.Extension.Length + (10 * sizeof(char))];

            Buffer.BlockCopy(message.Extension, 0, bufferWithNulls, 0, bufferWithNulls.Length - (10 * sizeof(char)));

            message.Extension = bufferWithNulls;

            Dictionary<string, string> headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual(expected, headers["NServiceBus.ExceptionInfo.Message"]);
        }

        [Test]
        public void Should_fetch_the_replyToAddress_from_responsequeue_for_backwards_compatibility()
        {
            Message message = MsmqUtilities.Convert(
                new OutgoingMessage("message id", [], Array.Empty<byte>()));

            message.ResponseQueue = new MessageQueue(new MsmqAddress("local", RuntimeEnvironment.MachineName).FullPath);
            Dictionary<string, string> headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual("local@" + RuntimeEnvironment.MachineName, headers[Headers.ReplyToAddress]);
        }

        [Test]
        public void Should_not_override_replyToAddress()
        {
            Message message = MsmqUtilities.Convert(
                new OutgoingMessage("message id",
                    new Dictionary<string, string> { { Headers.ReplyToAddress, "SomeAddress" } }, Array.Empty<byte>()));

            message.ResponseQueue = new MessageQueue(new MsmqAddress("local", RuntimeEnvironment.MachineName).FullPath);
            Dictionary<string, string> headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual("SomeAddress", headers[Headers.ReplyToAddress]);
        }

        [Test]
        public void Should_not_override_messageIntent()
        {
            Message message = MsmqUtilities.Convert(
                new OutgoingMessage("message id",
                    new Dictionary<string, string> { { Headers.MessageIntent, MessageIntent.Send.ToString() } },
                    Array.Empty<byte>()));

            message.AppSpecific = 3; //Send = 1, Publish = 2, Subscribe = 3, Unsubscribe = 4 and Reply = 5
            Dictionary<string, string> headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual("Send", headers[Headers.MessageIntent]);
        }

        [Test]
        public void Should_set_messageIntent_if_header_not_present()
        {
            Message message = MsmqUtilities.Convert(
                new OutgoingMessage("message id", [], Array.Empty<byte>()));

            message.AppSpecific = 3; //Send = 1, Publish = 2, Subscribe = 3, Unsubscribe = 4 and Reply = 5
            Dictionary<string, string> headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual("Subscribe", headers[Headers.MessageIntent]);
        }

        [Test]
        public void Should_deserialize_if_trailing_bogus_data()
        {
            string expected = "Hello World";

            Message message =
                MsmqUtilities.Convert(
                    new OutgoingMessage("message id",
                        new Dictionary<string, string> { { "NServiceBus.ExceptionInfo.Message", expected } }, Array.Empty<byte>()));

            var r = new Random();

            byte[] bufferWithNulls = new byte[message.Extension.Length + (10 * sizeof(char))];
            r.NextBytes(bufferWithNulls);

            Buffer.BlockCopy(message.Extension, 0, bufferWithNulls, 0, bufferWithNulls.Length - (10 * sizeof(char)));

            message.Extension = bufferWithNulls;

            Dictionary<string, string> headers = MsmqUtilities.ExtractHeaders(message);

            Assert.AreEqual(expected, headers["NServiceBus.ExceptionInfo.Message"]);
        }

        [Test]
        public void Should_not_emit_BOM_when_serializing_headers_for_backwards_compatibility()
        {
            // This test ensures that older versions of the transport can still parse the headers since that code can't handle a BOM 
            // v1.0.X - https://github.com/Particular/NServiceBus.Transport.Msmq/blob/1.0.2/src/NServiceBus.Transport.Msmq/MsmqUtilities.cs#L94
            // v6 with msmq bundled - https://github.com/Particular/NServiceBus/blob/6.5.10/src/NServiceBus.Core/Transports/Msmq/MsmqUtilities.cs#L98
            // v5 with msmq bundled - https://github.com/Particular/NServiceBus/blob/5.2.26/src/NServiceBus.Core/Transports/Msmq/MsmqUtilities.cs#L199
            // Note: As of v1.1 the transport can handle a BOM due to this change - https://github.com/Particular/NServiceBus.Transport.Msmq/pull/125

            var message =
                MsmqUtilities.Convert(
                    new OutgoingMessage("message id",
                        new Dictionary<string, string> { { "some-header", "some value" } }, Array.Empty<byte>()));

            var encodingWithBOM = new UTF8Encoding(true);
            var preamble = encodingWithBOM.GetPreamble();

            Assert.AreNotEqual(preamble, message.Extension.Take(preamble.Length));
        }

        [Test]
        public void Default_usage_of_xml_serializer_does_not_emit_bom()
        {
            // Older versions of the transport doesn't use XmlWriter settings and therefor no BOM is emitted. This test verified that behavior

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(string));

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, "test");

                var seralizedHeaders = stream.ToArray();

                var encodingWithBOM = new UTF8Encoding(true);
                var preamble = encodingWithBOM.GetPreamble();

                Assert.AreNotEqual(preamble, seralizedHeaders.Take(preamble.Length));
            }
        }

        [Test]
        public void Default_xmlwritersettings_emit_bom()
        {
            // This test docments that the default XmlWriterSettings emits a BOM by default

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(string));

            using (var stream = new MemoryStream())
            {
                using var writer = XmlWriter.Create(stream, new XmlWriterSettings());

                serializer.Serialize(writer, "test");

                var seralizedHeaders = stream.ToArray();

                var encodingWithBOM = new UTF8Encoding(true);
                var preamble = encodingWithBOM.GetPreamble();

                Assert.AreEqual(preamble, seralizedHeaders.Take(preamble.Length));
            }
        }
    }
}