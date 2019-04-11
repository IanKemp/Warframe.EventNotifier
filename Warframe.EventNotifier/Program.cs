using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarframeNET;

namespace Warframe.EventNotifier
{
    class Program
    {
        static async Task Main(string[] args)
        {
            void WriteHeading(string heading)
            {
                Console.WriteLine(heading);
                Console.WriteLine(new string('=', 79));
                Console.WriteLine();
            }

            void WriteCountedHeading(string heading, int shown, int total)
            {
                WriteHeading($"{heading} ({shown} of {total} shown)");
            }

            void WriteEvents<T, T2, T3>(IEnumerable<T> events, Func<T, bool> preFilter, Func<T, T2> transform, Func<T2, bool> postFilter, string heading, Func<T2, T3> orderBy, Func<T2, ConsoleColor> foregroundColour, Func<T2, string> writer)
            {
                var filteredEvents = events.Where(preFilter).Select(transform);

                if (postFilter != null)
                {
                    filteredEvents = filteredEvents.Where(postFilter);
                }

                WriteCountedHeading(heading, filteredEvents.Count(), events.Count());

                foreach (var filteredEvent in filteredEvents.OrderBy(orderBy))
                {
                    if (foregroundColour != null)
                    {
                        Console.ForegroundColor = foregroundColour(filteredEvent);
                    }

                    Console.WriteLine(writer(filteredEvent));

                    if (foregroundColour != null)
                    {
                        Console.ResetColor();
                    }
                }

                Console.WriteLine();
            }

            var token = new CancellationTokenSource();

            Console.CancelKeyPress += (s, e) =>
            {
                token.Cancel();
            };

            var client = new WarframeClient();
            WorldState worldState;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        worldState = await client.GetWorldStateAsync(Platform.PC).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is TaskCanceledException)
                    {
                        continue;
                    }

                    Console.Clear();

                    WriteHeading("CYCLES");

                    Console.WriteLine($"Earth: {(worldState.EarthCycle.IsDay ? "Night" : "Day")} in {worldState.EarthCycle.TimeRemaining}");
                    Console.WriteLine($"Plains: {(worldState.CetusCycle.IsDay ? "Night" : "Day")} in {worldState.CetusCycle.TimeRemaining}");
                    Console.WriteLine($"Vallis: {(worldState.VallisCycle.IsWarm ? "Cold" : "Warm")} in {worldState.VallisCycle.TimeRemaining}");
                    Console.WriteLine();

                    WriteEvents(worldState.Alerts, IsUsefulAlert, a => new TimedEvent<Alert>(a, worldState),
                        a => a.TimeToExpiry > TimeSpan.Zero, "ALERTS", a => a.TimeToExpiry, a => a.Event.Mission.IsNightmare || IsAnyItemCriticallyImportant(a.Event.Mission.Reward) ? ConsoleColor.Red : ConsoleColor.Gray,
                        FormatAlert);

                    WriteEvents(worldState.Invasions.Where(i => !i.IsCompleted), IsInterestingInvasion, i => new { Invasion = i, OrderedCompletion = Math.Min(i.Completion, 100 - i.Completion), },
                        null, "INVASIONS", i => i.OrderedCompletion, i => IsAnyItemCriticallyImportant(i.Invasion.AttackerReward) || IsAnyItemCriticallyImportant(i.Invasion.DefenderReward) ? ConsoleColor.Red : ConsoleColor.Gray,
                        i => FormatInvasion(i.Invasion));

                    WriteEvents(worldState.Fissures, IsFarmableFissure, f => new TimedEvent<Fissure>(f, worldState), null,
                        "FISSURES", f => f.TimeToExpiry, null, FormatFissure);

                    await Task.Delay(TimeSpan.FromMinutes(1), token.Token).ConfigureAwait(false);

                    /*
                    var key = Console.ReadKey(true);

                    if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C)
                    {
                        token.Cancel();
                    }
                    */
                }

                await Task.Yield();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            string durationDesc;

            if (duration.TotalMinutes <= 0)
            {
                durationDesc = (int)duration.TotalSeconds + "s";
            }
            else if (duration.TotalHours >= 1)
            {
                durationDesc = (int)duration.TotalHours + "h " + ((int)duration.TotalMinutes - ((int)duration.TotalHours * 60)) + "m";
            }
            else
            {
                durationDesc = (int)duration.TotalMinutes + "m";
            }

            return durationDesc;
        }

        private static string FormatReward(Reward reward, bool includeCredits = true)
        {
            var rewardBuilder = new StringBuilder();

            if (reward.Items.Any())
            {
                rewardBuilder
                    .Append(string.Join(" + ", reward.Items))
                    .Append(" ");
            }

            rewardBuilder
                .Append(string.Join(" + ", reward.CountedItems.Select(countedItem => countedItem.Type + (countedItem.Count > 1 ? " x" + countedItem.Count : string.Empty))));

            if (reward.Credits > 0 && includeCredits)
            {
                rewardBuilder
                    .Append(" + ")
                    .Append(reward.Credits);
            }

            return rewardBuilder.ToString().Trim();
        }

        /*
        private static string FormatCountedItems(IEnumerable<CountedItem> countedItems)
        {
            return string.Join(" + ", countedItems.Select(countedItem => countedItem.Type + (countedItem.Count > 1 ? " x" + countedItem.Count : string.Empty)));
        }
        */

        private static bool HasAnyItems(Reward reward)
        {
            return reward.Items.Any() || reward.CountedItems.Any();
        }

        private static bool IsUsefulAlert(Alert alert)
        {
            return HasAnyItems(alert.Mission.Reward)
                && !alert.Mission.Reward.Items.Any(i => i.EndsWith("Endo", StringComparison.CurrentCultureIgnoreCase)
                    || i.EndsWith("Ferrite", StringComparison.CurrentCultureIgnoreCase)
                    || i.EndsWith("Rubedo", StringComparison.CurrentCultureIgnoreCase));
        }

        private static bool ContainsAny(IEnumerable<string> items, IEnumerable<string> values, StringComparison comparisonType = StringComparison.CurrentCulture)
        {
            foreach (var item in items)
            {
                foreach (var value in values)
                {
                    if (item.Contains(value, comparisonType))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsAnyItemCriticallyImportant(Reward reward)
        {
            var criticallyImportantItems = new[] { "Exilus Adapter", "Forma", "Orokin Catalyst", "Orokin Reactor", "Riven", };

            return ContainsAny(reward.Items, criticallyImportantItems, StringComparison.CurrentCultureIgnoreCase)
                || ContainsAny(reward.CountedItems.Select(ci => ci.Type), criticallyImportantItems, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool IsCriticallyImportantAlert(Alert alert)
        {
            return HasAnyItems(alert.Mission.Reward) && alert.Mission.Reward.Items.Any(i => i.Contains("Exilus Adapter") || i.Contains("Orokin Catalyst") || i.Contains("Orokin Reactor") || i.Contains("Riven"));
        }

        private static string FormatAlert(TimedEvent<Alert> alertEvent)
        {
            var alert = alertEvent.Event;

            string GetNightmareSuffix()
            {
                return /*alert.Mission.IsNightmare ? " [_!N!_]" : */string.Empty;
            }

            int NormalizeEnemyLevel(int level)
            {
                return level + (alert.Mission.IsNightmare ? 10 : 0);
            }

            return $"> {FormatDuration(alertEvent.TimeToExpiry)} | {FormatReward(alert.Mission.Reward, false)}{GetNightmareSuffix()} | {alert.Mission.Type} " +
                $"lvl {NormalizeEnemyLevel(alert.Mission.EnemyMinLevel)} - {NormalizeEnemyLevel(alert.Mission.EnemyMaxLevel)} ({alert.Mission.Faction}) | {alert.Mission.Node}";
        }

        private static bool IsInterestingInvasion(Invasion invasion)
        {
            bool IsCrappyReward(Reward reward)
            {
                return reward.CountedItems.Any(i =>
                       i.Type.Contains("Detonite Injector", StringComparison.CurrentCultureIgnoreCase)
                    || i.Type.Contains("Fieldron", StringComparison.CurrentCultureIgnoreCase)
                    || i.Type.Contains("Mutagen Mass", StringComparison.CurrentCultureIgnoreCase)
                    || i.Type.Contains("Mutalist Alad V Nav Coordinate", StringComparison.CurrentCultureIgnoreCase)
                );
            }

            // phorid assassinations are fastest
            return (invasion.IsVsInfestation ? invasion.Description.Contains("Phorid", StringComparison.CurrentCultureIgnoreCase) : true)
                && !IsCrappyReward(invasion.AttackerReward)
                && !IsCrappyReward(invasion.DefenderReward);
        }

        private static string FormatInvasion(Invasion invasion)
        {
            string FormatFaction(bool isAttacker)
            {
                var faction = isAttacker ? invasion.AttackingFaction : invasion.DefendingFaction;
                var reward = isAttacker ? invasion.AttackerReward : invasion.DefenderReward;

                return $"{FormatReward(reward)} ({faction})"; // FormatCountedItems(reward.CountedItems); // (reward.CountedItems.Count > 0 ? ((reward.CountedItems.Count > 1 ? $" x{reward.CountedItems.Count}" : string.Empty) + $" ({reward.CountedItems[0].Type})") : string.Empty);
            }

            string attackerReward;

            // infested are always the attacker and can't be sided with, hence no rewards
            if (invasion.IsVsInfestation)
            {
                attackerReward = string.Empty;
            }
            else
            {
                attackerReward = FormatFaction(true) + " vs ";
            }

            return $"> {Math.Round(invasion.Completion, 0),3}% | {attackerReward}{FormatFaction(false)} | {invasion.Node}";
        }

        private static bool IsFarmableFissure(Fissure fissure)
        {
            // removing defense for now since DE are a bunch of drunken monkeys
            return /*fissure.MissionType == "Defense"
                ||*/ fissure.MissionType == "Excavation"
                || fissure.MissionType == "Interception"
                || fissure.MissionType == "Survival";
        }

        private static string FormatFissure(TimedEvent<Fissure> fissureEvent)
        {
            var fissure = fissureEvent.Event;

            return $"> {FormatDuration(fissureEvent.TimeToExpiry)} | {fissure.Tier} {fissure.MissionType} ({fissure.Enemy}) | {fissure.Node}";
        }
    }
}
