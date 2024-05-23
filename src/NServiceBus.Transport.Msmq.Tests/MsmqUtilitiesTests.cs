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
            var message =
                MsmqUtilities.Convert(
                    new OutgoingMessage("message id",
                        new Dictionary<string, string> { { "some-header", "some value" } }, Array.Empty<byte>()));

            var encodingWithBOM = new UTF8Encoding(true);
            var preamble = encodingWithBOM.GetPreamble();

            Assert.AreNotEqual(preamble, message.Extension.Take(preamble.Length));
        }

        [Test]
        public void Should_support_v1dot0_header_deserialization()
        {
            var headers = new Dictionary<string, string> { { "some-header", "some value" } };
            var message =
                MsmqUtilities.Convert(
                    new OutgoingMessage("message id", headers, Array.Empty<byte>()));

            var v1dot0Headers = DeserializeMessageHeadersV1dot0(message);

            CollectionAssert.AreEqual(v1dot0Headers.Values, headers.Values);
        }

        [Test]
        public void Verify_that_Version_1dot2_and_2_does_not_emit_BOM_in_headers()
        {
            var headerSerializer = new System.Xml.Serialization.XmlSerializer(typeof(List<HeaderInfo>));
            var headers = new Dictionary<string, string> { { "some-header", "some value" } };
            var wrappedHeaders = headers.Select(pair => new HeaderInfo
            {
                Key = pair.Key,
                Value = pair.Value
            }).ToList();

            // this is a copy of what v1 and v2 of the transport does
            // https://github.com/Particular/NServiceBus.Transport.Msmq/blob/release-2.0/src/NServiceBus.Transport.Msmq/MsmqUtilities.cs#L131
            // https://github.com/Particular/NServiceBus.Transport.Msmq/blob/release-1.2/src/NServiceBus.Transport.Msmq/MsmqUtilities.cs#L144
            using (var stream = new MemoryStream())
            {
                headerSerializer.Serialize(stream, wrappedHeaders);

                var seralizedHeaders = stream.ToArray();

                var encodingWithBOM = new UTF8Encoding(true);
                var preamble = encodingWithBOM.GetPreamble();

                Assert.AreNotEqual(preamble, seralizedHeaders.Take(preamble.Length));
            }
        }

        //this is a copy of v1.0 header deserialization
        static Dictionary<string, string> DeserializeMessageHeadersV1dot0(Message m)
        {
            var headerSerializer = new System.Xml.Serialization.XmlSerializer(typeof(List<HeaderInfo>));
            var result = new Dictionary<string, string>();

            if (m.Extension.Length == 0)
            {
                return result;
            }

            //This is to make us compatible with v3 messages that are affected by this bug:
            //http://stackoverflow.com/questions/3779690/xml-serialization-appending-the-0-backslash-0-or-null-character
            var extension = Encoding.UTF8.GetString(m.Extension).TrimEnd('\0');
            object o;
            using (var stream = new StringReader(extension))
            {
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    CheckCharacters = false
                }))
                {
                    o = headerSerializer.Deserialize(reader);
                }
            }

            foreach (var pair in (List<HeaderInfo>)o)
            {
                if (pair.Key != null)
                {
                    result.Add(pair.Key, pair.Value);
                }
            }

            return result;
        }
    }
}