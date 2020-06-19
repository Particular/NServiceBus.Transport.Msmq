namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.IO;
    using Features;
    using Routing;
    using Settings;

    class InstanceMappingFileFeature : Feature
    {
        public InstanceMappingFileFeature()
        {
            EnableByDefault();

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
            var instanceMappingLoader = CreateInstanceMappingLoader(context.Settings);

            var checkInterval = context.Settings.Get<TimeSpan>(CheckIntervalSettingsKey);
            var endpointInstances = context.Settings.Get<EndpointInstances>();

            var instanceMappingTable = new InstanceMappingFileMonitor(checkInterval, new AsyncTimer(), instanceMappingLoader, endpointInstances);
            instanceMappingTable.ReloadData();
            context.RegisterStartupTask(instanceMappingTable);
        }

        static string GetRootedPath(string filePath)
        {
            return Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
        }

        IInstanceMappingLoader CreateInstanceMappingLoader(ReadOnlySettings settings)
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

            return loader;
        }

        public const string CheckIntervalSettingsKey = "InstanceMappingFile.CheckInterval";
        public const string PathSettingsKey = "InstanceMappingFile.Path";
        const string DefaultInstanceMappingFileName = "instance-mapping.xml";
    }
}
