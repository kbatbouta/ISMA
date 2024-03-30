using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CombatAI.Comps;
using CombatAI.Patches;
using CombatAI.Squads;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static CombatAI.AvoidanceTracker;
using static CombatAI.SightTracker;

namespace CombatAI
{
    public static class CombatAI_Utility
    {
	    [MethodImpl(MethodImplOptions.AggressiveInlining)]
	    public static bool IsBurning_Fast(this Pawn pawn)
	    {
		    CompAttachBase comp = pawn.CompAttachBase();
		    if (comp != null)
		    {
			    return comp.HasAttachment(ThingDefOf.Fire);
		    }
		    return false;
	    }

	    public static T[] Join<T>(this T[] a, T[] b)
	    {
		    T[] array = new T[a.Length + b.Length];
		    int index = 0;
		    for (int i = 0; i < a.Length; i++)
		    {
			    array[index++] = a[i];
		    }
		    for (int i = 0; i < b.Length; i++)
		    {
			    array[index++] = b[i];
		    }
		    return array;
	    }
	    
	    public static T[] Join<T>(this T[] a, T[] b, T[] c)
	    {
		    T[] array = new T[a.Length + b.Length + c.Length];
		    int index = 0;
		    for (int i = 0; i < a.Length; i++)
		    {
			    array[index++] = a[i];
		    }
		    for (int i = 0; i < b.Length; i++)
		    {
			    array[index++] = b[i];
		    }
		    for (int i = 0; i < c.Length; i++)
		    {
			    array[index++] = c[i];
		    }
		    return array;
	    }
	    
	    [MethodImpl(MethodImplOptions.AggressiveInlining)]
	    public static bool Is<T>(this T def, T other) where T : Def
        {
            return def != null && other != null && def == other;
        }

	    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Is<T>(this Job job, T other) where T : Def
        {
            return job != null && other != null && job.def == other;
        }
	    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Is<T>(this PawnDuty duty, T other) where T : Def
        {
            return duty != null && other != null && duty.def == other;
        }
	    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Is<T>(this Thing thing, T other) where T : Def
        {
            return thing != null && other != null && thing.def == other;
        }
	    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Is<T>(this Thing thing, Thing other) where T : Def
        {
            return thing != null && other != null && thing == other;
        }
	    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDormant(this Thing thing)
        {
            if (!TKVCache<Thing, CompCanBeDormant, bool>.TryGet(thing, out bool value, 240))
            {
                CompCanBeDormant dormant = thing.GetComp_Fast<CompCanBeDormant>();
                TKVCache<Thing, CompCanBeDormant, bool>.Put(thing, dormant != null && !dormant.Awake);
            }
            return value;
        }

        public static IntVec3 TryGetNextDutyDest(this Pawn pawn, float maxDistFromPawn = -1)
        {
            if (pawn.mindState?.duty == null || !pawn.mindState.duty.focus.IsValid)
            {
                return IntVec3.Invalid;
            }
            Tuple<int, int, IntVec3> key = new Tuple<int, int, IntVec3>
            {
                val1 = pawn.thingIDNumber,
                val2 = pawn.mindState.duty.def.index,
                val3 = pawn.mindState.duty.focus.Cell
            };
            if (!TKVCache<Tuple<int, int, IntVec3>, PawnDuty, IntVec3>.TryGet(key, out IntVec3 dutyDest, 600))
            {
                dutyDest = IntVec3.Invalid;
                if (pawn.mindState.duty.focus.Cell.DistanceToSquared(pawn.Position) > Maths.Sqr(Maths.Max(pawn.mindState.duty.radius, 10)))
                {
                    PawnPath path = pawn.Map.pathFinder.FindPath(pawn.Position, pawn.mindState.duty.focus.Cell, pawn);
                    if (path != null && path.nodes.Count > 0)
                    {
                        maxDistFromPawn = Mathf.Clamp(maxDistFromPawn, 5f, 64f);
                        int     i          = path.nodes.Count - 1;
                        float   maxDistSqr = Maths.Sqr(maxDistFromPawn);
                        IntVec3 pawnPos    = pawn.Position;
                        while (i >= 0 && path.nodes[i].DistanceToSquared(pawnPos) < maxDistSqr)
                        {
                            i--;
                        }
                        dutyDest = path.nodes[Maths.Max(i, 0)];
                        path.ReleaseToPool();
                    }
                }
                TKVCache<Tuple<int, int, IntVec3>, PawnDuty, IntVec3>.Put(key, dutyDest);
            }
            return dutyDest;
        }

        public static bool IsApproachingMeleeTarget(this Pawn pawn, float distLimit = 5, bool allowCached = true)
        {
            if (!allowCached || !TKVCache<Pawn, IsApproachingMeleeTargetCache, bool>.TryGet(pawn, out bool result, 5))
            {
                result = IsApproachingMeleeTarget(pawn, out _, distLimit);
                if (allowCached)
                {
                    TKVCache<Pawn, IsApproachingMeleeTargetCache, bool>.Put(pawn, result);
                }
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsApproachingMeleeTarget(this Pawn pawn, out Thing target, float distLimit = 5)
        {
            target = null;
            Job attackJob;
            return (attackJob = pawn.CurJob).Is(JobDefOf.AttackMelee) && attackJob.targetA.IsValid && attackJob.targetA.Cell.DistanceToSquared(pawn.Position) <= distLimit * distLimit && (target = attackJob.targetA.Thing) != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Verb TryGetAttackVerb(this Thing thing)
        {
            if (thing is Pawn pawn)
            {
                Pawn_EquipmentTracker equipment = pawn.equipment;
                if (equipment is { Primary: { } } && equipment.PrimaryEq.PrimaryVerb.Available() && (!equipment.PrimaryEq.PrimaryVerb.verbProps.onlyManualCast || pawn.CurJob != null && pawn.CurJob.def != JobDefOf.Wait_Combat))
                {
                    return equipment.PrimaryEq.PrimaryVerb;
                }
                Pawn_MeleeVerbs meleeVerbs = pawn.meleeVerbs;
                if (meleeVerbs != null)
                {
                    if (meleeVerbs.curMeleeVerb != null)
                    {
                        return meleeVerbs.curMeleeVerb;
                    }
                    if (!TKVCache<Thing, Pawn_MeleeVerbs, Verb>.TryGet(thing, out Verb verb, 600) || verb == null || verb.DirectOwner != thing)
                    {
                        TKVCache<Thing, Pawn_MeleeVerbs, Verb>.Put(thing, verb = meleeVerbs.TryGetMeleeVerb(null));
                        return verb;
                    }
                }
                return null;
            }
            if (thing is Building_Turret turret)
            {
                return turret.AttackVerb;
            }
            return null;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasWeaponVisible(this Pawn pawn)
        {
            return (pawn.CurJob?.def.alwaysShowWeapon ?? false) || (pawn.mindState?.duty?.def.alwaysShowWeapon ?? false);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetAvoidanceReader(this Pawn pawn, out AvoidanceReader reader)
        {
            return pawn.Map.GetComp_Fast<AvoidanceTracker>().TryGetReader(pawn, out reader);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSkillLevelSafe(this Pawn pawn, SkillDef def, int fallback)
        {
	        return pawn?.skills?.GetSkill(def).Level ?? fallback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetSightReader(this Pawn pawn, out SightReader reader)
        {
            if (pawn.Map.GetComp_Fast<SightTracker>().TryGetReader(pawn, out reader) && reader != null)
            {
                reader.armor = pawn.GetArmorReport();
                return true;
            }
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ISGrid<float> GetFloatGrid(this Map map)
        {
            ISGrid<float> grid = map.GetComp_Fast<MapComponent_CombatAI>().f_grid;
            grid.Reset();
            return grid;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CellFlooder GetCellFlooder(this Map map)
        {
            return map.GetComp_Fast<MapComponent_CombatAI>().flooder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetThingFlags(this Thing thing)
        {
            return (ulong)1 << GetThingFlagsIndex(thing);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetThingFlagsIndex(this Thing thing)
        {
            return thing.thingIDNumber % 64;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSquadFlags(this Squad squad)
        {
	        return squad != null ? ((ulong)1 << (squad.squadIDNumber % 64)) : 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSquadFlags(this Pawn pawn)
        {
	        return (ulong)1 << GetSquadFlagsIndex(pawn);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSquadFlagsIndex(this Pawn pawn)
        {
	        return pawn.AI()?.squad?.squadIDNumber ?? 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRangedSappingCompatible(this Verb verb)
        {
	        return verb != null && !verb.IsMeleeAttack && verb != null && !(verb is Verb_SpewFire || verb is Verb_ShootBeam) && !verb.IsEMP() && !verb.verbProps.CausesExplosion;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUsingVerb(this Pawn pawn)
        {
	        return pawn.CurJobDef?.Is(JobDefOf.UseVerbOnThing) ?? false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PersonalityTacker.PersonalityResult GetCombatPersonality(this Thing thing, int expiry = 240)
        {
	        if (!TKCache<Thing, PersonalityTacker.PersonalityResult>.TryGet(thing, out PersonalityTacker.PersonalityResult result, expiry))
	        {
		        TKCache<Thing, PersonalityTacker.PersonalityResult>.Put(thing, result = Current.Game.GetComponent<PersonalityTacker>().GetPersonality(thing));
	        }
	        return result;
        }

        private class IsApproachingMeleeTargetCache
        {
        }
    }
}
