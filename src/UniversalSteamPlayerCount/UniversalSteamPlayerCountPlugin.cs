using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public class UniversalSteamPlayerCountPlugin : GenericPlugin
    {
        public const string SourceName = "SteamPlayerCount";
        public const string PlayerCountControlName = "PlayerCountControl";

        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly Lazy<PluginServices> services;
        private SelectionWatcher watcher;
        private TopPanelItem topPanelItem;
        private TextBlock topPanelText;
        private PlayerCountResult lastResult = PlayerCountResult.None;

        public UniversalSteamPlayerCountPlugin(IPlayniteAPI api) : base(api)
        {
            SettingsViewModel = new PluginSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                SourceName = SourceName,
                ElementList = new List<string> { PlayerCountControlName },
            });

            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = SourceName,
                SettingsRoot = nameof(SettingsViewModel) + "." + nameof(PluginSettingsViewModel.Settings),
            });

            services = new Lazy<PluginServices>(() => new PluginServices(
                GetPluginUserDataPath(),
                () => SettingsViewModel.Settings.EnableNonSteamMatching,
                (e, message) => logger.Error(e, message)));
        }

        public override Guid Id { get; } = Guid.Parse("aa33d12d-49ed-42b6-86eb-d96efe1ccbb3");

        public PluginSettingsViewModel SettingsViewModel { get; }

        public PluginServices Services
        {
            get { return services.Value; }
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                watcher = new SelectionWatcher(
                    PlayniteApi.MainView.UIDispatcher,
                    () => Services.Counts,
                    Publish,
                    e => logger.Error(e, "Could not get the Steam player count"));

                // Read matches.json off the UI thread before the first selection needs it.
                Task.Run(() =>
                {
                    try
                    {
                        var warm = Services;
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Could not start Steam Player Count services");
                    }
                });
            }
            catch (Exception e)
            {
                logger.Error(e, "OnApplicationStarted failed");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            try
            {
                if (watcher != null)
                {
                    watcher.Stop();
                }

                if (services.IsValueCreated)
                {
                    services.Value.Dispose();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "OnApplicationStopped failed");
            }
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            try
            {
                if (watcher != null)
                {
                    watcher.OnSelectionChanged(args.NewValue);
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "OnGameSelected failed");
            }
        }

        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            if (topPanelItem == null)
            {
                topPanelText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                topPanelItem = new TopPanelItem
                {
                    Icon = topPanelText,
                    Visible = false,
                    Activated = () => SteamLinks.OpenGraphs(lastResult.AppId, logger),
                };
                Publish(lastResult);
            }

            yield return topPanelItem;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new PluginSettingsView();
        }

        // Called after a match was set, cleared or excluded by the user.
        public void OnMatchesChanged()
        {
            if (watcher != null)
            {
                watcher.Refresh();
            }
        }

        public void OnSettingsSaved()
        {
            if (watcher != null)
            {
                watcher.Refresh();
            }
        }

        private void Publish(PlayerCountResult result)
        {
            lastResult = result ?? PlayerCountResult.None;
            if (topPanelItem == null)
            {
                return;
            }

            var show = lastResult.HasCount && SettingsViewModel.Settings.ShowTopPanelItem;
            if (show)
            {
                var count = lastResult.PlayerCount.ToString("N0", CultureInfo.CurrentCulture);
                topPanelText.Text = count;
                topPanelItem.Title = string.Format(
                    PlayniteApi.Resources.GetString("LOCSteamPlayerCountTopPanelTitle"),
                    count,
                    lastResult.SteamName);
            }

            topPanelItem.Visible = show;
        }
    }
}
