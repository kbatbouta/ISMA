using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
namespace CombatAI
{
	public class PersonalityTacker : GameComponent
	{
		public PersonalityTacker(Game game)
		{
		}

		public PersonalityResult GetPersonality(Thing thing)
		{
			if (thing == null)
			{
				return PersonalityResult.From(Finder.Settings.GetTechSettings(TechLevel.Undefined));
			}
			if (thing is Pawn pawn)
			{
				if (pawn.RaceProps.Animal)
				{
					return PersonalityResult.From(Finder.Settings.GetTechSettings(TechLevel.Animal));
				}
			}
			return GetPersonality(thing.Faction);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public PersonalityResult GetPersonality(Faction faction)
		{
			if (faction == null)
			{
				return PersonalityResult.From(Finder.Settings.GetTechSettings(TechLevel.Undefined));
			}
			if (Finder.Settings.Personalities_Enabled && faction.leader != null)
			{
				return PersonalityResult.From(Finder.Settings.GetTechSettings(faction.def.techLevel)) * 0.2f + GetPawnPersonality(faction.leader) * 0.6f + GetFactionPersonality(faction) * 0.2f;
			}
			return PersonalityResult.From(Finder.Settings.GetTechSettings(faction.def.techLevel));
		}

		private PersonalityResult GetPawnPersonality(Pawn pawn)
		{
			if (!TKVCache<int, Pawn, PersonalityResult>.TryGet(pawn.thingIDNumber, out PersonalityResult result) || !result.IsValid)
			{
				int seed = pawn.thingIDNumber;
				Rand.PushState(91 + seed + (int)(GenTicks.TicksGame / GenDate.TicksPerYear / 2));
				result         = PersonalityResult.Default;
				result.retreat = Rand.Range(0.0f, 2.0f);
				result.duck    = Rand.Range(0.0f, 2.0f);
				result.sapping = Rand.Range(0.0f, 2.5f);
				result.pathing = Rand.Range(0.0f, 2.5f);
				result.cover   = Rand.Range(0.0f, 2.0f);
				result.group   = Rand.Range(0.0f, 2.0f);
				Rand.PopState();
				TKVCache<int, Pawn, PersonalityResult>.Put(pawn.thingIDNumber, result);	
			}
			return result;
		}
		
		private PersonalityResult GetFactionPersonality(Faction faction)
		{
			if (!TKVCache<int, Faction, PersonalityResult>.TryGet(faction.loadID, out PersonalityResult result) || !result.IsValid)
			{
				Settings.FactionTechSettings techSettings = Finder.Settings.GetTechSettings(faction.def.techLevel);
				result         = PersonalityResult.Default;
				result.retreat = techSettings.retreat;
				result.duck    = techSettings.duck;
				result.sapping = techSettings.sapping;
				result.pathing = techSettings.pathing;
				result.cover   = techSettings.cover;
				result.group   = techSettings.group;
				TKVCache<int, Faction, PersonalityResult>.Put(faction.loadID, result);	
			}
			return result;
		}

		public struct PersonalityResult : IExposable
		{
			private const           int               version = 1;
			private static readonly PersonalityResult _default;
			static PersonalityResult()
			{
				PersonalityResult defaultPersonality = new PersonalityResult();
				defaultPersonality.retreat = 1;
				defaultPersonality.duck    = 1;
				defaultPersonality.cover   = 1;
				defaultPersonality.sapping = 1;
				defaultPersonality.pathing = 1;
				defaultPersonality.group   = 1;
				defaultPersonality._valid  = true;
				_default                   = defaultPersonality;
			}
			
			private bool              _valid;
			public  float             retreat;
			public  float             duck;
			public  float             cover;
			public  float             sapping;
			public  float             pathing;
			public  float             group;

			public bool IsValid
			{
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				get => _valid;
			}

			public static PersonalityResult Default
			{
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				get => _default;
			}

			public static PersonalityResult operator +(PersonalityResult first, PersonalityResult second)
			{
				PersonalityResult result = new PersonalityResult();
				result.retreat = first.retreat + second.retreat;
				result.duck    = first.duck + second.duck;
				result.cover   = first.cover + second.cover;
				result.sapping = first.sapping + second.sapping;
				result.pathing = first.pathing + second.pathing;
				result.group   = first.group + second.group;
				result._valid  = true;
				return result;
			}

			public static PersonalityResult operator *(PersonalityResult first, PersonalityResult second)
			{
				PersonalityResult result = new PersonalityResult();
				result.retreat = first.retreat * second.retreat;
				result.duck    = first.duck * second.duck;
				result.cover   = first.cover * second.cover;
				result.sapping = first.sapping * second.sapping;
				result.pathing = first.pathing * second.pathing;
				result.group   = first.group * second.group;
				result._valid  = true;
				return result;
			}
			
			public static PersonalityResult operator +(PersonalityResult first, float val)
			{
				PersonalityResult result = new PersonalityResult();
				result.retreat = first.retreat + val;
				result.duck    = first.duck + val;
				result.cover   = first.cover + val;
				result.sapping = first.sapping + val;
				result.pathing = first.pathing + val;
				result.group   = first.group + val;
				result._valid  = true;
				return result;
			}
			
			public static PersonalityResult operator *(PersonalityResult first, float val)
			{
				PersonalityResult result = new PersonalityResult();
				result.retreat = first.retreat * val;
				result.duck    = first.duck * val;
				result.cover   = first.cover * val;
				result.sapping = first.sapping * val;
				result.pathing = first.pathing * val;
				result.group   = first.group * val;
				result._valid  = true;
				return result;
			}

			public static PersonalityResult From(Settings.FactionTechSettings settings)
			{
				PersonalityResult result = new PersonalityResult();
				result.retreat = settings.retreat;
				result.duck    = settings.duck;
				result.cover   = settings.cover;
				result.sapping = settings.sapping;
				result.pathing = settings.pathing;
				result.group   = settings.group;
				result._valid   = true;
				return result;
			}

			public void ExposeData()
			{
				Scribe_Values.Look(ref retreat, $"retreat.{version}", 1);
				Scribe_Values.Look(ref duck, $"duck.{version}", 1);
				Scribe_Values.Look(ref cover, $"cover.{version}", 1);
				Scribe_Values.Look(ref sapping, $"sapping.{version}", 1);
				Scribe_Values.Look(ref pathing, $"pathing.{version}", 1);
				Scribe_Values.Look(ref group, $"group.{version}", 1);
				Scribe_Values.Look(ref _valid, $"valid.{version}");
			}
		}
	}
}
