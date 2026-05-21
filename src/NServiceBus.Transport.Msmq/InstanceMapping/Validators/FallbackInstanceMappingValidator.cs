namespace NServiceBus.Transport.Msmq;

using System;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

class FallbackInstanceMappingValidator(IInstanceMappingValidator preferredValidator,
    IInstanceMappingValidator fallbackValidator,
    string fallbackWarning,
    ILogger<FallbackInstanceMappingValidator> logger) : IInstanceMappingValidator
{
    public void Validate(XDocument document)
    {
        try
        {
            preferredValidator.Validate(document);
            logWarningOnFallback = true;
        }
        catch (Exception ex)
        {
            if (logWarningOnFallback)
            {
                logger.LogWarning(fallbackWarning, ex);
                logWarningOnFallback = false;
            }
            fallbackValidator.Validate(document);
        }
    }

    bool logWarningOnFallback = true;
}