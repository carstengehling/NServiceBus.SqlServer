﻿namespace NServiceBus.Transport.SQLServer
{
    using Features;
    using Logging;
    using Settings;

    static class DelayedDeliveryInfrastructure
    {
        public static StartupCheckResult CheckForInvalidSettings(SettingsHolder settings)
        {
            var delayedDeliverySettings = settings.GetOrDefault<DelayedDeliverySettings>();
            if (delayedDeliverySettings != null)
            {
                var sendOnlyEndpoint = settings.GetOrDefault<bool>("Endpoint.SendOnly");
                if (sendOnlyEndpoint)
                {
                    return StartupCheckResult.Failed("Native delayed delivery is only supported for endpoints capable of receiving messages.");
                }
            }
            else
            {
                var timeoutManagerEnabled = settings.IsFeatureActive(typeof(TimeoutManager));
                if (timeoutManagerEnabled)
                {
                    Logger.Warn("Current configuration of the endpoint uses the TimeoutManager feature for delayed delivery - an option which is not recommended for new deployments. SqlTransport native delayed delivery should be used instead. It can be enabled by calling `UseNativeDelayedDelivery()`.");
                }
            }

            return StartupCheckResult.Success;
        }

        static ILog Logger = LogManager.GetLogger("DelayedDeliveryInfrastructure");
    }
}