namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Net;
    using NUnit.Framework;

    [TestFixture]
    public class MsmqAddressTests
    {

        [Test]
        public void If_both_addresses_are_specified_via_host_name_it_should_not_convert()
        {
            var address = new MsmqAddress("replyToAddress", "replyToMachine");
            var returnAddress = address.MakeCompatibleWith(new MsmqAddress("someQueue", "destinationmachine"), _ =>
            {
                throw new Exception("Should not call the lookup method");
            });
            Assert.That(returnAddress.Machine, Is.EqualTo("replyToMachine"));
        }

        [Test]
        public void If_both_addresses_are_specified_via_ip_it_should_not_convert()
        {
            var address = new MsmqAddress("replyToAddress", "202.171.13.141");
            var returnAddress = address.MakeCompatibleWith(new MsmqAddress("someQueue", "202.171.13.140"), _ =>
            {
                throw new Exception("Should not call the lookup method");
            });
            Assert.That(returnAddress.Machine, Is.EqualTo("202.171.13.141"));
        }

        [Test]
        public void If_reference_address_is_specified_via_ip_and_this_is_specified_via_host_name_it_should_convert_to_ip()
        {
            var address = new MsmqAddress("replyToAddress", "replyToMachine");
            var returnAddress = address.MakeCompatibleWith(new MsmqAddress("someQueue", "202.171.13.140"), _ => "10.10.10.10");
            Assert.That(returnAddress.Machine, Is.EqualTo("10.10.10.10"));
        }

        [Test]
        [TestCase("::1")]
        [TestCase(".")]
        public void If_machine_is_looplocal_is_specified_is_remote_should_be_false(string machine)
        {
            Assert.That(MsmqAddress.Parse("replyToAddress@" + machine).IsRemote(), Is.False);
        }

        [Test]
        public void If_local_machine_name_is_remote_should_be_false()
        {
            Assert.That(MsmqAddress.Parse("replyToAddress@" + Environment.MachineName).IsRemote(), Is.False);
        }

        [Test]
        public void If_machine_name_is_local_ip_is_remote_should_be_false()
        {
            var machinename = "localtestmachinename";
            try
            {
                Dns.GetHostAddresses(machinename);
            }
            catch
            {
                Assert.Ignore($"Add `127.0.0.1 {machinename}` to hosts file for this test to run.");
            }
            Assert.That(MsmqAddress.Parse("replyToAddress@" + machinename).IsRemote(), Is.False);
        }
    }
}
