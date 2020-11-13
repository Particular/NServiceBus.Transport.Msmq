using NServiceBus;
using NUnit.Framework;
using Particular.Approvals;
using PublicApiGenerator;

[TestFixture]
public class APIApprovals
{
    [Test]
    public void Approve()
    {
        var publicApi = typeof(MsmqTransport).Assembly.GeneratePublicApi();
        Approver.Verify(publicApi);
    }
}