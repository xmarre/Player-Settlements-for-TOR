using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BannerlordPlayerSettlement.Behaviours;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace BannerlordPlayerSettlement.Patches
{
    /// <summary>
    /// Registers dynamically created fortifications in Bannerlord's strategic
    /// fortification-neighbour cache. Native siege target selection assigns a zero
    /// score to a fortification with no cached neighbours, which makes an otherwise
    /// valid player settlement invisible to besieger and army objective selection.
    /// </summary>
    [HarmonyPatch(typeof(PlayerSettlementBehaviour), nameof(PlayerSettlementBehaviour.RegisterEvents))]
    internal static class SiegeAiIntegrationPatch
    {
        private const string PlayerSettlementPrefix = "player_settlement_";

        [HarmonyPostfix]
        private static void RegisterRepairEvents(PlayerSettlementBehaviour __instance)
        {
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(__instance, RepairAllPlayerFortifications);
            PlayerSettlementBehaviour.SettlementCreatedEvent?.AddNonSerializedListener(__instance, RepairPlayerFortification);
        }

        private static void RepairAllPlayerFortifications()
        {
            foreach (Settlement settlement in Settlement.All)
            {
                if (IsPlayerFortification(settlement))
                    RepairPlayerFortification(settlement);
            }
        }

        private static void RepairPlayerFortification(Settlement settlement)
        {
            if (!IsPlayerFortification(settlement) || Campaign.Current == null)
                return;

            List<Settlement> fortifications = Settlement.All
                .Where(candidate => candidate != null && candidate.IsFortification && candidate.Town != null)
                .ToList();

            if (fortifications.Count < 2)
                return;

            int repairedCaches = 0;
            int resultingNeighbors = 0;

            foreach (object cache in EnumerateNavigationCaches(Campaign.Current.Models.MapDistanceModel))
            {
                try
                {
                    MethodInfo getNeighbors = FindMethod(cache.GetType(), "GetNeighbors", 1);
                    MethodInfo checkBeingNeighbor = FindMethod(cache.GetType(), "CheckBeingNeighbor", 3);
                    MethodInfo addNeighbor = FindMethod(cache.GetType(), "AddNeighbor", 2);
                    if (getNeighbors == null || checkBeingNeighbor == null || addNeighbor == null)
                        continue;

                    int existingCount = CountItems(getNeighbors.Invoke(cache, new object[] { settlement }));
                    if (existingCount > 0)
                    {
                        resultingNeighbors = Math.Max(resultingNeighbors, existingCount);
                        continue;
                    }

                    foreach (Settlement candidate in fortifications)
                    {
                        if (ReferenceEquals(candidate, settlement))
                            continue;

                        object result = checkBeingNeighbor.Invoke(
                            cache,
                            new object[] { fortifications, settlement, candidate });
                        if (result is bool isNeighbor && isNeighbor)
                            addNeighbor.Invoke(cache, new object[] { settlement, candidate });
                    }

                    int newCount = CountItems(getNeighbors.Invoke(cache, new object[] { settlement }));
                    if (newCount > 0)
                    {
                        repairedCaches++;
                        resultingNeighbors = Math.Max(resultingNeighbors, newCount);
                    }
                }
                catch (Exception exception)
                {
                    Debug.Print("[PlayerSettlement] Strategic neighbour repair failed for " +
                                settlement.StringId + ": " + Unwrap(exception));
                }
            }

            Debug.Print("[PlayerSettlement] Strategic neighbour integration for " +
                        settlement.StringId + ": repaired caches=" + repairedCaches +
                        ", neighbours=" + resultingNeighbors + ".");
        }

        private static IEnumerable<object> EnumerateNavigationCaches(MapDistanceModel model)
        {
            if (model == null)
                yield break;

            var yielded = new HashSet<object>();
            for (Type type = model.GetType(); type != null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(
                             BindingFlags.Instance | BindingFlags.Public |
                             BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    object value;
                    try
                    {
                        value = field.GetValue(model);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value == null)
                        continue;

                    if (IsNavigationCache(value) && yielded.Add(value))
                    {
                        yield return value;
                        continue;
                    }

                    if (!(value is IDictionary dictionary))
                        continue;

                    foreach (object entryValue in dictionary.Values)
                    {
                        if (entryValue != null && IsNavigationCache(entryValue) && yielded.Add(entryValue))
                            yield return entryValue;
                    }
                }
            }
        }

        private static bool IsNavigationCache(object value)
        {
            Type type = value.GetType();
            return FindMethod(type, "GetNeighbors", 1) != null &&
                   FindMethod(type, "CheckBeingNeighbor", 3) != null &&
                   FindMethod(type, "AddNeighbor", 2) != null;
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(candidate =>
                        candidate.Name == name && candidate.GetParameters().Length == parameterCount);
                if (method != null)
                    return method;
            }
            return null;
        }

        private static int CountItems(object value)
        {
            if (value is ICollection collection)
                return collection.Count;

            if (value is IEnumerable enumerable)
            {
                int count = 0;
                foreach (object ignored in enumerable)
                    count++;
                return count;
            }

            return 0;
        }

        private static bool IsPlayerFortification(Settlement settlement)
        {
            return settlement != null && settlement.IsFortification && settlement.Town != null &&
                   !string.IsNullOrEmpty(settlement.StringId) &&
                   settlement.StringId.StartsWith(PlayerSettlementPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }
    }
}
