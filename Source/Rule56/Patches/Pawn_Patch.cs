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
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawAt))]
        private static class Pawn_DrawAt_Patch
        {
            public static bool Prefix(Pawn __instance, Vector3 drawLoc)
            {
	            MapComponent_FogGrid fog;
                if (__instance.Spawned)
                {
                    fog = __instance.Map.GetComp_Fast<MapComponent_FogGrid>() ?? null;
                }
                else
                {
	                fog = null;
                }
                return fog == null || (Finder.Settings.Debug || !fog.IsFogged(drawLoc.ToIntVec3()));
            }
        }

        [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
        private static class PawnRenderer_RenderPawnAt_Patch
        {
	        public static bool Prefix(PawnRenderer __instance, Vector3 drawLoc)
	        {
		        MapComponent_FogGrid fog;
		        if (__instance.pawn.Spawned)
		        {
			        fog = __instance.pawn.Map.GetComp_Fast<MapComponent_FogGrid>() ?? null;
		        }
		        else
		        {
			        fog = null;
		        }
		        return fog == null || (Finder.Settings.Debug || !fog.IsFogged(drawLoc.ToIntVec3()));
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
                return !__instance.Map.GetComp_Fast<MapComponent_FogGrid>().IsFogged(__instance.Position);
            }
        }
        
        [HarmonyPatch(typeof(SilhouetteUtility), nameof(SilhouetteUtility.ShouldDrawSilhouette))]
        private static class SilhouetteUtility_Patch
        {
	        public static bool Prefix(Thing thing, ref bool __result)
	        {
		        if (thing.Spawned)
		        {
			        if (thing.Map.GetComp_Fast<MapComponent_FogGrid>().IsFogged(thing.DrawPos.ToIntVec3()))
			        {
				        return __result = false;
			        }
		        }
		        return true;
	        }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
        private static class Pawn_DrawGUIOverlay_Patch
        {
            public static bool Prefix(Pawn __instance)
            {
	            MapComponent_FogGrid fog;
	            if (__instance.Spawned)
	            {
		            fog = __instance.Map.GetComp_Fast<MapComponent_FogGrid>() ?? null;
	            }
	            else
	            {
		            fog = null;
	            }
                return fog == null || (!fog.IsFogged(__instance.Position) && !Finder.Settings.Debug_DisablePawnGuiOverlay);
            }
        }
    }
}
