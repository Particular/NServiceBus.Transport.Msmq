namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.IO;
    using Features;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Routing;
    using Settings;

    sealed class InstanceMappingFileFeature : Feature
    {
        public InstanceMappingFileFeature()
        {
            var defaultPath = GetRootedPath(DefaultInstanceMappingFileName);
            Uri.TryCreate(defaultPath, UriKind.Absolute, out var defaultUri);

            Defaults(s =>
            {
                s.SetDefault(CheckIntervalSettingsKey, TimeSpan.FromSeconds(30));
                s.SetDefault(PathSettingsKey, defaultUri);
            });

            Prerequisite(c => c.Settings.HasExplicitValue(PathSettingsKey) || File.Exists(defaultPath), "No explicit instance mapping file configuration and default file does not exist.");
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            var checkInterval = context.Settings.Get<TimeSpan>(CheckIntervalSettingsKey);
            var endpointInstances = context.Settings.Get<EndpointInstances>();

            context.RegisterStartupTask(sp =>
            {
                var instanceMappingLoader = CreateInstanceMappingLoader(context.Settings, sp);
                var instanceMappingFileMonitor = new InstanceMappingFileMonitor(checkInterval, new AsyncTimer(), instanceMappingLoader, endpointInstances, sp.GetRequiredService<ILogger<InstanceMappingFileMonitor>>());
                instanceMappingFileMonitor.ReloadData();

                return instanceMappingFileMonitor;
            });
        }

        static string GetRootedPath(string filePath) =>
            Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);

        static IInstanceMappingLoader CreateInstanceMappingLoader(IReadOnlySettings settings, IServiceProvider serviceProvider)
        {
            var uri = settings.Get<Uri>(PathSettingsKey);

            IInstanceMappingLoader loader;

            if (!uri.IsAbsoluteUri || uri.IsFile)
            {
                var filePath = uri.IsAbsoluteUri
                    ? uri.LocalPath
                    : GetRootedPath(uri.OriginalString);

                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException("The specified instance mapping file does not exist.", filePath);
                }

                loader = new InstanceMappingFileLoader(filePath);
            }
            else
            {
                loader = new InstanceMappingUriLoader(uri);
            }

            IInstanceMappingValidator validator;

            if (settings.GetOrDefault<bool>(StrictSchemaValidationKey))
            {
                validator = EmbeddedSchemaInstanceMappingValidator.CreateValidatorV2();
            }
            else
            {
                validator = new FallbackInstanceMappingValidator(
                    EmbeddedSchemaInstanceMappingValidator.CreateValidatorV2(),
                    EmbeddedSchemaInstanceMappingValidator.CreateValidatorV1(),
                    "Validation error parsing instance mapping. Falling back on relaxed parsing method. Instance mapping may contain unsupported attributes.",
                    serviceProvider.GetRequiredService<ILogger<FallbackInstanceMappingValidator>>()
                );
            }

            return new ValidatingInstanceMappingLoader(loader, validator);
        }

        public const string CheckIntervalSettingsKey = "InstanceMappingFile.CheckInterval";
        public const string PathSettingsKey = "InstanceMappingFile.Path";
        public const string StrictSchemaValidationKey = "InstanceMappingFile.StrictSchemaValidation";
        const string DefaultInstanceMappingFileName = "instance-mapping.xml";
    }
}
