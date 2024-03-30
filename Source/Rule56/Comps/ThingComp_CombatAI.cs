using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CombatAI.Abilities;
using CombatAI.R;
using CombatAI.Squads;
using CombatAI.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace CombatAI.Comps
{
	public class ThingComp_CombatAI : ThingComp
	{
		private readonly Dictionary<Thing, AIEnvAgentInfo> allAllies;
		private readonly Dictionary<Thing, AIEnvAgentInfo> allEnemies;
		/// <summary>
		///     Escorting pawns.
		/// </summary>
		private readonly List<Pawn> escorts = new List<Pawn>();
		/// <summary>
		///     Set of visible enemies. A queue for visible enemies during scans.
		/// </summary>
		private readonly List<Thing> rangedEnemiesTargetingSelf = new List<Thing>(4);
		/// <summary>
		///     Sapper path nodes.
		/// </summary>
		private readonly List<IntVec3> sapperNodes = new List<IntVec3>();
		/// <summary>
		///     Aggro countdown ticks.
		/// </summary>
		private int aggroTicks;
		/// <summary>
		///     Aggro target.
		/// </summary>
		private LocalTargetInfo aggroTarget;

		private Thing _bestEnemy;
		private int   _last;

		private int _sap;
		/// <summary>
		///     Pawn ability caster.
		/// </summary>
		public Pawn_AbilityCaster abilities;
		/// <summary>
		///     Saves job logs. for debugging only.
		/// </summary>
		public List<JobLog> jobLogs;
		/// <summary>
		///     Pawn squad
		/// </summary>
		public Squad squad;
		/// <summary>
		///     Parent armor report.
		/// </summary>
		private ArmorReport armor;
		/// <summary>
		///     Cell to stand on while sapping
		/// </summary>
		private IntVec3 cellBefore = IntVec3.Invalid;
		private IntVec3     cellAhead = IntVec3.Invalid;
		public  AIAgentData data;
		/// <summary>
		///     Custom pawn duty tracker. Allows the execution of new duties then going back to the old one once the new one is
		///     finished.
		/// </summary>
		public Pawn_CustomDutyTracker duties;
		/// <summary>
		///     Number of enemies in range.
		///     Updated by the sightgrid.
		/// </summary>
		public int enemiesInRangeNum;
		/// <summary>
		///     Whether to find escorts.
		/// </summary>
		private bool findEscorts;
		/// <summary>
		///     Target forced by the player.
		/// </summary>
		public LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;
		/// <summary>
		///     Sapper timestamp
		/// </summary>
		private int sapperStartTick;
		//Whether a scan is occuring.
		private bool scanning;
		/// <summary>
		///     Parent pawn.
		/// </summary>
		public Pawn selPawn;
		/// <summary>
		///     Parent sight reader.
		/// </summary>
		public SightTracker.SightReader sightReader;

		public ThingComp_CombatAI()
		{
			allEnemies = new Dictionary<Thing, AIEnvAgentInfo>(32);
			allAllies  = new Dictionary<Thing, AIEnvAgentInfo>(32);
			data       = new AIAgentData();
		}

		/// <summary>
		///     Whether the pawn is downed or dead.
		/// </summary>
		public bool IsDeadOrDowned
		{
			get => selPawn.Dead || selPawn.Downed;
		}

		/// <summary>
		///     Whether the pawning is sapping.
		/// </summary>
		public bool IsSapping
		{
			get => cellBefore.IsValid && sapperNodes.Count > 0;
		}
		/// <summary>
		///     Whether the pawn is available to escort other pawns or available for sapping.
		/// </summary>
		public bool CanSappOrEscort
		{
			get => !IsSapping && GenTicks.TicksGame - releasedTick > 900;
		}

		public override void Initialize(CompProperties props)
		{
			base.Initialize(props);
			selPawn = parent as Pawn;
			if (selPawn == null)
			{
				throw new Exception($"ThingComp_CombatAI initialized for a non pawn {parent}/def:{parent.def}");
			}
		}

		public override void PostDeSpawn(Map map)
		{
			base.PostDeSpawn(map);
			allAllies.Clear();
			allEnemies.Clear();
			escorts.Clear();
			rangedEnemiesTargetingSelf.Clear();
			sapperNodes.Clear();
			aggroTarget = LocalTargetInfo.Invalid;
			data?.PostDeSpawn();
		}

		public override void PostSpawnSetup(bool respawningAfterLoad)
		{
			base.PostSpawnSetup(respawningAfterLoad);
			armor          =   selPawn.GetArmorReport();
			duties         ??= new Pawn_CustomDutyTracker(selPawn);
			duties.pawn    =   selPawn;
			abilities      ??= new Pawn_AbilityCaster(selPawn);
			abilities.pawn =   selPawn;
		}

#if DEBUG_REACTION
		private static List<Thing> _buffer = new List<Thing>(16);
		public override void CompTick()
		{
			base.CompTick();
			if (selPawn.IsHashIntervalTick(15) && Find.Selector.SelectedPawns.Contains(selPawn))
			{
				List<Thing> buffer = _buffer;
				buffer.Clear();
				sightReader.GetEnemies(selPawn.Position, buffer);
				foreach (var thing in buffer)
				{
					selPawn.Map.debugDrawer.FlashCell(thing.Position, 0.01f, "H", 15);
				}
				buffer.Clear();
				sightReader.GetFriendlies(selPawn.Position, buffer);
				foreach (var thing in buffer)
				{
					selPawn.Map.debugDrawer.FlashCell(thing.Position, 0.99f, "F", 15);
				}
				buffer.Clear();
			}
		}
#endif

		public override void CompTickRare()
		{
			base.CompTickRare();
			if (!selPawn.Spawned)
			{
				return;
			}
			if (IsDeadOrDowned)
			{
				if (IsSapping || escorts.Count > 0)
				{
					ReleaseEscorts(false);
					sapperNodes.Clear();
					cellBefore      = IntVec3.Invalid;
					sapperStartTick = -1;
				}
				return;
			}
			if (selPawn.IsBurning_Fast())
			{
				return;
			}
			if (aggroTicks > 0)
			{
				aggroTicks -= GenTicks.TickRareInterval;
				if (aggroTicks <= 0)
				{
					if (aggroTarget is { IsValid: true, HasThing: true } && aggroTarget.Thing.Spawned && (aggroTarget.Thing is not Pawn enemy || !(enemy.Downed || enemy.Dead)))
					{
						TryAggro(aggroTarget, 0.8f, Rand.Int);
					}
					aggroTarget = LocalTargetInfo.Invalid;
					aggroTicks  = (int) 240;
				}

			}
			if (duties != null)
			{
				duties.TickRare();
			}
			if (abilities != null)
			{
				// abilities.TickRare(visibleEnemies);
			}
			if (selPawn.IsApproachingMeleeTarget(out Thing target))
			{
				ThingComp_CombatAI comp = target.GetComp_Fast<ThingComp_CombatAI>();
				
				if (comp != null)
				{
					comp.Notify_BeingTargeted(selPawn, selPawn.CurrentEffectiveVerb);
				}
			}
			if (IsSapping)
			{
				// end if this pawn is in
				if (sapperNodes[0].GetEdifice(parent.Map) == null)
				{
					cellBefore = sapperNodes[0];
					sapperNodes.RemoveAt(0);
					if (sapperNodes.Count > 0)
					{
						_sap++;
						TryStartSapperJob();
					}
					else
					{
						ReleaseEscorts(success: true);
						sapperNodes.Clear();
						cellBefore      = IntVec3.Invalid;
						sapperStartTick = -1;
					}
				}
				else
				{
					TryStartSapperJob();
				}
			}
			if (forcedTarget.IsValid)
			{
				if (Mod_CE.active && (selPawn.CurJobDef.Is(Mod_CE.ReloadWeapon) || selPawn.CurJobDef.Is(Mod_CE.HunkerDown)))
				{
					return;
				}
				// remove the forced target on when not drafted and near the target
				if (!selPawn.Drafted || selPawn.Position.DistanceToSquared(forcedTarget.Cell) < 25)
				{
					forcedTarget = LocalTargetInfo.Invalid;
				}
				else if (enemiesInRangeNum == 0 && (selPawn.jobs.curJob?.def.Is(JobDefOf.Goto) == false || selPawn.pather?.Destination != forcedTarget.Cell))
				{
					Job gotoJob = JobMaker.MakeJob(JobDefOf.Goto, forcedTarget);
					gotoJob.canUseRangedWeapon    = true;
					gotoJob.checkOverrideOnExpire = false;
					gotoJob.locomotionUrgency     = LocomotionUrgency.Jog;
					gotoJob.playerForced          = true;
					selPawn.jobs.ClearQueuedJobs();
					selPawn.jobs.StartJob(gotoJob);
				}
			}
		}

		public override void CompTickLong()
		{
			base.CompTickLong();
			// update the current armor report.
			armor = selPawn.GetArmorReport();
		}

		/// <summary>
		///     Returns whether the parent has took damage in the last number of ticks.
		/// </summary>
		/// <param name="ticks">The number of ticks</param>
		/// <returns>Whether the pawn took damage in the last number of ticks</returns>
		public bool TookDamageRecently(int ticks)
		{
			return data.TookDamageRecently(ticks);
		}
		/// <summary>
		///     Returns whether the parent has reacted in the last number of ticks.
		/// </summary>
		/// <param name="ticks">The number of ticks</param>
		/// <returns>Whether the reacted in the last number of ticks</returns>
		public bool ReactedRecently(int ticks)
		{
			return data.InterruptedRecently(ticks);
		}

		/// <summary>
		///     Called when a scan for enemies starts. Will clear the visible enemy queue. If not called, calling OnScanFinished or
		///     Notify_VisibleEnemy(s) will result in an error.
		///     Should only be called from the main thread.
		/// </summary>
		public void OnScanStarted()
		{
			if (allEnemies.Count != 0)
			{
				if (scanning)
				{
					Log.Warning($"ISMA: OnScanStarted called while scanning. ({allEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
					return;
				}
				allEnemies.Clear();
			}
			if (allAllies.Count != 0)
			{
				allAllies.Clear();
			}
			scanning         = true;
			data.LastScanned = lastScanned = GenTicks.TicksGame;
		}

		/// <summary>
		///     Called a scan is finished. This will process enemies queued in visibleEnemies. Responsible for parent reacting.
		///     If OnScanStarted is not called before then this will result in an error.
		///     Should only be called from the main thread.
		/// </summary>
		/// <param name="progress">Used to debug where an exception was thrown</param>
		public void OnScanFinished(ref int progress)
		{
			if (scanning == false)
			{
				Log.Warning($"ISMA: OnScanFinished called while not scanning. ({allEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
				return;
			}
			scanning = false;
			// set enemies.
			data.ReSetEnemies(allEnemies);
			// set allies.
			data.ReSetAllies(allAllies);
			// update when this pawn last saw enemies
			data.LastSawEnemies = data.NumEnemies > 0 ? GenTicks.TicksGame : -1;
			//
			var settings = parent is Pawn ? Finder.Settings.GetDefKindSettings(parent as Pawn) :  Finder.Settings.GetDefKindSettings(parent.def, null);
			// For debugging and logging.
			progress = 1;
			// skip for animals.
			if (selPawn.mindState == null || selPawn.RaceProps.Animal || IsDeadOrDowned)
			{
				return;
			}
			// skip for player pawns with no forced target.
			if (selPawn.Faction.IsPlayerSafe() && !forcedTarget.IsValid)
			{
				return;
			}
			// if the pawn is burning don't react.
			if (selPawn.IsBurning_Fast())
			{
				return;
			}
			ReactDebug_Internel(out progress);
			// For debugging and logging.
			progress = 3;
			List<Thing> targetedBy = data.BeingTargetedBy;
			// update when last saw enemies
			data.LastSawEnemies = data.NumEnemies > 0 ? GenTicks.TicksGame : data.LastSawEnemies;
			// if no enemies are visible nor anyone targeting self skip.
			if (data.NumEnemies == 0 && targetedBy.Count == 0)
			{
				return;
			}
			// For debugging and logging.
			progress = 4;
			// check if the TPS is good enough.
			// reduce cooldown if the pawn hasn't seen enemies for a few ticks
			if (!Finder.Performance.TpsCriticallyLow)
			{
				// if the pawn haven't seen enemies in a while and recently reacted then reset lastInterupted.
				// This is done to ensure fast reaction times when exiting then entering combat.
				if (GenTicks.TicksGame - lastSawEnemies > 90)
				{
					lastInterupted = -1;
					if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight)
					{
						parent.Map.debugDrawer.FlashCell(parent.Position, 1.0f, "X", 60);
					}
				}
				lastSawEnemies = GenTicks.TicksGame;
			}
			// For debugging and logging.
			progress = 5;
			// get body size and use it in cooldown math.
			float bodySize = selPawn.RaceProps.baseBodySize;
			// pawn reaction cooldown changes with their body size.
			if (data.InterruptedRecently((int)(45 * bodySize)) || data.RetreatedRecently((int)(120 * bodySize)))
			{
				return;
			}
			// if the pawn is kidnapping a pawn skip.
			if (selPawn.CurJobDef.Is(JobDefOf.Kidnap) || selPawn.CurJobDef.Is(JobDefOf.Flee))
			{
				return;
			}
			// if the pawn is sapping, stop sapping.
			if (selPawn.CurJobDef.Is(JobDefOf.Mine) && sightReader.GetVisibilityToEnemies(selPawn.Position) > 0)
			{
				selPawn.jobs.StopAll();
			}
			// For debugging and logging.
			progress = 6;
			// Skip if some vanilla duties are active.
			PawnDuty duty = selPawn.mindState.duty;
			if (duty.Is(DutyDefOf.Build) || duty.Is(DutyDefOf.SleepForever) || duty.Is(DutyDefOf.TravelOrLeave))
			{
				data.LastInterrupted = GenTicks.TicksGame + Rand.Int % 240;
				return;
			}
			PersonalityTacker.PersonalityResult personality = parent.GetCombatPersonality();
			IntVec3                             selPos      = selPawn.Position;
			// used to update nearest enemy THing
			// For debugging and logging.
			progress = 7;
			// check if the chance of survivability is high enough
			// defensive actions
			Verb verb = selPawn.CurrentEffectiveVerb;
			if (verb is { WarmupStance.ticksLeft: < 40 })
			{
				return;
			}
			// For debugging and logging.
			progress = 8;
			rangedEnemiesTargetingSelf.Clear();
			// Calc the current threat 
			ThreatUtility.CalculateThreat(selPawn, targetedBy, armor, rangedEnemiesTargetingSelf, null, out float possibleDmg, out float possibleDmgDistance, out float possibleDmgWarmup, out Thing nearestEnemy, out float nearestEnemyDist, out Pawn nearestMeleeEnemy, out float nearestMeleeEnemyDist, ref progress);
			// Try retreat
			if ((settings?.Retreat_Enabled ?? true) && (bodySize < 2 || selPawn.RaceProps.Humanlike))
			{
				// For debugging and logging.
				progress = 9;
				if (TryStartRetreat(possibleDmg, possibleDmgWarmup, possibleDmgDistance, personality, nearestMeleeEnemy, nearestMeleeEnemyDist, ref progress))
				{
					return;
				}
			}
			// For debugging and logging.
			progress = 100;
			if (duty.Is(DutyDefOf.ExitMapRandom))
			{
				return;
			}
			// For debugging and logging.
			progress = 200;
			// offensive actions
			if (verb != null)
			{
				// if the pawn is retreating and the pawn is still in danger or recently took damage, skip any offensive reaction.
				if (verb.IsMeleeAttack)
				{
					TryMeleeReaction(possibleDmg, ref progress);
				}
				// ranged
				else
				{
					TryRangedReaction(verb, selPos, personality, bodySize, duty, ref progress);
				}
			}
		}
		private void TryRangedReaction(Verb verb, IntVec3 selPos, PersonalityTacker.PersonalityResult personality, float bodySize, PawnDuty duty, ref int progress)
		{
			Thing nearestEnemy          = null;
			float nearestEnemyDist      = 1e6f;
			Pawn  nearestMeleeEnemy     = null;
			float nearestMeleeEnemyDist = 1e6f;
			// For debugging and logging.
			progress = 208;
			if (selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) && GenTicks.TicksGame - selPawn.CurJob.startTick < 60)
			{
				return;
			}
			// if CE is active skip reaction if the pawn is reloading or hunkering down.
			if (Mod_CE.active && (selPawn.CurJobDef.Is(Mod_CE.ReloadWeapon) || selPawn.CurJobDef.Is(Mod_CE.HunkerDown)))
			{
				return;
			}
			// For debugging and logging.
			progress = 209;
			// check if the verb is available.
			if (!verb.Available() || Mod_CE.active && Mod_CE.IsAimingCE(verb))
			{
				return;
			}
			bool  bestEnemyVisibleNow  = false;
			bool  bestEnemyVisibleSoon = false;
			ulong selFlags             = selPawn.GetThingFlags();
			// For debugging and logging.
			progress = 210;
			// A not fast check will check for retreat and for reactions to enemies that are visible or soon to be visible.
			// A fast check will check only for retreat.
			IEnumerator<AIEnvAgentInfo> enumerator = data.Enemies();
			while (enumerator.MoveNext())
			{
				progress = 301;
				AIEnvAgentInfo info = enumerator.Current;
#if DEBUG_REACTION
				if (info.thing == null)
				{
					Log.Error("Found null thing (3)");
					continue;
				}
#endif
				// For debugging and logging.
				progress = 302;
				if (info.thing.Spawned)
				{
					Pawn enemyPawn = info.thing as Pawn;
					if ((sightReader.GetDynamicFriendlyFlags(info.thing.Position) & selFlags) != 0 && verb.CanHitTarget(info.thing))
					{
						// For debugging and logging.
						progress = 311;
						if (!bestEnemyVisibleNow)
						{
							nearestEnemy        = null;
							nearestEnemyDist    = 1e4f;
							bestEnemyVisibleNow = true;
						}
						float dist = selPawn.DistanceTo_Fast(info.thing);
						if (dist < nearestEnemyDist)
						{
							nearestEnemyDist = dist;
							nearestEnemy     = info.thing;
						}
					}
					else if (enemyPawn != null && !bestEnemyVisibleNow)
					{
						// For debugging and logging.
						progress = 312;
						IntVec3 temp = PawnPathUtility.GetMovingShiftedPosition(enemyPawn, 120);
						if ((sightReader.GetDynamicFriendlyFlags(temp) & selFlags) != 0 && verb.CanHitTarget(temp))
						{
							if (!bestEnemyVisibleSoon)
							{
								nearestEnemy         = null;
								nearestEnemyDist     = 1e4f;
								bestEnemyVisibleSoon = true;
							}
							float dist = selPawn.DistanceTo_Fast(info.thing);
							if (dist < nearestEnemyDist)
							{
								nearestEnemyDist = dist;
								nearestEnemy     = info.thing;
							}
						}
						else if (!bestEnemyVisibleSoon)
						{
							float dist = selPawn.DistanceTo_Fast(info.thing);
							if (dist < nearestEnemyDist)
							{
								nearestEnemyDist = dist;
								nearestEnemy     = info.thing;
							}
						}
					}
					// For debugging and logging.
					progress = 303;
					if (enemyPawn != null && (enemyPawn.CurrentEffectiveVerb?.IsMeleeAttack ?? false))
					{
						float dist = selPos.DistanceTo_Fast(PawnPathUtility.GetMovingShiftedPosition(enemyPawn, 120f));
						if (dist < nearestMeleeEnemyDist)
						{
							nearestMeleeEnemyDist = dist;
							nearestMeleeEnemy     = enemyPawn;
						}
					}
				}
			}
			progress = 400;
			void StartOrQueueCoverJob(IntVec3 cell, int codeOffset)
			{
				Job curJob = selPawn.CurJob;
				if (curJob != null && (curJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) || curJob.Is(JobDefOf.Goto)) && cell == curJob.targetA.Cell)
				{
					if (selPawn.jobs.jobQueue.Count == 0 || !selPawn.jobs.jobQueue[0].job.Is(JobDefOf.Wait_Combat))
					{
						Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
						job_waitCombat.targetA                        = nearestEnemy;
						job_waitCombat.playerForced                   = forcedTarget.IsValid;
						job_waitCombat.endIfCantShootTargetFromCurPos = true;
						job_waitCombat.checkOverrideOnExpire          = true;
						selPawn.jobs.ClearQueuedJobs();
						selPawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
					}
				}
				else if (cell == selPawn.Position)
				{
					if (!selPawn.CurJob.Is(JobDefOf.Wait_Combat))
					{
						Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
						job_waitCombat.targetA                        = nearestEnemy;
						job_waitCombat.playerForced                   = forcedTarget.IsValid;
						job_waitCombat.endIfCantShootTargetFromCurPos = true;
						job_waitCombat.checkOverrideOnExpire          = true;
						selPawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
					}
				}
				else if (selPawn.CurJob.Is(JobDefOf.Wait_Combat))
				{
					_last = 50 + codeOffset;
					Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Cover, cell);
					job_goto.playerForced          = forcedTarget.IsValid;
					job_goto.expiryInterval        = -1;
					job_goto.checkOverrideOnExpire = false;
					job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
					Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
					job_waitCombat.targetA                        = nearestEnemy;
					job_waitCombat.playerForced                   = forcedTarget.IsValid;
					job_waitCombat.endIfCantShootTargetFromCurPos = true;
					job_waitCombat.checkOverrideOnExpire          = true;
					selPawn.jobs.ClearQueuedJobs();
					selPawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
					selPawn.jobs.jobQueue.EnqueueFirst(job_goto);
				}
				else
				{
					_last                         = 51 + codeOffset;
					selPawn.mindState.enemyTarget = nearestEnemy;
					Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Cover, cell);
					job_goto.expiryInterval        = -1;
					job_goto.checkOverrideOnExpire = false;
					job_goto.playerForced          = forcedTarget.IsValid;
					job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
					Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
					job_waitCombat.playerForced                   = forcedTarget.IsValid;
					job_waitCombat.endIfCantShootTargetFromCurPos = true;
					job_waitCombat.checkOverrideOnExpire          = true;
					selPawn.jobs.ClearQueuedJobs();
					selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
					selPawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
				}
				data.LastInterrupted = GenTicks.TicksGame;
			}

			if (nearestEnemy != null && rangedEnemiesTargetingSelf.Contains(nearestEnemy))
			{
				rangedEnemiesTargetingSelf.Remove(nearestEnemy);
			}
			progress = 500;
			bool retreatMeleeThreat = nearestMeleeEnemy != null && verb.EffectiveRange * personality.retreat > 16 && nearestMeleeEnemyDist < Maths.Max(verb.EffectiveRange * personality.retreat / 3f, 9) && 0.25f * data.NumAllies < data.NumEnemies;
			bool retreatThreat      = !retreatMeleeThreat && nearestEnemy != null && nearestEnemyDist < Maths.Max(verb.EffectiveRange * personality.retreat / 4f, 5);
			_bestEnemy = retreatMeleeThreat ? nearestMeleeEnemy : nearestEnemy;
			// retreat because of a close melee threat
			if (bodySize < 2.0f && (retreatThreat || retreatMeleeThreat))
			{
				progress   = 501;
				_bestEnemy = retreatThreat ? nearestEnemy : nearestMeleeEnemy;
				_last      = 40;
				CoverPositionRequest request = new CoverPositionRequest();
				request.caster             = selPawn;
				request.target             = nearestMeleeEnemy;
				request.verb               = verb;
				request.majorThreats       = rangedEnemiesTargetingSelf;
				request.checkBlockChance   = true;
				request.maxRangeFromCaster = verb.EffectiveRange / 2 + 8;
				if (CoverPositionFinder.TryFindRetreatPosition(request, out IntVec3 cell))
				{
					if (cell != selPos)
					{
						_last = 41;
						Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Retreat, cell);
						job_goto.expiryInterval        = -1;
						job_goto.checkOverrideOnExpire = false;
						job_goto.playerForced          = forcedTarget.IsValid;
						job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
						selPawn.jobs.ClearQueuedJobs();
						selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
						data.LastRetreated = GenTicks.TicksGame;
					}
				}
			}
			// best enemy is insight
			else if (nearestEnemy != null)
			{
				progress   = 502;
				_bestEnemy = nearestEnemy;
				if (!selPawn.RaceProps.Humanlike || bodySize > 2.0f)
				{
					progress = 511;
					if (bestEnemyVisibleNow && selPawn.mindState.enemyTarget == null)
					{
						selPawn.mindState.enemyTarget = nearestEnemy;
					}
				}
				else
				{
					progress = 521;
					int                         shootingNum      = 0;
					int                         rangedNum        = 0;
					IEnumerator<AIEnvAgentInfo> enumeratorAllies = data.Allies();
					while (enumeratorAllies.MoveNext())
					{
						AIEnvAgentInfo info = enumeratorAllies.Current;
						if (info.thing is Pawn ally && DamageUtility.GetDamageReport(ally).primaryIsRanged)
						{
							rangedNum++;
							if (ally.stances?.curStance is Stance_Warmup)
							{
								shootingNum++;
							}
						}
					}
					progress = 522;
					float distOffset = Mathf.Clamp(2.0f * shootingNum - rangedEnemiesTargetingSelf.Count, 0, 25);
					float moveBias   = Mathf.Clamp01(2f * shootingNum / (rangedNum + 1f) * personality.group);
					if (Finder.Settings.Debug_LogJobs && distOffset > 0)
					{
						selPawn.Map.debugDrawer.FlashCell(selPos, distOffset / 20f, $"{distOffset}");
					}
					if (moveBias <= 0.5f)
					{
						moveBias = 0f;
					}
					if (duty.Is(CombatAI_DutyDefOf.CombatAI_AssaultPoint) && Rand.Chance(1 - moveBias))
					{
						return;
					}
					if (bestEnemyVisibleNow)
					{
						progress = 523;
						if (nearestEnemyDist > 6 * personality.cover)
						{
							CastPositionRequest request = new CastPositionRequest();
							request.caster              = selPawn;
							request.target              = nearestEnemy;
							request.maxRangeFromTarget  = 9999;
							request.verb                = verb;
							request.maxRangeFromCaster  = (Maths.Max(Maths.Min(verb.EffectiveRange, nearestEnemyDist) / 2f, 10f) * personality.cover + distOffset) * Finder.P50;
							request.wantCoverFromTarget = true;
							if (CastPositionFinder.TryFindCastPosition(request, out IntVec3 cell) && cell != selPos && (ShouldMoveTo(cell) || Rand.Chance(moveBias)))
							{
								StartOrQueueCoverJob(cell, 0);
							}
							else if (ShouldShootNow())
							{
								_last = 52;
								Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 100 + 100);
								job_waitCombat.playerForced                   = forcedTarget.IsValid;
								job_waitCombat.endIfCantShootTargetFromCurPos = true;
								selPawn.jobs.ClearQueuedJobs();
								selPawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
								data.LastInterrupted = GenTicks.TicksGame;
							}
						}
						else if (ShouldShootNow())
						{
							_last = 53;
							Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 100 + 100);
							job_waitCombat.playerForced                   = forcedTarget.IsValid;
							job_waitCombat.endIfCantShootTargetFromCurPos = true;
							selPawn.jobs.ClearQueuedJobs();
							selPawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
							data.LastInterrupted = GenTicks.TicksGame;
						}
					}
					// best enemy is approaching but not yet in view
					else if (bestEnemyVisibleSoon)
					{
						progress = 524;
						_last    = 60;
						CoverPositionRequest request = new CoverPositionRequest();
						request.caster = selPawn;
						request.verb   = verb;
						request.target = nearestEnemy;
						if (!Finder.Performance.TpsCriticallyLow)
						{
							request.majorThreats = new List<Thing>();
							for (int i = 0; i < rangedEnemiesTargetingSelf.Count; i++)
							{
								request.majorThreats.Add(rangedEnemiesTargetingSelf[Rand.Int % rangedEnemiesTargetingSelf.Count]);
							}
							request.maxRangeFromCaster = Maths.Min(verb.EffectiveRange, 10f) + distOffset;
						}
						else
						{
							request.maxRangeFromCaster = Maths.Max(verb.EffectiveRange, 10f);
						}
						request.maxRangeFromCaster *= personality.cover;
						request.checkBlockChance   =  true;
						if (CoverPositionFinder.TryFindCoverPosition(request, out IntVec3 cell))
						{
							if (ShouldMoveTo(cell) || Rand.Chance(moveBias))
							{
								StartOrQueueCoverJob(cell, 10);
							}
							else if (nearestEnemy is Pawn enemyPawn)
							{
								_last = 71;
								// fallback
								request.target             = PawnPathUtility.GetMovingShiftedPosition(enemyPawn, 90);
								request.maxRangeFromCaster = Mathf.Min(request.maxRangeFromCaster, 5) + distOffset;
								if (verb.CanHitFromCellIgnoringRange(selPos, request.target, out _) && CoverPositionFinder.TryFindCoverPosition(request, out cell) && (ShouldMoveTo(cell) || Rand.Chance(moveBias)))
								{
									StartOrQueueCoverJob(cell, 20);
								}
							}
						}
					}
				}
			}
		}
		
		private void TryMeleeReaction(float possibleDmg, ref int progress)
		{
			float nearestEnemyDist = 1e6f;
			Thing nearestEnemy = null;
			// For debugging and logging.
			progress = 201;
			if ((selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Retreat) || selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover)) && (rangedEnemiesTargetingSelf.Count == 0 || possibleDmg < 2.5f))
			{
				_last = 30;
				selPawn.jobs.StopAll();
			}
			bool    bestEnemyIsRanged             = false;
			bool    bestEnemyIsMeleeAttackingAlly = false;
			// TODO create melee reactions.
			IEnumerator<AIEnvAgentInfo> enumeratorEnemies = data.EnemiesWhere(AIEnvAgentState.nearby);
			// For debugging and logging.
			progress = 202;
			while (enumeratorEnemies.MoveNext())
			{
				AIEnvAgentInfo info = enumeratorEnemies.Current;
#if DEBUG_REACTION
				if (info.thing == null)
				{
					Log.Error("Found null thing (2)");
					continue;
				}
#endif
				// For debugging and logging.
				progress = 203;
				if (info.thing.Spawned && selPawn.CanReach(info.thing, PathEndMode.Touch, Danger.Deadly))
				{
					Verb enemyVerb = info.thing.TryGetAttackVerb();
					if (enemyVerb?.IsMeleeAttack == true && info.thing is Pawn enemyPawn && enemyPawn.CurJob.Is(JobDefOf.AttackMelee) && enemyPawn.CurJob.targetA.Thing?.TryGetAttackVerb()?.IsMeleeAttack == false)
					{
						if (!bestEnemyIsMeleeAttackingAlly)
						{
							bestEnemyIsMeleeAttackingAlly = true;
							nearestEnemyDist              = 1e5f;
							nearestEnemy                  = null;
						}
						float dist = selPawn.DistanceTo_Fast(info.thing);
						if (dist < nearestEnemyDist)
						{
							nearestEnemyDist = dist;
							nearestEnemy     = info.thing;
						}
					}
					else if (!bestEnemyIsMeleeAttackingAlly)
					{
						if (enemyVerb?.IsMeleeAttack == false)
						{
							if (!bestEnemyIsRanged)
							{
								bestEnemyIsRanged = true;
								nearestEnemyDist  = 1e5f;
								nearestEnemy      = null;
							}
							float dist = selPawn.DistanceTo_Fast(info.thing);
							if (dist < nearestEnemyDist)
							{
								nearestEnemyDist = dist;
								nearestEnemy     = info.thing;
							}
						}
						else if (!bestEnemyIsRanged)
						{
							float dist = selPawn.DistanceTo_Fast(info.thing);
							if (dist < nearestEnemyDist)
							{
								nearestEnemyDist = dist;
								nearestEnemy     = info.thing;
							}
						}
					}
				}
				// For debugging and logging.
				progress = 204;
			}
			// For debugging and logging.
			progress = 205;
			if (nearestEnemy == null)
			{
				nearestEnemy = selPawn.mindState.enemyTarget;
			}
			if (nearestEnemy == null || selPawn.CurJob.Is(JobDefOf.AttackMelee) && selPawn.CurJob.targetA.Thing == nearestEnemy)
			{
				return;
			}
			// For debugging and logging.
			progress   = 206;
			_bestEnemy = nearestEnemy;
			if (!selPawn.mindState.MeleeThreatStillThreat || selPawn.stances?.stagger?.Staggered == false)
			{
				_last = 31;
				Job job_melee = JobMaker.MakeJob(JobDefOf.AttackMelee, nearestEnemy);
				job_melee.playerForced      = forcedTarget.IsValid;
				job_melee.locomotionUrgency = LocomotionUrgency.Jog;
				selPawn.jobs.ClearQueuedJobs();
				selPawn.jobs.StartJob(job_melee, JobCondition.InterruptForced);
				data.LastInterrupted = GenTicks.TicksGame;
			}
			// For debugging and logging.
			progress = 207;
		}

		private void ReactDebug_Internel(out int progress)
		{
			// For debugging and logging.
			progress = 2;
#if DEBUG_REACTION
			if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight)
			{
				_visibleEnemies.Clear();
				IEnumerator<AIEnvAgentInfo> enumerator = data.Enemies();
				while (enumerator.MoveNext())
				{
					AIEnvAgentInfo info = enumerator.Current;
					if (info.thing == null)
					{
						Log.Warning("Found null thing (1)");
						continue;
					}
					if (info.thing.Spawned && info.thing is Pawn pawn)
					{
						_visibleEnemies.Add(pawn);
					}
				}
				// For debugging and logging.
				progress = 21;
				if (_path.Count == 0 || _path.Last() != parent.Position)
				{
					_path.Add(parent.Position);
					if (GenTicks.TicksGame - lastInterupted < 150)
					{
						_colors.Add(Color.red);
					}
					else if (GenTicks.TicksGame - lastInterupted < 240)
					{
						_colors.Add(Color.yellow);
					}
					else
					{
						_colors.Add(Color.black);
					}
					if (_path.Count >= 30)
					{
						_path.RemoveAt(0);
						_colors.RemoveAt(0);
					}
				}
				// For debugging and logging.
				progress = 22;
			}
#endif
		}
		
		private bool TryStartRetreat(float possibleDmg, float possibleDmgWarmup, float possibleDmgDistance, PersonalityTacker.PersonalityResult personality, Pawn nearestMeleeEnemy, float nearestMeleeEnemyDist, ref int progress)
		{
			IntVec3 selPos = selPawn.Position;
			if (rangedEnemiesTargetingSelf.Count > 0 && nearestMeleeEnemyDist > 2)
			{
				float retreatRoll = 15 + Rand.Range(0, 15 * rangedEnemiesTargetingSelf.Count) + data.NumAllies * 15;
				if (Finder.Settings.Debug_LogJobs)
				{
					MoteMaker.ThrowText(selPawn.DrawPos, selPawn.Map, $"r:{Math.Round(retreatRoll)},d:{possibleDmg}", Color.white);
				}
				// For debugging and logging.
				progress = 91;
				// major retreat attempt if the pawn is doomed
				if (possibleDmg * personality.retreat - retreatRoll > 0.001f && possibleDmg * personality.retreat >= 50)
				{
					_last      = 10;
					_bestEnemy = nearestMeleeEnemy;
					CoverPositionRequest request = new CoverPositionRequest();
					request.caster             = selPawn;
					request.target             = nearestMeleeEnemy;
					request.majorThreats       = rangedEnemiesTargetingSelf;
					request.maxRangeFromCaster = 12;
					request.checkBlockChance   = true;
					if (CoverPositionFinder.TryFindRetreatPosition(request, out IntVec3 cell))
					{
						// For debugging and logging.
						progress = 911;
						if (ShouldMoveTo(cell))
						{
							if (cell != selPos)
							{
								_last = 11;
								Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Retreat, cell);
								job_goto.playerForced          = forcedTarget.IsValid;
								job_goto.checkOverrideOnExpire = false;
								job_goto.expiryInterval        = -1;
								job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
								selPawn.jobs.ClearQueuedJobs();
								selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
								data.LastRetreated = GenTicks.TicksGame;
								if (Rand.Chance(0.5f) && !Finder.Settings.Debug_LogJobs)
								{
									MoteMaker.ThrowText(selPawn.DrawPos, selPawn.Map, "Cover me!", Color.white);
								}
							}
							return true;
						}
						if (Finder.Settings.Debug_LogJobs)
						{
							MoteMaker.ThrowText(selPawn.DrawPos, selPawn.Map, "retreat skipped", Color.white);
						}
					}
				}
				// For debugging and logging.
				progress = 92;
				// try minor retreat (duck for cover fast)
				if (possibleDmg * personality.duck - retreatRoll * 0.5f > 0.001f && possibleDmg * personality.duck >= 30)
				{
					// selPawn.Map.debugDrawer.FlashCell(selPos, 1.0f, $"{possibleDmg}, {targetedBy.Count}, {rangedEnemiesTargetingSelf.Count}");
					CoverPositionRequest request = new CoverPositionRequest();
					request.caster             = selPawn;
					request.majorThreats       = rangedEnemiesTargetingSelf;
					request.checkBlockChance   = true;
					request.maxRangeFromCaster = Mathf.Clamp(possibleDmgWarmup * 5f - rangedEnemiesTargetingSelf.Count, 4f, 8f);
					if (CoverPositionFinder.TryFindDuckPosition(request, out IntVec3 cell))
					{
						bool diff = cell != selPos;
						// run to cover
						if (diff)
						{
							_last = 12;
							Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Duck, cell);
							job_goto.playerForced          = forcedTarget.IsValid;
							job_goto.checkOverrideOnExpire = false;
							job_goto.expiryInterval        = -1;
							job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
							selPawn.jobs.ClearQueuedJobs();
							selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
							data.LastRetreated = lastRetreated = GenTicks.TicksGame;
						}
						if (data.TookDamageRecently(45) || !diff)
						{
							_last = 13;
							Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 50 + 50);
							job_waitCombat.playerForced          = forcedTarget.IsValid;
							job_waitCombat.checkOverrideOnExpire = true;
							selPawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
							data.LastRetreated = lastRetreated = GenTicks.TicksGame;
						}
						if (Rand.Chance(0.5f) && !Finder.Settings.Debug_LogJobs)
						{
							MoteMaker.ThrowText(selPawn.DrawPos, selPawn.Map, "Finding cover!", Color.white);
						}
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		///     Returns whether parent pawn should move to a new position.
		/// </summary>
		/// <param name="newPos">New position</param>
		/// <returns>Whether to move or not</returns>
		private bool ShouldMoveTo(IntVec3 newPos)
		{
			IntVec3 pos = selPawn.Position;
			if (pos == newPos)
			{
				return true;
			}
			float curVisibility = sightReader.GetVisibilityToEnemies(pos);
			float curThreat     = sightReader.GetVisibilityToEnemies(pos);
			Job   job           = selPawn.CurJob;
			if (curThreat == 0 && curVisibility == 0 && !(job.Is(JobDefOf.Wait_Combat) || job.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) || job.Is(CombatAI_JobDefOf.CombatAI_Goto_Duck) || job.Is(CombatAI_JobDefOf.CombatAI_Goto_Retreat)))
			{
				return sightReader.GetVisibilityToEnemies(newPos) <= 2f && sightReader.GetThreat(newPos) < 1f;
			}
			float visDiff    = curVisibility - sightReader.GetVisibilityToEnemies(newPos);
			float magDiff    = Maths.Sqrt_Fast(sightReader.GetEnemyDirection(pos).sqrMagnitude, 4) - Maths.Sqrt_Fast(sightReader.GetEnemyDirection(newPos).sqrMagnitude, 4);
			float threatDiff = curThreat - sightReader.GetThreat(newPos);
			return Rand.Chance(visDiff) && Rand.Chance(threatDiff) && Rand.Chance(magDiff);
		}

		/// <summary>
		///     Whether the pawn should start shooting now.
		/// </summary>
		/// <returns></returns>
		private bool ShouldShootNow()
		{
			return !selPawn.CurJob.Is(JobDefOf.Wait_Combat) && (!selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) && !selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Duck) || !ShouldMoveTo(selPawn.CurJob.targetA.Cell));
		}

		/// <summary>
		///     Called When the parent takes damage.
		/// </summary>
		/// <param name="dInfo">Damage info</param>
		public void Notify_TookDamage(DamageInfo dInfo)
		{
			// notify the custom duty manager that this pawn took damage.
			if (duties != null)
			{
				duties.Notify_TookDamage();
			}
			data.LastTookDamage = lastTookDamage = GenTicks.TicksGame;
			if (dInfo.Instigator != null && data.NumAllies != 0 && dInfo.Instigator.HostileTo(selPawn))
			{
				StartAggroCountdown(dInfo.Instigator);
			}
		}

		/// <summary>
		///     Called when a bullet impacts nearby.
		/// </summary>
		/// <param name="instigator">Attacker</param>
		/// <param name="cell">Impact position</param>
		public void Notify_BulletImpact(Thing instigator, IntVec3 cell)
		{
			if (instigator == null)
			{
				StartAggroCountdown(new LocalTargetInfo(cell));
			}
			else
			{
				StartAggroCountdown(new LocalTargetInfo(instigator));
			}
		}
		/// <summary>
		///     Start aggro countdown.
		/// </summary>
		/// <param name="enemy">Enemy.</param>
		public void StartAggroCountdown(LocalTargetInfo enemy)
		{
			aggroTarget = enemy;
			aggroTicks  = Rand.Range(30, 90);
		}

		/// <summary>
		///     Switch the pawn to an aggro mode and their allies around them.
		/// </summary>
		/// <param name="enemy">Attacker</param>
		/// <param name="aggroAllyChance">Chance to aggro nearbyAllies</param>
		/// <param name="sig">Aggro sig</param>
		private void TryAggro(LocalTargetInfo enemy, float aggroAllyChance, int sig)
		{
			if (selPawn.mindState.duty.Is(DutyDefOf.Defend) && data.AgroSig != sig)
			{
				Pawn_CustomDutyTracker.CustomPawnDuty custom = CustomDutyUtility.HuntDownEnemies(enemy.Cell, Rand.Int % 1200 + 2400);
				if (selPawn.TryStartCustomDuty(custom))
				{
					data.AgroSig = sig;
					// aggro nearby Allies
					IEnumerator<AIEnvAgentInfo> allies = data.AlliesNearBy();
					while (allies.MoveNext())
					{
						AIEnvAgentInfo ally = allies.Current;
						// make allies not targeting anyone target the attacking enemy
						if (Rand.Chance(aggroAllyChance) && ally.thing is Pawn { Destroyed: false, Spawned: true, Downed: false } other && other.mindState.duty.Is(DutyDefOf.Defend))
						{
							ThingComp_CombatAI comp = other.AI();
							if (comp != null && comp.data.AgroSig != sig)
							{
								if (enemy.HasThing)
								{
									other.mindState.enemyTarget ??= enemy.Thing;
								}
								comp.TryAggro(enemy, aggroAllyChance / 2f, sig);
							}
						}
					}
				}
			}
		}

		/// <summary>
		///     Start a sapping task.
		/// </summary>
		/// <param name="blocked">Blocked cells</param>
		/// <param name="cellBefore">Cell before blocked cells</param>
		/// <param name="findEscorts">Whether to look for escorts</param>
		public void StartSapper(List<IntVec3> blocked, IntVec3 cellBefore, IntVec3 cellAhead, bool findEscorts)
		{
			if (cellBefore.IsValid && sapperNodes.Count > 0 && GenTicks.TicksGame - sapperStartTick < 4800)
			{
				ReleaseEscorts(false);
			}
			this.cellBefore  = cellBefore;
			this.cellAhead   = cellAhead;
			this.findEscorts = findEscorts;
			sapperStartTick  = GenTicks.TicksGame;
			sapperNodes.Clear();
			sapperNodes.AddRange(blocked);
			_sap = 0;
//			TryStartSapperJob();
		}

		/// <summary>
		///     Returns debug gizmos.
		/// </summary>
		/// <returns></returns>
		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			if (Finder.Settings.Debug && Finder.Settings.Debug_LogJobs)
			{
				Command_Action jobs = new Command_Action();
				jobs.defaultLabel = "DEV: View job logs";
				jobs.action = delegate
				{
					if (Find.WindowStack.windows.Any(w => w is Window_JobLogs logs && logs.comp == this))
					{
						return;
					}
					jobLogs ??= new List<JobLog>();
					Window_JobLogs window = new Window_JobLogs(this);
					Find.WindowStack.Add(window);
				};
				yield return jobs;
			}
			if (Prefs.DevMode && DebugSettings.godMode)
			{
				if ((selPawn.mindState.duty.Is(DutyDefOf.Escort) || selPawn.mindState.duty.Is(CombatAI_DutyDefOf.CombatAI_Escort)) && selPawn.mindState.duty.focus.IsValid)
				{
					Command_Action escort = new Command_Action();
					escort.defaultLabel = "DEV: Flash escort area";
					escort.action = delegate
					{
						Pawn  focus  = selPawn.mindState.duty.focus.Thing as Pawn;
						Map   map    = focus.Map;
						float radius = selPawn.mindState.duty.radius;
						map.debugDrawer.FlashCell(focus.Position, 1, "XXXXXXX");
						foreach (IntVec3 cell in GenRadial.RadialCellsAround(focus.Position, 0, 20))
						{
							if (JobGiver_CAIFollowEscortee.NearFollowee(selPawn, focus, cell, radius, out _))
							{
								map.debugDrawer.FlashCell(cell, 0.9f, $"{cell.HeuristicDistanceTo(focus.Position, map)}");
							}
							else
							{
								map.debugDrawer.FlashCell(cell, 0.01f, $"{cell.HeuristicDistanceTo(focus.Position, map)}");
							}
						}
					};
					yield return escort;
				}
				Verb           verb           = selPawn.TryGetAttackVerb();
				float          retreatDistSqr = Maths.Max(verb.EffectiveRange * verb.EffectiveRange / 9, 36);
				Map            map            = selPawn.Map;
				Command_Action retreat        = new Command_Action();
				retreat.defaultLabel = "DEV: Retreat position search";
				retreat.action = delegate
				{
					CoverPositionRequest request = new CoverPositionRequest();
					if (_bestEnemy != null)
					{
						request.target = new LocalTargetInfo(_bestEnemy.Position);
					}
					request.caster             = selPawn;
					request.verb               = verb;
					request.maxRangeFromCaster = Maths.Min(Mathf.Max(retreatDistSqr * 2 / (selPawn.BodySize + 0.01f), 5), 15);
					request.checkBlockChance   = true;
					CoverPositionFinder.TryFindRetreatPosition(request, out IntVec3 cell, (cell, val) => map.debugDrawer.FlashCell(cell, Mathf.Clamp((val + 15f) / 30f, 0.01f, 0.99f), $"{Math.Round(val, 3)}"));
					if (cell.IsValid)
					{
						map.debugDrawer.FlashCell(cell, 1, "XXXXXXX", 150);
					}
				};
				Command_Action duck = new Command_Action();
				duck.defaultLabel = "DEV: Duck position search";
				duck.action = delegate
				{
					CoverPositionRequest request = new CoverPositionRequest();
					request.majorThreats       = data.BeingTargetedBy;
					request.caster             = selPawn;
					request.verb               = verb;
					request.maxRangeFromCaster = 5;
					request.checkBlockChance   = true;
					CoverPositionFinder.TryFindDuckPosition(request, out IntVec3 cell, (cell, val) => map.debugDrawer.FlashCell(cell, Mathf.Clamp((val + 15f) / 30f, 0.01f, 0.99f), $"{Math.Round(val, 3)}"));
					if (cell.IsValid)
					{
						map.debugDrawer.FlashCell(cell, 1, "XXXXXXX", 150);
					}
				};
				Command_Action cover = new Command_Action();
				cover.defaultLabel = "DEV: Cover position search";
				cover.action = delegate
				{
					CoverPositionRequest request = new CoverPositionRequest();
					if (_bestEnemy != null)
					{
						request.target = new LocalTargetInfo(_bestEnemy.Position);
					}
					request.caster             = selPawn;
					request.verb               = verb;
					request.maxRangeFromCaster = Mathf.Clamp(selPawn.GetStatValue_Fast(StatDefOf.MoveSpeed, 60) * 3 / (selPawn.BodySize + 0.01f), 4, 15);
					request.checkBlockChance   = true;
					CoverPositionFinder.TryFindCoverPosition(request, out IntVec3 cell, (cell, val) => map.debugDrawer.FlashCell(cell, Mathf.Clamp((val + 15f) / 30f, 0.01f, 0.99f), $"{Math.Round(val, 3)}"));
					if (cell.IsValid)
					{
						map.debugDrawer.FlashCell(cell, 1, "XXXXXXX", 150);
					}
				};
				Command_Action cast = new Command_Action();
				cast.defaultLabel = "DEV: Cast position search";
				cast.action = delegate
				{
					if (_bestEnemy == null)
					{
						return;
					}
					CastPositionRequest request = new CastPositionRequest();
					request.caster              = selPawn;
					request.target              = _bestEnemy;
					request.verb                = verb;
					request.maxRangeFromTarget  = 9999;
					request.maxRangeFromCaster  = Mathf.Clamp(selPawn.GetStatValue_Fast(StatDefOf.MoveSpeed, 60) * 3 / (selPawn.BodySize + 0.01f), 4, 15);
					request.wantCoverFromTarget = true;
					try
					{
						DebugViewSettings.drawCastPositionSearch = true;
						CastPositionFinder.TryFindCastPosition(request, out IntVec3 cell);
						if (cell.IsValid)
						{
							map.debugDrawer.FlashCell(cell, 1, "XXXXXXX", 150);
						}
					}
					catch (Exception er)
					{
						Log.Error(er.ToString());
					}
					finally
					{
						DebugViewSettings.drawCastPositionSearch = false;
					}
				};
				yield return retreat;
				yield return duck;
				yield return cover;
				yield return cast;
			}
			if (selPawn.IsColonist)
			{
				Command_Target attackMove = new Command_Target();
				attackMove.defaultLabel                       = Keyed.CombatAI_Gizmos_AttackMove;
				attackMove.targetingParams                    = new TargetingParameters();
				attackMove.targetingParams.canTargetPawns     = true;
				attackMove.targetingParams.canTargetLocations = true;
				attackMove.targetingParams.canTargetSelf      = false;
				attackMove.targetingParams.validator = target =>
				{
					if (!target.IsValid || !target.Cell.InBounds(selPawn.Map))
					{
						return false;
					}
					foreach (Pawn pawn in Find.Selector.SelectedPawns)
					{
						if (pawn == null)
						{
							continue;
						}
						if (pawn.CanReach(target.Cell, PathEndMode.OnCell, Danger.Deadly))
						{
							return true;
						}
					}
					return false;
				};
				attackMove.icon       = Tex.Isma_Gizmos_move_attack;
				attackMove.groupable  = true;
				attackMove.shrinkable = false;
				attackMove.action = target =>
				{
					foreach (Pawn pawn in Find.Selector.SelectedPawns)
					{
						if (pawn.IsColonist && pawn.drafter != null)
						{
							if (!pawn.CanReach(target.Cell, PathEndMode.OnCell, Danger.Deadly))
							{
								continue;
							}
							if (!pawn.Drafted)
							{
								if (!pawn.drafter.ShowDraftGizmo)
								{
									continue;
								}
								DevelopmentalStage stage = pawn.DevelopmentalStage;
								if (stage <= DevelopmentalStage.Child && stage != DevelopmentalStage.None)
								{
									continue;
								}
								pawn.drafter.Drafted = true;
							}
							if (pawn.CurrentEffectiveVerb?.IsMeleeAttack ?? true)
							{
								Messages.Message(Keyed.CombatAI_Gizmos_AttackMove_Warning, MessageTypeDefOf.RejectInput, false);
								continue;
							}
							pawn.AI().forcedTarget = target;
							Job gotoJob = JobMaker.MakeJob(JobDefOf.Goto, target);
							gotoJob.canUseRangedWeapon = true;
							gotoJob.locomotionUrgency  = LocomotionUrgency.Jog;
							gotoJob.playerForced       = true;
							pawn.jobs.ClearQueuedJobs();
							pawn.jobs.StartJob(gotoJob);
						}
					}
				};
				yield return attackMove;
				if (forcedTarget.IsValid)
				{
					Command_Action cancelAttackMove = new Command_Action();
					cancelAttackMove.defaultLabel = Keyed.CombatAI_Gizmos_AttackMove_Cancel;
					cancelAttackMove.groupable    = true;
					//
					// cancelAttackMove.disabled     = forcedTarget.IsValid;
					cancelAttackMove.action = () =>
					{
						foreach (Pawn pawn in Find.Selector.SelectedPawns)
						{
							if (pawn.IsColonist)
							{
								pawn.AI().forcedTarget = LocalTargetInfo.Invalid;
								pawn.jobs.ClearQueuedJobs();
								pawn.jobs.StopAll();
							}
						}
					};
				}
			}
		}

		/// <summary>
		///     Release escorts pawns.
		/// </summary>
		public void ReleaseEscorts(bool success)
		{
			for (int i = 0; i < escorts.Count; i++)
			{
				Pawn escort = escorts[i];
				if (escort == null || escort.Destroyed || escort.Dead || escort.Downed || escort.mindState.duty == null)
				{
					continue;
				}
				if (escort.mindState.duty.focus == parent)
				{
					if (success)
					{
						escort.AI().releasedTick = GenTicks.TicksGame;
					}
					escort.AI().duties.FinishAllDuties(CombatAI_DutyDefOf.CombatAI_Escort, parent);
				}
			}
			if (success)
			{
				Predicate<Thing> validator = t =>
				{
					if (!t.HostileTo(selPawn))
					{
						ThingComp_CombatAI comp = t.GetComp_Fast<ThingComp_CombatAI>();
						if (comp != null && comp.IsSapping && comp.sapperNodes.Count > 3)
						{
							ReleaseEscorts(false);
							comp.cellBefore      = IntVec3.Invalid;
							comp.sapperStartTick = GenTicks.TicksGame + 800;
							comp.sapperNodes.Clear();
						}
					}
					return false;
				};
				Verse.GenClosest.RegionwiseBFSWorker(selPawn.Position, selPawn.Map, ThingRequest.ForGroup(ThingRequestGroup.Pawn), PathEndMode.InteractionCell, TraverseParms.For(selPawn), validator, null, 1, 4, 15, out int _);
			}
			escorts.Clear();
		}


		/// <summary>
		///     Add enemy targeting self to Env data.
		/// </summary>
		/// <param name="enemy"></param>
		/// <param name="ticksToBurst"></param>
		public void Notify_BeingTargeted(Thing enemy, Verb verb)
		{
			if (enemy != null && !enemy.Destroyed)
			{
				data.BeingTargeted(enemy);
				if (Rand.Chance(0.15f) && (selPawn.mindState.duty.Is(DutyDefOf.Defend) || selPawn.mindState.duty.Is(DutyDefOf.Escort)))
				{
					StartAggroCountdown(enemy);
				}
			}
			else
			{
				Log.Error($"{selPawn} received a null thing in Notify_BeingTargeted");
			}
		}

		/// <summary>
		///     Enqueue enemy for reaction processing.
		/// </summary>
		/// <param name="things">Spotted enemy</param>
		public void Notify_Enemy(AIEnvAgentInfo info)
		{
			if (!scanning)
			{
				Log.Warning($"ISMA: Notify_EnemiesVisible called while not scanning. ({allEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
				return;
			}
			if (info.thing is Pawn enemy)
			{
				// skip if the enemy is downed
				if (enemy.Downed)
				{
					return;
				}
				// skip for children
				DevelopmentalStage stage = enemy.DevelopmentalStage;
				if (stage <= DevelopmentalStage.Child && stage != DevelopmentalStage.None)
				{
					return;
				}
			}
			if (allEnemies.TryGetValue(info.thing, out AIEnvAgentInfo store))
			{
				info = store.Combine(info);
			}
			allEnemies[info.thing] = info;
		}

		/// <summary>
		///     Enqueue ally for reaction processing.
		/// </summary>
		/// <param name="things">Spotted enemy</param>
		public void Notify_Ally(AIEnvAgentInfo info)
		{
			if (!scanning)
			{
				Log.Warning($"ISMA: Notify_EnemiesVisible called while not scanning. ({allEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
				return;
			}
			if (info.thing is Pawn ally)
			{
				// skip if the ally is downed
				if (ally.Downed)
				{
					return;
				}
				// skip for children
				DevelopmentalStage stage = ally.DevelopmentalStage;
				if (stage <= DevelopmentalStage.Child && stage != DevelopmentalStage.None)
				{
					return;
				}
			}
			if (allAllies.TryGetValue(info.thing, out AIEnvAgentInfo store))
			{
				info = store.Combine(info);
			}
			allAllies[info.thing] = info;
		}

		/// <summary>
		///     Called to notify a wait job started by reaction has ended. Will reduce the reaction cooldown.
		/// </summary>
		public void Notify_WaitJobEnded()
		{
			lastInterupted -= 30;
		}

		/// <summary>
		///     Called when the parent sightreader group has changed.
		///     Should only be called from SighTracker/SightGrid.
		/// </summary>
		/// <param name="reader">The new sightReader</param>
		public void Notify_SightReaderChanged(SightTracker.SightReader reader)
		{
			sightReader = reader;
		}

		public override void PostExposeData()
		{
			base.PostExposeData();
			if (Finder.Settings.Debug)
			{
				PersonalityTacker.PersonalityResult personality = parent.GetCombatPersonality();
				Scribe_Deep.Look(ref personality, "personality");
			}
			Scribe_Deep.Look(ref data, "AIAgentData.0");
			data ??= new AIAgentData();
			Scribe_Deep.Look(ref duties, "duties2");
			Scribe_Deep.Look(ref abilities, "abilities2");
			Scribe_TargetInfo.Look(ref forcedTarget, "forcedTarget");
			if (duties == null)
			{
				duties = new Pawn_CustomDutyTracker(selPawn);
			}
			if (abilities == null)
			{
				abilities = new Pawn_AbilityCaster(selPawn);
			}
			duties.pawn    = selPawn;
			abilities.pawn = selPawn;
		}

		private void TryStartSapperJob()
		{
			bool failed = sapperNodes.Count == 0 || (sightReader.GetVisibilityToFriendlies(cellAhead) > 0 && GenTicks.TicksGame - sapperStartTick > 1000);
			if (failed)
			{
				ReleaseEscorts(false);
				cellBefore      = IntVec3.Invalid;
				sapperStartTick = -1;
				sapperNodes.Clear();
				return;
			}
			if (IsDeadOrDowned || !(selPawn.mindState.duty.Is(DutyDefOf.AssaultColony) || selPawn.mindState.duty.Is(CombatAI_DutyDefOf.CombatAI_AssaultPoint) || selPawn.mindState.duty.Is(DutyDefOf.AssaultThing)) || selPawn.CurJob.Is(JobDefOf.Wait_Combat) || selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) || selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Retreat)  || selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Duck))
			{
				ReleaseEscorts(false);
				cellBefore      = IntVec3.Invalid;
				sapperStartTick = -1;
				sapperNodes.Clear();
				return;
			}
			Map   map     = selPawn.Map;
			Thing blocker = sapperNodes[0].GetEdifice(map);
			if (blocker != null)
			{
				Job                                 job         = null;
				float                               miningSkill = selPawn.GetSkillLevelSafe(SkillDefOf.Mining, 0);
				PersonalityTacker.PersonalityResult personality = parent.GetCombatPersonality();
				if (findEscorts && Rand.Chance(personality.sapping - Maths.Min(escorts.Count  / (Maths.Max(miningSkill, 7)), 0.85f)))
				{
					int     count       = escorts.Count;
					int     countTarget = 7 + Mathf.FloorToInt(Maths.Max(miningSkill, 7)) + Maths.Min(sapperNodes.Count, 10);
					Faction faction     = selPawn.Faction;
					Predicate<Thing> validator = t =>
					{
						if (count < countTarget && t.Faction == faction && t is Pawn ally && !ally.Destroyed && !ally.CurJobDef.Is(JobDefOf.Mine) && !ally.IsColonist && ally.def != CombatAI_ThingDefOf.Mech_Tunneler && ally.mindState?.duty?.def != CombatAI_DutyDefOf.CombatAI_Escort 
						    && (sightReader == null || sightReader.GetAbsVisibilityToEnemies(ally.Position) == 0) 
						    && ally.GetSkillLevelSafe(SkillDefOf.Mining, 0) < miningSkill)
						{
							ThingComp_CombatAI comp = ally.AI();
							if (comp?.duties != null && comp.duties?.Any(CombatAI_DutyDefOf.CombatAI_Escort) == false && !comp.IsSapping && GenTicks.TicksGame - comp.releasedTick > 300)
							{
								Pawn_CustomDutyTracker.CustomPawnDuty custom = CustomDutyUtility.Escort(selPawn, 20, 100, (int)(personality.sapping * 4800) + Mathf.CeilToInt(12 * selPawn.Position.DistanceTo(cellBefore)) + 540 * sapperNodes.Count + Rand.Int % 600);
								if (ally.TryStartCustomDuty(custom))
								{
									escorts.Add(ally);
								}
								if (comp.duties.curCustomDuty?.duty != duties.curCustomDuty?.duty)
								{
									count += 4;
								}
								else
								{
									count++;
								}
							}
							return count == countTarget;
						}
						return false;
					};
					Verse.GenClosest.RegionwiseBFSWorker(selPawn.Position, map, ThingRequest.ForGroup(ThingRequestGroup.Pawn), PathEndMode.InteractionCell, TraverseParms.For(selPawn), validator, null, 1, 10, 40, out int _);
				}
				float visibility = sightReader.GetAbsVisibilityToEnemies(cellBefore);
				if (!Mod_CE.active &&  (visibility > 0 || miningSkill < 9) && (escorts.Count >= 2 || miningSkill == 0))
				{
					job = TryStartRemoteSapper(selPawn, blocker, sapperNodes[0], cellBefore);
				}
				if (job == null)
				{
					// if no remote sapping job has been started, start a melee sapping job. If that's not possible release held escorts
					if (visibility <= 1e-2f && miningSkill > 0)
					{
						job                    = DigUtility.PassBlockerJob(selPawn, blocker, cellBefore, true, true);
						job.playerForced       = true;
						job.expiryInterval     = 3600;
						job.maxNumMeleeAttacks = 300;
						selPawn.jobs.StartJob(job, JobCondition.InterruptForced);
					}
					else
					{
						ReleaseEscorts(false);
						cellBefore      = IntVec3.Invalid;
						sapperStartTick = -1;
						sapperNodes.Clear();
					}
				}
				if (!Mod_CE.active && job != null && job.def == JobDefOf.UseVerbOnThing)
				{
					TryStartSapperJobForEscort(blocker);
				}
			}
		}

		/// <summary>
		/// Tries to start ranged sapping jobs for escorting pawns
		/// </summary>
		/// <param name="blocker">The path blocker</param>
		private void TryStartSapperJobForEscort(Thing blocker)
		{
			foreach (Pawn ally in escorts)
			{
				if (!ally.Destroyed && ally.Spawned && !ally.Downed && !ally.Dead && !ally.IsUsingVerb() && sightReader.GetAbsVisibilityToEnemies(ally.Position) == 0)
				{
					TryStartRemoteSapper(ally, blocker, sapperNodes[0], cellBefore);
				}
			}
		}
		
		/// <summary>
		/// Starts remote sapping for a pawn given a blocker 
		/// </summary>
		/// <param name="blocker"></param>
		/// <returns></returns>
		private static Job TryStartRemoteSapper(Pawn pawn, Thing blocker, IntVec3 cellBlocker, IntVec3 cellBefore)
		{
			Verb verb = pawn.TryGetAttackVerb();
			if (verb.IsRangedSappingCompatible() && (verb.verbProps.burstShotCount > 1 || !pawn.RaceProps.IsMechanoid))
			{
				CastPositionRequest request = new CastPositionRequest();
				request.verb               = verb;
				request.caster             = pawn;
				request.target             = blocker;
				request.maxRangeFromTarget = 10;
				Vector3 dir = (cellBefore - cellBlocker).ToVector3();
				request.validator = cell =>
				{
					return Mathf.Abs(Vector3.Angle((cell - cellBlocker).ToVector3(), dir)) <= 45f && cellBefore.DistanceToSquared(cell) >= 9;
				};
				try
				{
					if (CastPositionFinder.TryFindCastPosition(request, out IntVec3 loc))
					{
						Job job = JobMaker.MakeJob(JobDefOf.UseVerbOnThing);
						job.targetA             = blocker;
						job.targetB             = loc;
						job.verbToUse           = verb;
						job.preventFriendlyFire = true;
						job.expiryInterval      = JobGiver_AIFightEnemy.ExpiryInterval_ShooterSucceeded.RandomInRange;
						pawn.jobs.StartJob(job, JobCondition.InterruptForced);
						for (int i = 0; i < 4; i++)
						{
							job                     = JobMaker.MakeJob(JobDefOf.UseVerbOnThing);
							job.targetA             = blocker;
							job.targetB             = loc;
							job.verbToUse           = verb;
							job.preventFriendlyFire = true;
							job.expiryInterval      = JobGiver_AIFightEnemy.ExpiryInterval_ShooterSucceeded.RandomInRange;
							pawn.jobs.jobQueue.EnqueueFirst(job);
						}
						return job;
					}
				}
				catch (Exception er)
				{
					Log.Error($"1. {er}");
				}
			}
			return null;
		}

		#region TimeStamps

		/// <summary>
		///     When the last injury occured/damage.
		/// </summary>
		private int lastTookDamage;
		/// <summary>
		///     When the last scan occured. SightGrid is responisble for these scan cycles.
		/// </summary>
		private int lastScanned;
		/// <summary>
		///     When did this comp last interupt the parent pawn. IE: reacted, retreated, etc.
		/// </summary>
		private int lastInterupted;
		/// <summary>
		///     When the pawn was last order to retreat by CAI.
		/// </summary>
		private int lastRetreated;
		/// <summary>
		///     Last tick any enemies where reported in a scan.
		/// </summary>
		private int lastSawEnemies;
		/// <summary>
		///     The general direction of enemies last time the pawn reacted.
		/// </summary>
		private Vector2 prevEnemyDir = Vector2.zero;
		/// <summary>
		///     Tick when this pawn was released as an escort.
		/// </summary>
		private int releasedTick;

		#endregion

#if DEBUG_REACTION
        /*
         * Debug only vars.
         */

        public override void DrawGUIOverlay()
        {
            if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight && parent is Pawn pawn)
            {
                base.DrawGUIOverlay();
                Verb  verb = pawn.CurrentEffectiveVerb;
                float sightRange = Maths.Min(SightUtility.GetSightRadius(pawn).scan, !verb.IsMeleeAttack ? verb.EffectiveRange : 15);
                float sightRangeSqr = sightRange * sightRange;
                if (sightRange != 0 && verb != null)
                {
                    Vector3 drawPos = pawn.DrawPos;
                    IntVec3 shiftedPos = PawnPathUtility.GetMovingShiftedPosition(pawn, 30);
                    List<Pawn> nearbyVisiblePawns = pawn.Position.ThingsInRange(pawn.Map, TrackedThingsRequestCategory.Pawns, sightRange)
                        .Select(t => t as Pawn)
                        .Where(p => !p.Dead && !p.Downed && PawnPathUtility.GetMovingShiftedPosition(p, 60).DistanceToSquared(shiftedPos) < sightRangeSqr && verb.CanHitTargetFrom(shiftedPos, PawnPathUtility.GetMovingShiftedPosition(p, 60)) && p.HostileTo(pawn))
                        .ToList();
                    Gui.GUIUtility.ExecuteSafeGUIAction(() =>
                    {
                        Vector2 drawPosUI = drawPos.MapToUIPosition();
                        Text.Font = GameFont.Tiny;
                        string state = GenTicks.TicksGame - lastInterupted > 120 ? "<color=blue>O</color>" : "<color=yellow>X</color>";
                        Widgets.Label(new Rect(drawPosUI.x - 25, drawPosUI.y - 15, 50, 30), $"{state}/{_visibleEnemies.Count}:{_last}:{data.AllEnemies.Count}:{data.NumAllies}:{data.BeingTargetedBy.Count}");
                    });
                    bool    bugged = nearbyVisiblePawns.Count != _visibleEnemies.Count;
                    Vector2 a = drawPos.MapToUIPosition();
                    if (bugged)
                    {
                        Rect    rect;
                        Vector2 b;
                        Vector2 mid;
                        foreach (Pawn other in nearbyVisiblePawns.Where(p => !_visibleEnemies.Contains(p)))
                        {
                            b = other.DrawPos.MapToUIPosition();
                            Widgets.DrawLine(a, b, Color.red, 1);

                            mid = (a + b) / 2;
                            rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                            Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                            Widgets.DrawBox(rect);
                            Widgets.Label(rect, $"<color=red>Errored</color>.  {Math.Round(other.Position.DistanceTo(pawn.Position), 1)}");
                        }
                    }
                    bool selected = Find.Selector.SelectedPawns.Contains(pawn);
                    if (bugged || selected)
                    {
                        GenDraw.DrawRadiusRing(pawn.Position, sightRange);
                    }
                    if (selected)
                    {
                        for (int i = 1; i < _path.Count; i++)
                        {
                            Widgets.DrawBoxSolid(new Rect((_path[i - 1].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)).MapToUIPosition() - new Vector2(5, 5), new Vector2(10, 10)), _colors[i]);
                            Widgets.DrawLine((_path[i - 1].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)).MapToUIPosition(), (_path[i].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)).MapToUIPosition(), Color.white, 1);
                        }
                        if (_path.Count > 0)
                        {
                            Vector2 v = pawn.DrawPos.Yto0().MapToUIPosition();
                            Widgets.DrawLine((_path.Last().ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)).MapToUIPosition(), v, _colors.Last(), 1);
                            Widgets.DrawBoxSolid(new Rect(v - new Vector2(5, 5), new Vector2(10, 10)), _colors.Last());
                        }
//						int     index = 0;
                        foreach (AIEnvAgentInfo ally in data.AllAllies)
                        {
                            if (ally.thing != null)
                            {
                                Vector2 b = ally.thing.DrawPos.MapToUIPosition();
                                Widgets.DrawLine(a, b, Color.green, 1);

                                Vector2 mid = (a + b) / 2;
                                Rect    rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                                Widgets.DrawBox(rect);
                                DamageReport report = DamageUtility.GetDamageReport(ally.thing);
                                if (report.IsValid)
                                {
                                    Widgets.Label(rect, $"{Math.Round(report.SimulatedDamage(armor), 2)}");
                                }
                            }
                        }
                        AIEnvThings enemies = data.AllEnemies;
                        foreach (AIEnvAgentInfo enemy in enemies)
                        {
                            if (enemy.thing != null)
                            {
                                Vector2 b = enemy.thing.DrawPos.MapToUIPosition();
                                Widgets.DrawLine(a, b, Color.yellow, 1);

                                Vector2 mid = (a + b) / 2;
                                Rect    rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                                Widgets.DrawBox(rect);
                                DamageReport report = DamageUtility.GetDamageReport(enemy.thing);
                                if (report.IsValid)
                                {
                                    Widgets.Label(rect, $"{Math.Round(report.SimulatedDamage(armor), 2)}");
                                }
                            }
                        }
                        foreach (Thing enemy in data.BeingTargetedBy)
                        {
                            if (enemy != null && enemy.TryGetAttackVerb() is Verb enemyVerb && ThreatUtility.GetEnemyAttackTargetId(enemy) == selPawn.thingIDNumber)
                            {
                                Vector2 b = enemy.DrawPos.MapToUIPosition();
                                Ray2D   ray = new Ray2D(a, b - a);
                                float   dist = Vector2.Distance(a, b);
                                if (dist > 0)
                                {
                                    for (int i = 1; i < dist; i++)
                                    {
                                        Widgets.DrawLine(ray.GetPoint(i - 1), ray.GetPoint(i), i % 2 == 1 ? Color.black : Color.magenta, 2);
                                    }
                                    Vector2 mid = (a + b) / 2;
                                    Rect    rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                                    Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                                    Widgets.DrawBox(rect);
                                    DamageReport report = DamageUtility.GetDamageReport(enemy);
                                    if (report.IsValid)
                                    {
                                        Widgets.Label(rect, $"{Math.Round(report.SimulatedDamage(armor), 2)}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private readonly HashSet<Pawn> _visibleEnemies = new HashSet<Pawn>();
        private readonly List<IntVec3> _path = new List<IntVec3>();
        private readonly List<Color>   _colors = new List<Color>();
#endif
	}
}
