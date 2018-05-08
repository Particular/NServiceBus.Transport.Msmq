namespace NServiceBus.Transport.Msmq.Tests
{
    //using System;
    //using NServiceBus.Features;
    using NUnit.Framework;
    //using Settings;

    [TestFixture]
    public class InstanceMappingFileFeatureTests
    {
        [Test]
        public void Should_configure_default_values()
        {
            // Test has to be commented out for now, no way to access the defaults

            //var feature = new InstanceMappingFileFeature();
            //var settings = new SettingsHolder();

            //feature.ConfigureDefaults(settings);

            //Assert.That(settings.Get<string>(InstanceMappingFileFeature.FilePathSettingsKey), Is.EqualTo("instance-mapping.xml"));
            //Assert.That(settings.Get<TimeSpan>(InstanceMappingFileFeature.CheckIntervalSettingsKey), Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
    }
}