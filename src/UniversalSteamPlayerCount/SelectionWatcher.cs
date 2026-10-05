using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using Playnite.SDK.Models;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    // Turns selection changes into one debounced player-count request. UI thread only.
    public sealed class SelectionWatcher
    {
        public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(700);

        private readonly Dispatcher dispatcher;
        private readonly Func<PlayerCountService> service;
        private readonly Action<PlayerCountResult> publish;
        private readonly Action<Exception> logError;
        private readonly DispatcherTimer timer;
        private CancellationTokenSource pending;
        private GameInfo current;

        public SelectionWatcher(
            Dispatcher dispatcher,
            Func<PlayerCountService> service,
            Action<PlayerCountResult> publish,
            Action<Exception> logError)
        {
            this.dispatcher = dispatcher;
            this.service = service;
            this.publish = publish;
            this.logError = logError;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Debounce };
            timer.Tick += OnTick;
        }

        public void OnSelectionChanged(IList<Game> games)
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => OnSelectionChanged(games)));
                return;
            }

            current = games != null && games.Count == 1 && games[0] != null ? GameInfoFactory.From(games[0]) : null;
            Restart();
        }

        // Re-runs the request for the current selection, for example after a manual match.
        public void Refresh()
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(Refresh));
                return;
            }

            Restart();
        }

        public void Stop()
        {
            timer.Stop();
            CancelPending();
        }

        private void Restart()
        {
            timer.Stop();
            CancelPending();
            publish(PlayerCountResult.None);
            if (current != null)
            {
                timer.Start();
            }
        }

        private void CancelPending()
        {
            if (pending != null)
            {
                pending.Cancel();
                pending = null;
            }
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
                // The selection may have moved on while the request was running.
                if (mine.IsCancellationRequested || !ReferenceEquals(game, current))
                {
                    return;
                }

                publish(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logError(ex);
            }
        }
    }
}
