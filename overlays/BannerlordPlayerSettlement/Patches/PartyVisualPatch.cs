
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using BannerlordPlayerSettlement.Extensions;
using BannerlordPlayerSettlement.Saves;
using BannerlordPlayerSettlement.Utils;

using HarmonyLib;

using Helpers;

using SandBox.View.Map;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using SandBox.ViewModelCollection;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.View;

namespace BannerlordPlayerSettlement.Patches
{

    [HarmonyPatch(typeof(SettlementVisual))]
    public static class PartyVisualPatch
    {
        static readonly PropertyInfo StrategicEntityProperty = AccessTools.Property(typeof(SettlementVisual), "StrategicEntity");
        static readonly MethodInfo SetStrategicEntity = StrategicEntityProperty?.GetSetMethod(true);
        static readonly FieldInfo StrategicEntityField = AccessTools.Field(typeof(SettlementVisual), "<StrategicEntity>k__BackingField")
            ?? AccessTools.Field(typeof(SettlementVisual), "_strategicEntity");

        static readonly PropertyInfo TownPhysicalEntitiesProperty = AccessTools.Property(typeof(SettlementVisual), "TownPhysicalEntities");
        static readonly MethodInfo SetTownPhysicalEntities = TownPhysicalEntitiesProperty?.GetSetMethod(true);
        static readonly FieldInfo TownPhysicalEntitiesField = AccessTools.Field(typeof(SettlementVisual), "<TownPhysicalEntities>k__BackingField")
            ?? AccessTools.Field(typeof(SettlementVisual), "_townPhysicalEntities");

        static readonly PropertyInfo CircleLocalFrameProperty = AccessTools.Property(typeof(SettlementVisual), "CircleLocalFrame");
        static readonly MethodInfo SetCircleLocalFrame = CircleLocalFrameProperty?.GetSetMethod(true);
        static readonly FieldInfo CircleLocalFrameField = AccessTools.Field(typeof(SettlementVisual), "<CircleLocalFrame>k__BackingField")
            ?? AccessTools.Field(typeof(SettlementVisual), "_circleLocalFrame");

        static readonly MethodInfo GetMapScene = AccessTools.Property(typeof(SettlementVisual), "MapScene")?.GetGetMethod(true);

        static MethodInfo PopulateSiegeEngineFrameListsFromChildren = AccessTools.Method(typeof(SettlementVisual), "PopulateSiegeEngineFrameListsFromChildren");
        static MethodInfo UpdateDefenderSiegeEntitiesCache = AccessTools.Method(typeof(SettlementVisual), "UpdateDefenderSiegeEntitiesCache");
        //static MethodInfo InitializePartyCollider = AccessTools.Method(typeof(MobilePartyVisual), "InitializePartyCollider");

        static FastInvokeHandler AddNewPartyVisualForPartyInvoker = MethodInvoker.GetHandler(AccessTools.Method(typeof(SettlementVisualManager), nameof(AddNewPartyVisualForParty)));

        public static void AddNewPartyVisualForParty(this SettlementVisualManager partyVisualManager, PartyBase partyBase)
        {
            AddNewPartyVisualForPartyInvoker(partyVisualManager, partyBase);
        }

        public static Scene MapScene(this SettlementVisual __instance)
        {
            if (__instance == null || GetMapScene == null)
            {
                return null;
            }

            return GetMapScene.Invoke(__instance, null) as Scene;
        }

        private static void AssignStrategicEntity(SettlementVisual visual, GameEntity entity)
        {
            if (visual == null)
            {
                return;
            }

            if (SetStrategicEntity != null)
            {
                SetStrategicEntity.Invoke(visual, new object[] { entity });
            }
            else
            {
                StrategicEntityField?.SetValue(visual, entity);
            }
        }

        private static void AssignTownPhysicalEntities(SettlementVisual visual, List<GameEntity> entities)
        {
            if (SetTownPhysicalEntities != null)
            {
                SetTownPhysicalEntities.Invoke(visual, new object[] { entities });
            }
            else
            {
                TownPhysicalEntitiesField?.SetValue(visual, entities);
            }
        }

        private static void AssignCircleLocalFrame(SettlementVisual visual, MatrixFrame frame)
        {
            if (SetCircleLocalFrame != null)
            {
                SetCircleLocalFrame.Invoke(visual, new object[] { frame });
            }
            else
            {
                CircleLocalFrameField?.SetValue(visual, frame);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(OnMapHoverSiegeEngine))]
        public static bool OnMapHoverSiegeEngine(ref SettlementVisual __instance, ref GameEntity[] ____attackerBatteringRamSpawnEntities, ref GameEntity[] ____defenderBreachableWallEntitiesCacheForCurrentLevel, ref GameEntity[] ____defenderRangedEngineSpawnEntitiesCacheForCurrentLevel, ref GameEntity[] ____attackerRangedEngineSpawnEntities, ref GameEntity[] ____attackerSiegeTowerSpawnEntities, ref MatrixFrame ____hoveredSiegeEntityFrame, MatrixFrame engineFrame)
        {
            try
            {
                bool isPlayerSettlement = (__instance.MapEntity != null && __instance.MapEntity.Settlement.IsPlayerBuilt());
                bool playerSiegeEvent = (PlayerSiege.PlayerSiegeEvent != null && (PlayerSiege.PlayerSiegeEvent.BesiegedSettlement.IsPlayerBuilt() || PlayerSiege.PlayerSiegeEvent.BesiegedSettlement.IsOverwritten(out OverwriteSettlementItem overwriteSettlementItem)));
                if (!playerSiegeEvent && !isPlayerSettlement)
                {
                    return true;
                }


                if (PlayerSiege.PlayerSiegeEvent == null)
                {
                    return true;
                }
                try
                {
                    for (int i = 0; i < (int) ____attackerBatteringRamSpawnEntities.Length; i++)
                    {
                        MatrixFrame globalFrame = ____attackerBatteringRamSpawnEntities[i].GetGlobalFrame();
                        if (globalFrame.NearlyEquals(engineFrame, 1E-05f))
                        {
                            if (____hoveredSiegeEntityFrame != globalFrame)
                            {
                                SiegeEvent.SiegeEngineConstructionProgress deployedMeleeSiegeEngines = PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedMeleeSiegeEngines[i];
                                InformationManager.ShowTooltip(typeof(List<TooltipProperty>), new Object[] { SandBoxUIHelper.GetSiegeEngineInProgressTooltip(deployedMeleeSiegeEngines) });
                            }
                            return false;
                        }
                    }
                    for (int j = 0; j < (int) ____attackerSiegeTowerSpawnEntities.Length; j++)
                    {
                        MatrixFrame matrixFrame = ____attackerSiegeTowerSpawnEntities[j].GetGlobalFrame();
                        if (matrixFrame.NearlyEquals(engineFrame, 1E-05f))
                        {
                            if (____hoveredSiegeEntityFrame != matrixFrame)
                            {
                                SiegeEvent.SiegeEngineConstructionProgress siegeEngineConstructionProgress = PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedMeleeSiegeEngines[(int) ____attackerBatteringRamSpawnEntities.Length + j];
                                InformationManager.ShowTooltip(typeof(List<TooltipProperty>), new Object[] { SandBoxUIHelper.GetSiegeEngineInProgressTooltip(siegeEngineConstructionProgress) });
                            }
                            return false;
                        }
                    }
                    for (int k = 0; k < (int) ____attackerRangedEngineSpawnEntities.Length; k++)
                    {
                        MatrixFrame globalFrame1 = ____attackerRangedEngineSpawnEntities[k].GetGlobalFrame();
                        if (globalFrame1.NearlyEquals(engineFrame, 1E-05f))
                        {
                            if (____hoveredSiegeEntityFrame != globalFrame1)
                            {
                                SiegeEvent.SiegeEngineConstructionProgress deployedRangedSiegeEngines = PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedRangedSiegeEngines[k];
                                InformationManager.ShowTooltip(typeof(List<TooltipProperty>), new Object[] { SandBoxUIHelper.GetSiegeEngineInProgressTooltip(deployedRangedSiegeEngines) });
                            }
                            return false;
                        }
                    }
                    for (int l = 0; l < (int) ____defenderRangedEngineSpawnEntitiesCacheForCurrentLevel.Length; l++)
                    {
                        MatrixFrame matrixFrame1 = ____defenderRangedEngineSpawnEntitiesCacheForCurrentLevel[l].GetGlobalFrame();
                        if (matrixFrame1.NearlyEquals(engineFrame, 1E-05f))
                        {
                            if (____hoveredSiegeEntityFrame != matrixFrame1)
                            {
                                SiegeEvent.SiegeEngineConstructionProgress deployedRangedSiegeEngines1 = PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Defender).SiegeEngines.DeployedRangedSiegeEngines[l];
                                InformationManager.ShowTooltip(typeof(List<TooltipProperty>), new Object[] { SandBoxUIHelper.GetSiegeEngineInProgressTooltip(deployedRangedSiegeEngines1) });
                            }
                            return false;
                        }
                    }
                    for (int m = 0; m < (int) ____defenderBreachableWallEntitiesCacheForCurrentLevel.Length; m++)
                    {
                        MatrixFrame globalFrame2 = ____defenderBreachableWallEntitiesCacheForCurrentLevel[m].GetGlobalFrame();
                        if (globalFrame2.NearlyEquals(engineFrame, 1E-05f))
                        {
                            if (____hoveredSiegeEntityFrame != globalFrame2 && (__instance as MapEntityVisual<PartyBase>).MapEntity.IsSettlement)
                            {
                                InformationManager.ShowTooltip(typeof(List<TooltipProperty>), new Object[] { SandBoxUIHelper.GetWallSectionTooltip((__instance as MapEntityVisual<PartyBase>).MapEntity.Settlement, m) });
                            }
                            return false;
                        }
                    }
                    ____hoveredSiegeEntityFrame = MatrixFrame.Identity;
                }
                catch (Exception e)
                {
                    // Silent fail here, do not fall back to default
                    LogManager.Log.Info(e.ToString());
                }

                return false;
            }
            catch (System.Exception e) { LogManager.Log.NotifyBad(e); }

            return true;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(nameof(OnMapHoverSiegeEngine))]
        public static Exception? FixOnMapHoverSiegeEngine(Exception? __exception, ref SettlementVisual __instance)
        {
            if (__exception != null)
            {
                var e = __exception;
                LogManager.Log.NotifyBad(e);
            }
            return null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(OnStartup))]
        public static bool OnStartup(ref SettlementVisual __instance, ref Dictionary<int, List<GameEntity>> ____gateBannerEntitiesWithLevels)
        {
            bool handlingCustomVisual = false;
            try
            {
                var mapVisual = __instance as MapEntityVisual<PartyBase>;
                var mapEntity = mapVisual?.MapEntity;
                var settlement = mapEntity?.Settlement;
                if (settlement == null)
                {
                    return true;
                }

                OverwriteSettlementItem? overwriteItem = null;
                bool isPlayerSettlement = settlement.IsPlayerBuilt();
                bool isOverwrite = settlement.IsOverwritten(out overwriteItem);
                if (!isPlayerSettlement && !isOverwrite)
                {
                    return true;
                }
                handlingCustomVisual = true;

                bool hasCircle = false;
                if (!isOverwrite)
                {
                    Scene scene = __instance.MapScene();
                    AssignStrategicEntity(
                        __instance,
                        scene?.GetCampaignEntityWithName(mapEntity.Id)
                        ?? scene?.GetCampaignEntityWithName(settlement.StringId));
                }

                if (__instance.StrategicEntity == null)
                {
                    IMapScene mapSceneWrapper = Campaign.Current?.MapSceneWrapper;
                    Scene scene = __instance.MapScene();
                    string stringId = settlement.StringId;
                    CampaignVec2 position = settlement.Position;

                    GameEntity copiedVisual = CultureVisualHelper.TryCopyCreatedSettlementVisual(
                        scene,
                        stringId,
                        settlement.Position.ToVec2());
                    if (copiedVisual != null)
                    {
                        AssignStrategicEntity(__instance, copiedVisual);
                    }
                    else
                    {
                        mapSceneWrapper?.AddNewEntityToMapScene(stringId, in position);
                        AssignStrategicEntity(
                            __instance,
                            scene?.GetCampaignEntityWithName(mapEntity.Id)
                            ?? scene?.GetCampaignEntityWithName(stringId));
                    }
                }

                if (__instance.StrategicEntity == null)
                {
                    LogManager.Log.Info($"Player settlement visual startup skipped because no strategic entity could be resolved for '{settlement.StringId}'.");
                    return false;
                }

                if (overwriteItem == null)
                {
                    var playerSettlementItem = PlayerSettlementInfo.Instance?.FindSettlement(settlement);
                    ApplySavedVisualTransform(__instance.StrategicEntity, playerSettlementItem?.RotationMat3, playerSettlementItem?.DeepEdits);
                }
                else
                {
                    ApplySavedVisualTransform(__instance.StrategicEntity, overwriteItem.RotationMat3, overwriteItem.DeepEdits);
                }

                if (settlement.IsFortification)
                {
                    List<GameEntity> gameEntities = new List<GameEntity>();
                    __instance.StrategicEntity.GetChildrenRecursive(ref gameEntities);
                    gameEntities.RemoveAll(entity => entity == null);

                    PopulateSiegeEngineFrameListsFromChildren?.Invoke(__instance, new object[] { gameEntities });
                    UpdateDefenderSiegeEntitiesCache?.Invoke(__instance, null);
                    AssignTownPhysicalEntities(__instance, gameEntities.FindAll(entity => entity.HasTag("bo_town")));

                    List<GameEntity> mapInteractionEntities = new List<GameEntity>();
                    Dictionary<int, List<GameEntity>> bannerEntitiesByLevel = new Dictionary<int, List<GameEntity>>()
                    {
                        { 1, new List<GameEntity>() },
                        { 2, new List<GameEntity>() },
                        { 3, new List<GameEntity>() }
                    };

                    foreach (GameEntity gameEntity in gameEntities)
                    {
                        if (gameEntity.HasTag("main_map_city_gate"))
                        {
                            MatrixFrame globalFrame = gameEntity.GetGlobalFrame();
                            NavigationHelper.IsPositionValidForNavigationType(
                                new CampaignVec2(globalFrame.origin.AsVec2, true),
                                MobileParty.NavigationType.Default);
                            mapInteractionEntities.Add(gameEntity);
                        }

                        if (gameEntity.HasTag("map_settlement_circle"))
                        {
                            AssignCircleLocalFrame(__instance, gameEntity.GetGlobalFrame());
                            hasCircle = true;
                            gameEntity.SetVisibilityExcludeParents(false);
                            mapInteractionEntities.Add(gameEntity);
                        }

                        if (!gameEntity.HasTag("map_banner_placeholder"))
                        {
                            continue;
                        }

                        int upgradeLevel = gameEntity.Parent?.GetUpgradeLevelOfEntity() ?? 0;
                        if (upgradeLevel != 0 && bannerEntitiesByLevel.TryGetValue(upgradeLevel, out List<GameEntity> levelEntities))
                        {
                            levelEntities.Add(gameEntity);
                        }
                        else
                        {
                            bannerEntitiesByLevel[1].Add(gameEntity);
                            bannerEntitiesByLevel[2].Add(gameEntity);
                            bannerEntitiesByLevel[3].Add(gameEntity);
                        }
                        mapInteractionEntities.Add(gameEntity);
                    }
                    ____gateBannerEntitiesWithLevels = bannerEntitiesByLevel;

                    List<MatrixFrame> campFrames1 = new List<MatrixFrame>();
                    List<MatrixFrame> campFrames2 = new List<MatrixFrame>();
                    if (Campaign.Current?.MapSceneWrapper != null)
                    {
                        Campaign.Current.MapSceneWrapper.GetSiegeCampFrames(settlement, out campFrames1, out campFrames2);
                    }
                    if (settlement.Town != null)
                    {
                        settlement.Town.BesiegerCampPositions1 = (campFrames1 ?? new List<MatrixFrame>()).ToArray();
                        settlement.Town.BesiegerCampPositions2 = (campFrames2 ?? new List<MatrixFrame>()).ToArray();
                    }

                    foreach (GameEntity interactionEntity in mapInteractionEntities)
                    {
                        interactionEntity.Remove(112);
                    }

                    if (mapEntity.IsSettlement)
                    {
                        foreach (GameEntity child in __instance.StrategicEntity.GetChildren())
                        {
                            if (!child.HasTag("main_map_city_port"))
                            {
                                continue;
                            }
                            MatrixFrame portFrame = child.GetGlobalFrame();
                            NavigationHelper.IsPositionValidForNavigationType(
                                new CampaignVec2(portFrame.origin.AsVec2, false),
                                MobileParty.NavigationType.Naval);
                        }
                    }
                }

                if (!hasCircle)
                {
                    AssignCircleLocalFrame(__instance, MatrixFrame.Identity);
                    MatrixFrame circleLocalFrame = __instance.CircleLocalFrame;
                    Mat3 circleRotation = circleLocalFrame.rotation;
                    if (settlement.IsVillage)
                    {
                        circleRotation.ApplyScaleLocal(1.75f);
                    }
                    else if (settlement.IsTown)
                    {
                        circleRotation.ApplyScaleLocal(5.75f);
                    }
                    else if (settlement.IsCastle)
                    {
                        circleRotation.ApplyScaleLocal(2.75f);
                    }
                    else
                    {
                        circleRotation.ApplyScaleLocal(1.75f);
                    }
                    circleLocalFrame.rotation = circleRotation;
                    AssignCircleLocalFrame(__instance, circleLocalFrame);
                }

                __instance.StrategicEntity.SetVisibilityExcludeParents(mapEntity.IsVisible);
                __instance.StrategicEntity.SetReadyToRender(true);
                __instance.StrategicEntity.SetEntityEnvMapVisibility(false);

                List<GameEntity> childEntities = new List<GameEntity>();
                __instance.StrategicEntity.GetChildrenRecursive(ref childEntities);
                var visualsOfEntities = MapScreen.VisualsOfEntities;
                var engineVisuals = MapScreenPatch.FrameAndVisualOfEngines();
                if (visualsOfEntities != null && !visualsOfEntities.ContainsKey(__instance.StrategicEntity.Pointer))
                {
                    visualsOfEntities.Add(__instance.StrategicEntity.Pointer, __instance);
                }
                foreach (GameEntity childEntity in childEntities)
                {
                    if (childEntity == null || visualsOfEntities == null ||
                        visualsOfEntities.ContainsKey(childEntity.Pointer) ||
                        (engineVisuals != null && engineVisuals.ContainsKey(childEntity.Pointer)))
                    {
                        continue;
                    }
                    visualsOfEntities.Add(childEntity.Pointer, __instance);
                }

                __instance.StrategicEntity.SetAsPredisplayEntity();
                return false;
            }
            catch (System.Exception e)
            {
                if (handlingCustomVisual)
                {
                    LogManager.Log.SilentException(e);
                    return false;
                }

                LogManager.Log.NotifyBad(e);
                return true;
            }
        }

        private static void ApplySavedVisualTransform(GameEntity strategicEntity, Mat3Saveable? rotation, List<DeepTransformEdit> deepEdits)
        {
            if (strategicEntity == null)
            {
                return;
            }

            if (rotation != null)
            {
                MatrixFrame frame = strategicEntity.GetFrame();
                frame.rotation = rotation;
                strategicEntity.SetFrame(ref frame);
            }

            if (deepEdits == null)
            {
                return;
            }

            List<GameEntity> children = new List<GameEntity>();
            strategicEntity.GetChildrenRecursive(ref children);
            foreach (DeepTransformEdit edit in deepEdits)
            {
                if (edit == null || edit.Index >= children.Count)
                {
                    continue;
                }

                GameEntity entity = edit.Index < 0 ? strategicEntity : children[edit.Index];
                if (entity == null)
                {
                    continue;
                }

                MatrixFrame local = entity.GetFrame();
                local.rotation = edit.Transform?.RotationScale != null ? edit.Transform.RotationScale : local.rotation;
                if (edit.Index >= 0)
                {
                    local.origin = edit.Transform?.Position != null ? edit.Transform.Position : local.origin;
                }
                else if (edit.Transform?.Offsets != null)
                {
                    local.origin += edit.Transform.Offsets;
                }
                entity.SetFrame(ref local);
            }

            foreach (DeepTransformEdit edit in deepEdits.AsEnumerable().Reverse().Where(item => item != null && item.IsDeleted && item.Index >= 0))
            {
                if (edit.Index < children.Count)
                {
                    children[edit.Index]?.ClearEntity();
                }
            }
        }


        [HarmonyPrefix]
        [HarmonyPatch("SetSettlementLevelVisibility")]
        public static bool SetSettlementLevelVisibility(ref SettlementVisual __instance)
        {
            var mapVisual = __instance as MapEntityVisual<PartyBase>;
            var settlement = mapVisual?.MapEntity?.Settlement;
            if (!CultureVisualHelper.IsTorPlayerSettlement(settlement) || !settlement.IsPlayerBuilt())
            {
                return true;
            }

            // ToR player settlements use a runtime-created strategic entity. Bannerlord's
            // native level-visibility routine recursively toggles physics on every child.
            // That native pass is unsafe for runtime-created campaign visual hierarchies
            // and can raise an AccessViolationException before managed code can recover.
            // The copied prefab already carries its own visibility state; skip only this
            // native physics/level pass for the ToR player-settlement visual.
            return false;
        }


        [HarmonyFinalizer]
        //[HarmonyPatch(nameof(SettlementVisual.OnStartup))]
        [HarmonyPatch(nameof(OnStartup))]
        public static Exception FixOnStartup(object __exception, ref SettlementVisual __instance)
        {
            if (__exception != null)
            {
                var e = __exception;
                if (e != null)
                {
                    if (e is Exception ex)
                    {

                        LogManager.Log.NotifyBad(ex);
                    }
                    else
                    {

                        LogManager.Log.NotifyBad(e.ToString());
                    }
                }
            }
            return null;
        }
    }
}
