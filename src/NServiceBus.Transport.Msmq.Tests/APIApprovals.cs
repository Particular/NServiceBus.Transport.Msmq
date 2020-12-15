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
        var publicApi = typeof(MsmqTransport).Assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            ExcludeAttributes = new[] { "System.Reflection.AssemblyMetadataAttribute" }
        });
        Approver.Verify(publicApi);
    }
}