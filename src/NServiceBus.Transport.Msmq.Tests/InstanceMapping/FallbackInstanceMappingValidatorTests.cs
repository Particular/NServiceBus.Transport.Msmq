namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Xml.Linq;
    using Microsoft.Extensions.Logging.Testing;
    using NUnit.Framework;

    [TestFixture]
    class FallbackInstanceMappingValidatorTests
    {
        [OneTimeSetUp]
        public void FixtureSetup()
        {
            preferredValidator = new FakeInstanceMappingValidator();
            secondaryValidator = new FakeInstanceMappingValidator();
            doc = null;
        }

        [SetUp]
        public void Setup()
        {
            preferredValidator.Pass();
            secondaryValidator.Pass();
            logger = new FakeLogger<FallbackInstanceMappingValidator>();
            fallbackValidator = new FallbackInstanceMappingValidator(
                preferredValidator,
                secondaryValidator,
                FallbackMessage,
                logger);
        }

        [Test]
        public void Passes_if_preferred_passes() => Assert.DoesNotThrow(() => fallbackValidator.Validate(doc));

        [Test]
        public void Passes_if_primary_fails_but_secondary_passes()
        {
            preferredValidator.Fail("Preferred validator failed");
            Assert.DoesNotThrow(() => fallbackValidator.Validate(doc));
        }

        [Test]
        public void Fails_if_both_fail()
        {
            preferredValidator.Fail("Preferred validator failed");
            secondaryValidator.Fail("Secondary validator failed");
            var exception = Assert.Throws<Exception>(() => fallbackValidator.Validate(doc));
            Assert.That(exception.Message, Does.Contain("Secondary validator failed"));
        }

        [Test]
        public void Logs_on_first_fallback()
        {
            preferredValidator.Fail("Preferred validator failed");
            fallbackValidator.Validate(doc);
            Assert.That(logger.LatestRecord.Message, Does.Contain(FallbackMessage));
        }

        [Test]
        public void Does_not_log_on_subsequent_fallbacks()
        {
            preferredValidator.Fail("Preferred validator failed");
            fallbackValidator.Validate(doc);
            logger.Collector.Clear();
            fallbackValidator.Validate(doc);
            Assert.That(logger.Collector.Count, Is.Zero);
        }

        [Test]
        public void Does_log_on_first_fallback_after_preferred_passes()
        {
            // Fallback once
            preferredValidator.Fail("Preferred validator failed");
            fallbackValidator.Validate(doc);
            logger.Collector.Clear();

            // Succeed once
            preferredValidator.Pass();
            fallbackValidator.Validate(doc);
            Assert.That(logger.Collector.Count, Is.Zero);

            // Fail again
            preferredValidator.Fail("Preferred validator failed again");
            fallbackValidator.Validate(doc);
            Assert.That(logger.LatestRecord.Message, Does.Contain(FallbackMessage));
        }

        FakeInstanceMappingValidator preferredValidator;
        FakeInstanceMappingValidator secondaryValidator;
        IInstanceMappingValidator fallbackValidator;
        FakeLogger<FallbackInstanceMappingValidator> logger;
        XDocument doc;
        const string FallbackMessage = "Falling Back";

        class FakeInstanceMappingValidator : IInstanceMappingValidator
        {
            public void Pass() => failureMessage = null;

            public void Fail(string message) => failureMessage = message;

            public void Validate(XDocument document)
            {
                if (failureMessage != null)
                {
                    throw new Exception(failureMessage);
                }
            }

            string failureMessage;
        }
    }
}
