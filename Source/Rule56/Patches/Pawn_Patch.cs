using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
namespace CombatAI.Patches
{
    public static class Pawn_Patch
    {
        private static MapComponent_FogGrid fogThings;
        private static MapComponent_FogGrid fogOverlay;

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawAt))]
        private static class Pawn_DrawAt_Patch
        {
            public static bool Prefix(Pawn __instance, Vector3 drawLoc)
            {
                if (fogOverlay == null &&  __instance.Spawned)
                {
                    fogOverlay = __instance.Map.GetComp_Fast<MapComponent_FogGrid>() ?? null;
                }
                return fogOverlay == null || (Finder.Settings.Debug || !fogThings.IsFogged(drawLoc.ToIntVec3()));
            }
        }

        [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
        private static class PawnRenderer_RenderPawnAt_Patch
        {
	        public static bool Prefix(PawnRenderer __instance, Vector3 drawLoc)
	        {
		        if (fogOverlay == null && __instance.pawn.Spawned)
		        {
			        fogOverlay = __instance.pawn.Map.GetComp_Fast<MapComponent_FogGrid>() ?? null;
		        }
		        return fogOverlay == null || (Finder.Settings.Debug || !fogThings.IsFogged(drawLoc.ToIntVec3()));
	        }
        }

        [HarmonyPatch]
        private static class Mote_Draw_Patch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Mote), nameof(MoteBubble.DrawAt));
                yield return AccessTools.Method(typeof(MoteBubble), nameof(MoteBubble.DrawAt));
            }

            public static bool Prefix(Mote __instance)
            {
                return !fogThings.IsFogged(__instance.Position);
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
        private static class Pawn_DrawGUIOverlay_Patch
        {
            public static bool Prefix(Pawn __instance)
            {
                if (fogOverlay == null && __instance.Spawned)
                {
                    fogOverlay = __instance.Map.GetComp_Fast<MapComponent_FogGrid>() ?? null;
                }
                return fogOverlay == null || (!fogOverlay.IsFogged(__instance.Position) && !Finder.Settings.Debug_DisablePawnGuiOverlay);
            }
        }

        [HarmonyPatch(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings))]
        private static class DynamicDrawManager_DrawDynamicThings_Patch
        {
            public static void Prefix(DynamicDrawManager __instance)
            {
                fogThings = __instance.map?.GetComp_Fast<MapComponent_FogGrid>();
            }

            public static void Postfix()
            {
                fogThings = null;
            }
        }

        [HarmonyPatch(typeof(ThingOverlays), nameof(ThingOverlays.ThingOverlaysOnGUI))]
        private static class ThingOverlays_ThingOverlaysOnGUI_Patch
        {
            public static void Prefix(ThingOverlays __instance)
            {
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }
                fogOverlay = Find.CurrentMap?.GetComp_Fast<MapComponent_FogGrid>() ?? null;
            }

            public static void Postfix()
            {
                fogOverlay = null;
            }
        }
    }
}
