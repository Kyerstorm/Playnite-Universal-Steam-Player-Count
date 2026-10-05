using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public sealed class MatchActions
    {
        public const string BulkNotificationId = "SteamPlayerCount-bulk-match";

        private readonly IPlayniteAPI api;
        private readonly Func<PluginServices> services;
        private readonly Action afterChange;
        private readonly ILogger logger;

        public MatchActions(IPlayniteAPI api, Func<PluginServices> services, Action afterChange, ILogger logger)
        {
            this.api = api;
            this.services = services;
            this.afterChange = afterChange;
            this.logger = logger;

            var section = Loc("LOCSteamPlayerCountName");
            GameMenuItems = new List<GameMenuItem>
            {
                new GameMenuItem
                {
                    MenuSection = section,
                    Description = Loc("LOCSteamPlayerCountMenuMatch"),
                    Action = a => Guarded(() => MatchManually(a.Games)),
                },
                new GameMenuItem
                {
                    MenuSection = section,
                    Description = Loc("LOCSteamPlayerCountMenuExclude"),
                    Action = a => Guarded(() => Exclude(a.Games)),
                },
                new GameMenuItem
                {
                    MenuSection = section,
                    Description = Loc("LOCSteamPlayerCountMenuReset"),
                    Action = a => Guarded(() => Reset(a.Games)),
                },
            };

            MainMenuItems = new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    MenuSection = "@" + section,
                    Description = Loc("LOCSteamPlayerCountMenuBulk"),
                    Action = a => Guarded(BulkMatch),
                },
            };
        }

        public List<GameMenuItem> GameMenuItems { get; }

        public List<MainMenuItem> MainMenuItems { get; }

        private string Loc(string key)
        {
            return api.Resources.GetString(key);
        }

        private void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                logger.Error(e, "Steam Player Count menu action failed");
            }
        }

        private void MatchManually(List<Game> games)
        {
            if (games == null || games.Count == 0 || games[0] == null)
            {
                return;
            }

            // One game at a time: each needs its own choice.
            var game = games[0];
            var chosen = api.Dialogs.ChooseItemWithSearch(
                new List<GenericItemOption>(),
                SearchOptions,
                game.Name,
                string.Format(Loc("LOCSteamPlayerCountMatchDialogCaption"), game.Name));

            int appId;
            if (chosen == null ||
                !int.TryParse(chosen.Description, NumberStyles.None, CultureInfo.InvariantCulture, out appId))
            {
                return;
            }

            var s = services();
            s.Store.Set(game.Id, new MatchEntry
            {
                AppId = appId,
                SteamName = chosen.Name,
                Source = MatchSource.Manual,
                CheckedUtc = s.Clock.UtcNow,
            });
            afterChange();
        }

        // Called by the dialog, possibly on the UI thread. Task.Run plus ConfigureAwait(false)
        // throughout the core keeps this blocking wait from deadlocking.
        private List<GenericItemOption> SearchOptions(string term)
        {
            var options = new List<GenericItemOption>();
            try
            {
                if (string.IsNullOrWhiteSpace(term))
                {
                    return options;
                }

                var search = services().Search;
                var outcome = Task.Run(() => search.SearchAsync(term, CancellationToken.None)).GetAwaiter().GetResult();
                foreach (var candidate in outcome.Candidates)
                {
                    if (string.Equals(candidate.Type, StrictNameMatcher.AppType, StringComparison.OrdinalIgnoreCase))
                    {
                        options.Add(new GenericItemOption(candidate.Name, candidate.AppId.ToString(CultureInfo.InvariantCulture)));
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Steam search failed for " + term);
            }

            return options;
        }

        private void Exclude(List<Game> games)
        {
            if (games == null)
            {
                return;
            }

            var s = services();
            foreach (var game in games.Where(g => g != null))
            {
                s.Store.Set(game.Id, new MatchEntry
                {
                    AppId = null,
                    SteamName = null,
                    Source = MatchSource.ManualExcluded,
                    CheckedUtc = s.Clock.UtcNow,
                });
            }

            afterChange();
        }

        private void Reset(List<Game> games)
        {
            if (games == null)
            {
                return;
            }

            var s = services();
            foreach (var game in games.Where(g => g != null))
            {
                s.Store.Remove(game.Id);
            }

            afterChange();
        }

        private void BulkMatch()
        {
            var total = 0;
            var matched = 0;
            var notFound = 0;
            var failed = 0;
            var rateLimited = false;

            var options = new GlobalProgressOptions(Loc("LOCSteamPlayerCountBulkProgress"), true) { IsIndeterminate = false };
            api.Dialogs.ActivateGlobalProgress(async progress =>
            {
                var s = services();
                var games = api.Database.Games.Select(GameInfoFactory.From).Where(s.Resolver.NeedsSearch).ToList();
                total = games.Count;
                progress.ProgressMaxValue = games.Count;

                foreach (var game in games)
                {
                    if (progress.CancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    progress.Text = game.Name;
                    try
                    {
                        var result = await s.Resolver.ResolveAsync(game, progress.CancelToken).ConfigureAwait(false);
                        if (result.Status == ResolveStatus.RateLimited)
                        {
                            rateLimited = true;
                            break;
                        }

                        if (result.Status == ResolveStatus.Resolved)
                        {
                            matched++;
                        }
                        else if (result.Status == ResolveStatus.NoMatch)
                        {
                            notFound++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        logger.Error(e, "Steam matching failed for " + game.Name);
                    }

                    progress.CurrentProgressValue = matched + notFound + failed;
                }
            }, options);

            string text;
            if (total == 0)
            {
                text = Loc("LOCSteamPlayerCountBulkNothing");
            }
            else
            {
                var format = Loc(rateLimited ? "LOCSteamPlayerCountBulkRateLimited" : "LOCSteamPlayerCountBulkResult");
                text = string.Format(format, matched, notFound, failed);
            }

            api.Notifications.Add(new NotificationMessage(BulkNotificationId, text, NotificationType.Info));
            afterChange();
        }
    }
}
