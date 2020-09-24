namespace NServiceBus.Transport.Msmq.Tests
{
    using Logging;
    using NUnit.Framework;
    using System;
    using System.IO;
    using System.Text;
    using System.Xml.Linq;
    using Testing;

    [TestFixture]
    class FallbackInstanceMappingValidatorTests
    {
        [OneTimeSetUp]
        public void FixtureSetup()
        {
            var loggerFactory = LogManager.Use<TestingLoggerFactory>();
            loggerFactory.Level(LogLevel.Info);
            logOutput = new StringBuilder();
            var stringWriter = new StringWriter(logOutput);
            loggerFactory.WriteTo(stringWriter);

            preferredValidator = new FakeInstanceMappingValidator();
            secondaryValidator = new FakeInstanceMappingValidator();
            doc = null;
        }

        [SetUp]
        public void Setup()
        {
            preferredValidator.Pass();
            secondaryValidator.Pass();
            fallbackValidator = new FallbackInstanceMappingValidator(
                preferredValidator,
                secondaryValidator,
                FallbackMessage);
            logOutput.Clear();
        }

        [Test]
        public void Passes_if_preferred_passes()
        {
            Assert.DoesNotThrow(() => fallbackValidator.Validate(doc));
        }

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
            Assert.That(logOutput.ToString(), Does.Contain(FallbackMessage));
        }

        [Test]
        public void Does_not_log_on_subsequent_fallbacks()
        {
            preferredValidator.Fail("Preferred validator failed");
            fallbackValidator.Validate(doc);
            logOutput.Clear();
            fallbackValidator.Validate(doc);
            Assert.That(logOutput.ToString(), Does.Not.Contain(FallbackMessage));
        }

        [Test]
        public void Does_log_on_first_fallback_after_preferred_passes()
        {
            // Fallback once
            preferredValidator.Fail("Preferred validator failed");
            fallbackValidator.Validate(doc);
            logOutput.Clear();

            // Succeed once
            preferredValidator.Pass();
            fallbackValidator.Validate(doc);
            Assert.That(logOutput.ToString(), Does.Not.Contain(FallbackMessage));

            // Fail again
            preferredValidator.Fail("Preferred validator failed again");
            fallbackValidator.Validate(doc);
            Assert.That(logOutput.ToString(), Does.Contain(FallbackMessage));
        }

        FakeInstanceMappingValidator preferredValidator;
        FakeInstanceMappingValidator secondaryValidator;
        IInstanceMappingValidator fallbackValidator;
        XDocument doc;
        StringBuilder logOutput;
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
