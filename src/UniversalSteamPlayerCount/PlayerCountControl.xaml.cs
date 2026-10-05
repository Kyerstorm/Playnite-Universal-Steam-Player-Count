using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public partial class PlayerCountControl : PluginUserControl, INotifyPropertyChanged
    {
        private readonly IPlayniteAPI api;
        private readonly PluginSettingsViewModel settings;
        private readonly Func<PlayerCountService> service;
        private readonly ILogger logger;
        private readonly DispatcherTimer timer;
        private readonly DesktopView viewAtCreation;
        private CancellationTokenSource pending;
        private GameInfo current;
        private int appId;
        private string playerCountText = string.Empty;
        private Visibility controlVisibility = Visibility.Collapsed;

        public PlayerCountControl(IPlayniteAPI api, PluginSettingsViewModel settings, Func<PlayerCountService> service, ILogger logger)
        {
            InitializeComponent();
            this.api = api;
            this.settings = settings;
            this.service = service;
            this.logger = logger;
            if (api.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                viewAtCreation = api.MainView.ActiveDesktopView;
            }

            OpenSteamDbCommand = new RelayCommand(() => SteamLinks.OpenGraphs(appId, logger));
            DataContext = this;

            timer = new DispatcherTimer { Interval = SelectionWatcher.Debounce };
            timer.Tick += OnTick;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public RelayCommand OpenSteamDbCommand { get; }

        public string PlayerCountText
        {
            get { return playerCountText; }
            set
            {
                playerCountText = value;
                RaisePropertyChanged(nameof(PlayerCountText));
            }
        }

        public Visibility ControlVisibility
        {
            get { return controlVisibility; }
            set
            {
                controlVisibility = value;
                RaisePropertyChanged(nameof(ControlVisibility));
            }
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            try
            {
                timer.Stop();
                CancelPending();

                // GameContextChanged also fires for controls in views that are not on screen.
                if (api.ApplicationInfo.Mode == ApplicationMode.Desktop && viewAtCreation != api.MainView.ActiveDesktopView)
                {
                    return;
                }

                Show(PlayerCountResult.None);
                current = newContext == null || !settings.Settings.EnableThemeControl ? null : GameInfoFactory.From(newContext);
                if (current != null)
                {
                    timer.Start();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "GameContextChanged failed");
            }
        }

        // Re-runs the request for the current game, for example after a manual match or a settings change.
        public void Refresh()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(Refresh));
                return;
            }

            GameContextChanged(GameContext, GameContext);
        }

        private async void OnTick(object sender, EventArgs e)
        {
            timer.Stop();
            var game = current;
            if (game == null)
            {
                return;
            }

            CancelPending();
            var mine = new CancellationTokenSource();
            pending = mine;
            try
            {
                var result = await service().GetAsync(game, mine.Token);
                if (mine.IsCancellationRequested || !ReferenceEquals(game, current))
                {
                    return;
                }

                Show(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not get the Steam player count");
            }
        }

        private void Show(PlayerCountResult result)
        {
            appId = result.HasCount ? result.AppId : 0;
            PlayerCountText = result.HasCount ? result.PlayerCount.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;
            ControlVisibility = result.HasCount ? Visibility.Visible : Visibility.Collapsed;
            settings.Settings.PlayerCountAvailable = result.HasCount;
        }

        private void CancelPending()
        {
            if (pending != null)
            {
                pending.Cancel();
                pending = null;
            }
        }

        private void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
