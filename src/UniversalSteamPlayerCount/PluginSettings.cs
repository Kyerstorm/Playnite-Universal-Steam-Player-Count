using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace UniversalSteamPlayerCount
{
    // ObservableObject lives in System.Collections.Generic in Playnite.SDK 6.17.
    public class PluginSettings : ObservableObject
    {
        private bool enableNonSteamMatching = true;
        private bool showTopPanelItem = true;
        private bool enableThemeControl = true;
        private bool playerCountAvailable;

        public bool EnableNonSteamMatching
        {
            get { return enableNonSteamMatching; }
            set
            {
                enableNonSteamMatching = value;
                OnPropertyChanged(nameof(EnableNonSteamMatching));
            }
        }

        public bool ShowTopPanelItem
        {
            get { return showTopPanelItem; }
            set
            {
                showTopPanelItem = value;
                OnPropertyChanged(nameof(ShowTopPanelItem));
            }
        }

        public bool EnableThemeControl
        {
            get { return enableThemeControl; }
            set
            {
                enableThemeControl = value;
                OnPropertyChanged(nameof(EnableThemeControl));
            }
        }

        // Runtime state for theme bindings; never saved.
        [DontSerialize]
        public bool PlayerCountAvailable
        {
            get { return playerCountAvailable; }
            set
            {
                playerCountAvailable = value;
                OnPropertyChanged(nameof(PlayerCountAvailable));
            }
        }
    }

    public class PluginSettingsViewModel : ObservableObject, ISettings
    {
        private readonly UniversalSteamPlayerCountPlugin plugin;
        private PluginSettings editingClone;
        private PluginSettings settings;

        public PluginSettingsViewModel(UniversalSteamPlayerCountPlugin plugin)
        {
            this.plugin = plugin;
            // LoadPluginSettings returns null on first run.
            Settings = plugin.LoadPluginSettings<PluginSettings>() ?? new PluginSettings();
        }

        public PluginSettings Settings
        {
            get { return settings; }
            set
            {
                settings = value;
                OnPropertyChanged(nameof(Settings));
            }
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.OnSettingsSaved();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}
