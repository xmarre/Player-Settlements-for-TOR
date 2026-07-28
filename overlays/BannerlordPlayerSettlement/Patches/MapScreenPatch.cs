using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

using BannerlordPlayerSettlement.Behaviours;
using BannerlordPlayerSettlement.Extensions;
using BannerlordPlayerSettlement.UI.Viewmodels;

using HarmonyLib;

using SandBox.View.Map;
using SandBox.View.Map.Visuals;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerlordPlayerSettlement.Patches
{

    [HarmonyPatch(typeof(MapScreen))]
    public static class MapScreenPatch
    {

        static readonly MethodInfo GetFrameAndVisualOfEngines = AccessTools.Property(typeof(MapScreen), "FrameAndVisualOfEngines")?.GetGetMethod(true);
        public static Dictionary<UIntPtr, Tuple<MatrixFrame, SettlementVisual>> FrameAndVisualOfEngines()
        {
            return GetFrameAndVisualOfEngines?.Invoke(null, null) as Dictionary<UIntPtr, Tuple<MatrixFrame, SettlementVisual>>;
        }


        static MethodInfo GetDesiredDecalColorMethod = AccessTools.Method(typeof(MapScreen), "GetDesiredDecalColor");

        public static uint GetDesiredDecalColor(this MapScreen mapScreen, bool isPrepOver, bool isHovered, bool isEnemy, bool isEmpty, bool isPlayerLeader)
        {
            return (uint) GetDesiredDecalColorMethod.Invoke(mapScreen, new object[] { isPrepOver, isHovered, isEnemy, isEmpty, isPlayerLeader });
        }


        static MethodInfo GetDesiredMaterialNameMethod = AccessTools.Method(typeof(MapScreen), "GetDesiredMaterialName");

        public static string GetDesiredMaterialName(this MapScreen mapScreen, bool isRanged, bool isAttacker, bool isEmpty, bool isTower)
        {
            return (string) GetDesiredMaterialNameMethod.Invoke(mapScreen, new object[] { isRanged, isAttacker, isEmpty, isTower });
        }

        static MethodInfo IsEscapedMethod = AccessTools.Method(typeof(MapView), "IsEscaped") ?? AccessTools.DeclaredMethod(typeof(MapView), "IsEscaped");

        static bool CheckIsEscaped(this MapView mapView)
        {
            try
            {
                return (bool) IsEscapedMethod.Invoke(mapView, new object[] { });
            }
            catch (Exception e)
            {
                // No logging, this is default behaviour
                return false;
            }
        }

        static MapScreenPatch()
        {
            Main.Harmony?.Patch(AccessTools.DeclaredMethod(typeof(MapScreen), "TaleWorlds.CampaignSystem.GameState.IMapStateHandler.AfterWaitTick"), prefix: new HarmonyMethod(typeof(MapScreenPatch), nameof(AfterWaitTick)));
        }

        public static bool AfterWaitTick(ref MapScreen __instance, ref MapViewsContainer ____mapViewsContainer, float dt)
        {
            if (__instance.SceneLayer.Input.IsHotKeyReleased("ToggleEscapeMenu") &&
                    PlayerSettlementBehaviour.Instance != null &&
                    (PlayerSettlementBehaviour.Instance.IsPlacingSettlement ||
                    PlayerSettlementBehaviour.Instance.IsPlacingPort ||
                        PlayerSettlementBehaviour.Instance.IsPlacingGate))
            {
                if (!____mapViewsContainer.IsThereAnyViewIsEscaped())
                {
                    if (PlayerSettlementBehaviour.Instance.IsPlacingGate || PlayerSettlementBehaviour.Instance.IsPlacingPort)
                    {
                        PlayerSettlementBehaviour.Instance.RefreshVisualSelection();
                        return false;
                    }
                    PlayerSettlementBehaviour.Instance.Reset();
                    MapBarExtensionVM.Current?.OnRefresh();
                    return false;
                }
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(HandleLeftMouseButtonClick))]
        public static bool HandleLeftMouseButtonClick(ref MapScreen __instance, ref MapEntityVisual visualOfSelectedEntity, CampaignVec2 intersectionPoint, PathFaceRecord mouseOverFaceIndex, bool isDoubleClick)
        {

            PlayerSettlementBehaviour behaviour = PlayerSettlementBehaviour.Instance;
            if (!__instance.SceneLayer.Input.GetIsMouseActive() || behaviour == null)
            {
                return true;
            }

            // The placement behavior now detects the mouse-release inside the same map tick that
            // calculates the preview frame. This prefix only suppresses Bannerlord's normal map
            // selection/click handling so it cannot select the ghost or newly created settlement.
            if (behaviour.IsPlacingPort || behaviour.IsPlacingGate || behaviour.IsPlacingSettlement)
            {
                return false;
            }

            RecoverPlayerVillageVisual(intersectionPoint, ref visualOfSelectedEntity);
            return true;
        }

        private static void RecoverPlayerVillageVisual(CampaignVec2 intersectionPoint, ref MapEntityVisual selectedVisual)
        {
            if (selectedVisual != null || !intersectionPoint.IsOnLand)
            {
                return;
            }

            var visuals = MapScreen.VisualsOfEntities;
            if (visuals == null || visuals.Count == 0)
            {
                return;
            }

            const float maximumDistanceSquared = 9f;
            float bestDistanceSquared = maximumDistanceSquared;
            MapEntityVisual bestVisual = null;
            var visitedSettlements = new HashSet<Settlement>();

            foreach (MapEntityVisual candidateVisual in visuals.Values)
            {
                if (candidateVisual is not MapEntityVisual<PartyBase> partyVisual)
                {
                    continue;
                }

                Settlement settlement = partyVisual.MapEntity?.Settlement;
                if (settlement == null || !settlement.IsVisible || !settlement.IsVillage ||
                    !settlement.IsPlayerBuilt() || !visitedSettlements.Add(settlement))
                {
                    continue;
                }

                float deltaX = settlement.Position.X - intersectionPoint.X;
                float deltaY = settlement.Position.Y - intersectionPoint.Y;
                float distanceSquared = deltaX * deltaX + deltaY * deltaY;
                if (distanceSquared > bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestVisual = candidateVisual;
            }

            if (bestVisual != null)
            {
                selectedVisual = bestVisual;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(CheckCursorState))]
        public static void CheckCursorState(ref MapScreen __instance)
        {
            if (__instance.SceneLayer.Input.GetIsMouseActive() && PlayerSettlementBehaviour.Instance != null)
            {
                if (PlayerSettlementBehaviour.Instance.IsPlacingPort || PlayerSettlementBehaviour.Instance.IsPlacingGate || PlayerSettlementBehaviour.Instance.IsPlacingSettlement)
                {
                    __instance.SceneLayer.ActiveCursor = PlayerSettlementBehaviour.Instance.PlacementSupported ? TaleWorlds.ScreenSystem.CursorType.Default : TaleWorlds.ScreenSystem.CursorType.Disabled;
                }
            }
        }
    }
}
