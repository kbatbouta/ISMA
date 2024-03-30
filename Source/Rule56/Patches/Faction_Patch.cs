using CombatAI.Comps;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
namespace CombatAI.Patches
{
    public static class Faction_Patch
    {
	    [HarmonyPatch(typeof(FactionUIUtility), nameof(FactionUIUtility.DrawFactionRow))]
	    private static class FactionUIUtility_DrawFactionRow_Patch
	    {
		    public static void Prefix(Faction faction, float rowY, Rect fillRect)
		    {
			    if (DebugSettings.godMode)
			    {
				    Rect rect        = new Rect(90f, rowY, 300f, 80f);
				    Rect rect2       = new Rect(0f, rowY, rect.xMax, 80f);
				    var  settings    = Finder.Settings.GetTechSettings(faction.def.techLevel);
				    var  m           = $"pathing:\t\t{settings.pathing}\nsapping:\t\t{settings.sapping}\ncover:\t\t{settings.cover}\nretreat:\t\t{settings.retreat}\n";
				    var  tracker     = Current.Game.GetComp_Fast<PersonalityTacker>();
				    var  personality = tracker.GetPersonality(faction);
				    m += "************\nleader personality:\n";
				    m += $"pathing:\t\t{personality.pathing}\nsapping:\t\t{personality.sapping}\ncover:\t\t{personality.cover}\nretreat:\t\t{personality.retreat}\n";
				    if (Mouse.IsOver(fillRect))
				    {
					    TooltipHandler.TipRegion(rect2, m);
				    }
			    }
		    }
	    }
	    
        [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
        private static class Faction_Notify_Notify_RelationKindChanged_Patch
        {
            public static void Prefix(Faction __instance, Faction other)
            {
                if (other.IsPlayerSafe())
                {
                    foreach (Map map in Find.Maps)
                    {
                        map.GetComp_Fast<SightTracker>()?.Notify_PlayerRelationChanged(__instance);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_MemberTookDamage))]
        private static class Faction_Notify_MemberTookDamage_Patch
        {
            public static void Prefix(Pawn member, DamageInfo dinfo)
            {
                if (member?.Spawned ?? false)
                {
                    if (!member.Dead && !member.Downed)
                    {
                        member.AI()?.Notify_TookDamage(dinfo);
                    }
                    member.Map?.GetComp_Fast<AvoidanceTracker>()?.Notify_Injury(member, dinfo);
                    member.GetComp_Fast<ThingComp_Statistics>()?.Notify_PawnTookDamage();
                }
            }
        }

        [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_MemberDied))]
        private static class Faction_Notify_MemberDied_Patch
        {
            public static void Prefix(Pawn member, bool wasWorldPawn, bool wasGuilty)
            {
                if (!wasWorldPawn && (member?.Spawned ?? false))
                {
                    member.Map.GetComp_Fast<AvoidanceTracker>()?.Notify_Death(member, member.Position);
                }
            }
        }
    }
}
