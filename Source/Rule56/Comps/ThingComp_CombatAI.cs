﻿using System;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;
using Verse.AI;
using System.Threading;
using System.Net.NetworkInformation;
using UnityEngine.Analytics;
using Unity.Baselib.LowLevel;

namespace CombatAI.Comps
{
    public class ThingComp_CombatAI : ThingComp
    {
        private HashSet<Pawn> _visibleEnemies = new HashSet<Pawn>();
        private List<IntVec3> _path = new List<IntVec3>();
        private List<Color> _colors = new List<Color>();

        private IntVec3 cellBefore;
        private List<IntVec3> miningCells = new List<IntVec3>(64);

        private Job moveJob;
		private int lastMoved;

		public int lastInterupted;
		public int lastRetreated;
        public int lastSawEnemies;

        private bool scanning;

        private HashSet<Thing> visibleEnemies;        

        public Pawn_CustomDutyTracker duties;
        public SightTracker.SightReader sightReader;

        public ThingComp_CombatAI()
        {
            this.visibleEnemies = new HashSet<Thing>();            
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (parent is Pawn pawn)
            {
                this.duties = new Pawn_CustomDutyTracker(pawn);
            }
        } 

#if DEBUG_REACTION

        public override void DrawGUIOverlay()
        {
            if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight && parent is Pawn pawn)
            {
                base.DrawGUIOverlay();
                var verb = pawn.CurrentEffectiveVerb;
                var sightRange = Maths.Min(SightUtility.GetSightRange(pawn), verb.EffectiveRange);
                var sightRangeSqr = sightRange * sightRange;
                if (sightRange != 0 && verb != null)
                {
                    Vector3 drawPos = pawn.DrawPos;
                    IntVec3 shiftedPos = pawn.GetMovingShiftedPosition(30);
                    List<Pawn> nearbyVisiblePawns = GenClosest.ThingsInRange(pawn.Position, pawn.Map, Utilities.TrackedThingsRequestCategory.Pawns, sightRange)
                        .Select(t => t as Pawn)
                        .Where(p => !p.Dead && !p.Downed && p.GetMovingShiftedPosition(60).DistanceToSquared(shiftedPos) < sightRangeSqr && verb.CanHitTargetFrom(shiftedPos, p.GetMovingShiftedPosition(60)) && p.HostileTo(pawn))
                        .ToList();
                    CombatAI.Gui.GUIUtility.ExecuteSafeGUIAction(() =>
                    {
                        Vector2 drawPosUI = UI.MapToUIPosition(drawPos);
                        Text.Font = GameFont.Tiny;
                        string state = GenTicks.TicksGame - lastInterupted > 120 ? "<color=blue>O</color>" : "<color=yellow>X</color>";
                        Widgets.Label(new Rect(drawPosUI.x - 25, drawPosUI.y - 15, 50, 30), $"{state}/{_visibleEnemies.Count}");
                    });
                    bool bugged = nearbyVisiblePawns.Count != _visibleEnemies.Count;
                    if (bugged)
                    {
                        Rect rect;
                        Vector2 a = UI.MapToUIPosition(drawPos);
                        Vector2 b;
                        Vector2 mid;
                        foreach (var other in nearbyVisiblePawns.Where(p => !_visibleEnemies.Contains(p)))
                        {
                            b = UI.MapToUIPosition(other.DrawPos);
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
                            Widgets.DrawBoxSolid(new Rect(UI.MapToUIPosition(_path[i - 1].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)) - new Vector2(5, 5), new Vector2(10, 10)), _colors[i]);
                            Widgets.DrawLine(UI.MapToUIPosition(_path[i - 1].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)), UI.MapToUIPosition(_path[i].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)), Color.white, 1);
                        }
                        if (_path.Count > 0)
                        {
                            Vector2 v = UI.MapToUIPosition(pawn.DrawPos.Yto0());
                            Widgets.DrawLine(UI.MapToUIPosition(_path.Last().ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)), v, _colors.Last(), 1);
                            Widgets.DrawBoxSolid(new Rect(v - new Vector2(5, 5), new Vector2(10, 10)), _colors.Last());
                        }
                        if (!_visibleEnemies.EnumerableNullOrEmpty())
                        {
                            Vector2 a = UI.MapToUIPosition(pawn.DrawPos);
                            Vector2 b;
                            Vector2 mid;
                            Rect rect;
                            int index = 0;
                            foreach (var other in _visibleEnemies)
                            {
                                b = UI.MapToUIPosition(other.DrawPos);
                                Widgets.DrawLine(a, b, Color.blue, 1);

                                mid = (a + b) / 2;
                                rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                                Widgets.DrawBox(rect);
                                Widgets.Label(rect, $"<color=gray>({index++}).</color> {Math.Round(other.Position.DistanceTo(pawn.Position), 1)}");
                            }
                        }
                    }
                }
            }
        }
#endif

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned)
            {
                return;
            }
            if (duties != null)
            {
                duties.TickRare();
            }
            //if (miningCells.Count != 0 && parent is Pawn pawn)
            //{                
            //    if (pawn.jobs.curJob == null || pawn.jobs.curJob.targetA.Cell != miningCells[0])
            //    {
            //        if (!cellBefore.IsCellWalkable(pawn))
            //        {
            //            miningCells.Clear();
            //            cellBefore = IntVec3.Invalid;
            //        }
            //        else
            //        {
            //            while (miningCells.Count != 0 && miningCells[0].IsCellWalkable(pawn))
            //            {
            //                cellBefore = miningCells[0];
            //                miningCells.RemoveAt(0);
            //            }                        
            //            if (miningCells.Count != 0)
            //            {
            //                Building building = miningCells[0].GetEdifice(pawn.Map);
            //                if (building != null)
            //                {
            //                    Job job = DigUtility.PassBlockerJob(pawn, building, cellBefore, true, true);
            //                    if (job != null)
            //                    {
            //                        pawn.jobs.StopAll();
            //                        pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            //                    }
            //                }
            //            }
            //        }
            //    }   
            //}
        }      

        public void OnScanFinished()
        {            
            if (scanning == false)
            {
                Log.Warning($"ISMA: OnScanFinished called while not scanning. ({visibleEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
                return;
            }
            scanning = false;
			#if DEBUG_REACTION
            if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight)
            {
                _visibleEnemies.Clear();
                _visibleEnemies.AddRange(visibleEnemies.Where(t => t is Pawn).Select(t => t as Pawn));
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
            }
            #endif
            if (visibleEnemies.Count > 0 && !Finder.Performance.TpsCriticallyLow)
            {
                if (GenTicks.TicksGame - lastInterupted < 240 && GenTicks.TicksGame - lastSawEnemies > 90)
                {
                    lastInterupted = -1;
                    if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight)
                    {
                        parent.Map.debugDrawer.FlashCell(parent.Position, 1.0f, "X", duration: 60);
                    }
                }
                lastSawEnemies = GenTicks.TicksGame;
            } 
            if (GenTicks.TicksGame - lastInterupted < 240 || visibleEnemies.Count == 0 || GenTicks.TicksGame - lastRetreated < 200)
            {
                return;
            }   
            if (parent is Pawn pawn && !(pawn.RaceProps?.Animal ?? true))
            {				
				if (pawn.CurJob == moveJob && GenTicks.TicksGame - lastInterupted < 300)
                {
                    lastMoved = GenTicks.TicksGame + 50;
					return;
                }
                if (Mod_CE.active && (pawn.CurJobDef.Is(Mod_CE.ReloadWeapon) || pawn.CurJobDef.Is(Mod_CE.HunkerDown)))
                {					
					return;
                }
                Stance_Warmup warmup = null;				
                if ((warmup = (pawn.stances?.curStance ?? null) as Stance_Warmup) != null && ((warmup.ticksLeft + GenTicks.TicksGame - warmup.startedTick) > 60 || warmup.ticksLeft < 30))
                {                   
					return;
                }
				Verb verb = parent.TryGetAttackVerb();
				if (verb == null || verb.IsMeleeAttack || !verb.Available() || (Mod_CE.active && Mod_CE.IsAimingCE(verb)))
				{
					return;
				}
				Thing bestEnemy = pawn.mindState.enemyTarget;
                IntVec3 bestEnemyPositon = IntVec3.Invalid;
                IntVec3 pawnPosition = pawn.Position;
                float bestEnemyScore = verb.currentTarget.IsValid && verb.currentTarget.Cell.IsValid ? verb.currentTarget.Cell.DistanceToSquared(pawnPosition) : 1e6f;           
                bool bestEnemyVisibleNow = warmup != null;
                bool bestEnemyVisibleSoon = false;
                bool retreat = false;
                bool fastCheck = warmup != null && GenTicks.TicksGame - lastMoved > 420;                
                foreach (Thing enemy in visibleEnemies)
                {
                    if (enemy != null && enemy.Spawned && !enemy.Destroyed)
                    {
                        float distSqr = pawnPosition.DistanceToSquared(enemy.Position);
                        if (distSqr < 100)
                        {
                            if (enemy is Pawn enemyPawn && distSqr < 49)
                            {
                                bestEnemy = enemy;
                                retreat = true;
                                break;
                            }
                        }                        
                        if (!fastCheck)
                        {
                            if (verb.CanHitTarget(enemy))
                            {                                
                                if (!bestEnemyVisibleNow)
                                {
                                    bestEnemyVisibleNow = true;
                                    bestEnemy = enemy;
                                    bestEnemyScore = distSqr;
                                    bestEnemyPositon = enemy.Position;
                                }
                                else
                                {
                                    if (bestEnemyScore > distSqr)
                                    {
                                        bestEnemy = enemy;
                                        bestEnemyScore = distSqr;
                                        bestEnemyPositon = enemy.Position;
                                    }
                                }                                
                            }
                            else if (!bestEnemyVisibleNow)
                            {
                                IntVec3 shiftedPos = enemy.Position;
                                if (enemy is Pawn enemyPawn)
                                {
                                    shiftedPos = enemyPawn.GetMovingShiftedPosition(60);
                                }
                                if (shiftedPos != enemy.Position && verb.CanHitTargetFrom(pawn.Position, shiftedPos))
                                {
                                    distSqr = pawnPosition.DistanceToSquared(shiftedPos);
                                    if (bestEnemyScore > distSqr)
                                    {
                                        bestEnemy = enemy;
                                        bestEnemyScore = distSqr;
                                        bestEnemyPositon = shiftedPos;
                                        bestEnemyVisibleSoon = true;
                                    }
                                }
                                else if (!bestEnemyVisibleSoon)
                                {
                                    distSqr = pawnPosition.DistanceToSquared(shiftedPos) * 2f;
                                    if (bestEnemyScore > distSqr)
                                    {
                                        bestEnemy = enemy;
                                        bestEnemyScore = distSqr;
                                        bestEnemyPositon = shiftedPos;
                                    }
                                }
                            }
                        }
                    }
                }
                if (bestEnemy == null)
                {
                    return;
                }
                if (retreat)
                {
                    //pawn.Map.debugDrawer.FlashCell(pawn.Position, 1f, "FLEE", 200);
                    pawn.mindState.enemyTarget = bestEnemy;
                    CoverPositionRequest request = new CoverPositionRequest();
                    request.caster = pawn;
                    request.target = new LocalTargetInfo(bestEnemyPositon);
                    request.verb = verb;
                    request.maxRangeFromCaster = Maths.Min(pawnPosition.DistanceTo(bestEnemy.Position) * 2, 10);
                    request.checkBlockChance = true;
                    if (CoverPositionFinder.TryFindRetreatPosition(request, out IntVec3 cell))
                    {
                        Job job_goto = JobMaker.MakeJob(JobDefOf.Goto, cell);
                        job_goto.locomotionUrgency = LocomotionUrgency.Sprint;
                        Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                        pawn.jobs.StopAll();
                        pawn.jobs.StartJob(moveJob = job_goto, JobCondition.InterruptForced);
                        pawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);                        
                    }
                    else 
                    {
                        Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                        pawn.jobs.StopAll();
                        pawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
                    }
                    lastRetreated = GenTicks.TicksGame - Rand.Int % 50;
                }
                else if(!fastCheck)
                {
                    bool changedPos = false;
                    // parent.Map.debugDrawer.FlashCell(pawn.Position, 0.9f, "s", duration: 100);
                    // ------------------------------------------------------------
                    float dist = bestEnemyPositon.DistanceToSquared(pawn.Position); ;
                    if (dist > 36)
                    {
                        if (bestEnemyVisibleNow)
                        {
                            //if (!Mod_CE.active)
                            //{
                                pawn.mindState.enemyTarget = bestEnemy;
                                CastPositionRequest request = new CastPositionRequest();
                                request.caster = pawn;
                                request.target = bestEnemy;
                                request.verb = verb;
                                request.maxRangeFromTarget = 9999;
								request.maxRangeFromCaster = Mathf.Clamp(pawn.Position.DistanceTo(bestEnemy.Position) / 2, 5, 15);								
                                request.wantCoverFromTarget = true;
                                if (Rand.Chance(0.5f) && CastPositionFinder.TryFindCastPosition(request, out IntVec3 cell))
                                {
                                    Job job_goto = JobMaker.MakeJob(JobDefOf.Goto, cell);
                                    job_goto.locomotionUrgency = LocomotionUrgency.Sprint;
                                    Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                                    job_waitCombat.checkOverrideOnExpire = true;
                                    pawn.jobs.StopAll();
                                    pawn.jobs.StartJob(moveJob = job_goto, JobCondition.InterruptForced);
                                    pawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
                                    //pawn.Map.debugDrawer.FlashCell(pawn.Position, 0.5f, "2", 200);
                                    changedPos = true;
                                }
                                else
                                {
                                    Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                                    pawn.jobs.StopAll();
                                    pawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
                                    //pawn.Map.debugDrawer.FlashCell(pawn.Position, 1, "2a", 200);
                                }
                            //}
                            //else
                            //{
                            //    pawn.mindState.enemyTarget = bestEnemy;
                            //    CoverPositionRequest request = new CoverPositionRequest();
                            //    request.caster = pawn;
                            //    request.target = new LocalTargetInfo(bestEnemy);
                            //    request.verb = verb;
                            //    request.maxRangeFromCaster = Mathf.Clamp(pawnPosition.DistanceTo(bestEnemy.Position) / 2, 4, Finder.Performance.TpsCriticallyLow ? 7 : 13);
                            //    request.checkBlockChance = true;
                            //    if (CoverPositionFinder.TryFindCoverPosition(request, out IntVec3 cell))
                            //    {
                            //        Job job_goto = JobMaker.MakeJob(JobDefOf.Goto, cell);
                            //        job_goto.locomotionUrgency = LocomotionUrgency.Sprint;
                            //        Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                            //        pawn.jobs.StopAll();
                            //        pawn.jobs.StartJob(moveJob = job_goto, JobCondition.InterruptForced);
                            //        pawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
                            //        //pawn.Map.debugDrawer.FlashCell(pawn.Position, 1, "2", 200);
                            //        changedPos = true;
                            //    }
                            //    else
                            //    {
                            //        Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                            //        pawn.jobs.StopAll();
                            //        pawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
                            //        //pawn.Map.debugDrawer.FlashCell(pawn.Position, 1, "2a", 200);
                            //    }
                            //}
                        }
                        else
                        {
                            pawn.mindState.enemyTarget = bestEnemy;
                            CoverPositionRequest request = new CoverPositionRequest();
                            request.caster = pawn;
                            request.target = new LocalTargetInfo(bestEnemyPositon);
                            request.verb = verb;
                            request.maxRangeFromCaster = Mathf.Clamp(pawnPosition.DistanceTo(bestEnemy.Position) / 2, 4, Finder.Performance.TpsCriticallyLow ? 7 : 13);
							request.checkBlockChance = true;
                            if (CoverPositionFinder.TryFindCoverPosition(request, out IntVec3 cell))
                            {
                                Job job_goto = JobMaker.MakeJob(JobDefOf.Goto, cell);
                                job_goto.locomotionUrgency = LocomotionUrgency.Sprint;
                                Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                                pawn.jobs.StopAll();
                                pawn.jobs.StartJob(moveJob = job_goto, JobCondition.InterruptForced);
                                pawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
								//pawn.Map.debugDrawer.FlashCell(pawn.Position, 1, "3", 200);
								changedPos = true;
							}
                            else
                            {
                                Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                                pawn.jobs.StopAll();
                                pawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
								//pawn.Map.debugDrawer.FlashCell(pawn.Position, 1, "3a", 200);
							}                            
						}
                    }
                    else
                    {
                        pawn.mindState.enemyTarget = bestEnemy;
                        Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, expiryInterval: Rand.Int % 100 + 100);
                        pawn.jobs.StopAll();
                        pawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
                    }
                    if (changedPos)
                    {
						lastInterupted = lastMoved = GenTicks.TicksGame;
                    }
                    else
                    {
                        lastInterupted = GenTicks.TicksGame - Rand.Int % 60;
					}
				}                
            }
        }

        public void OnScanStarted()
        {
            if(scanning == true)
            {
                Log.Warning($"ISMA: OnScanStarted called while scanning. ({visibleEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
                return;
            }
            scanning = true;
            visibleEnemies.Clear();            
        }

        public bool TryStartMiningJobs(PawnPath path)
        {
            //if (miningCells.Count > 0 && (parent.Position == cellBefore || parent.Position == miningCells[0] || path.nodes[0] == cellBefore))
            //{
            //    return false;
            //}
            //if (parent is Pawn pawn && path.TryGetBlockedSubPath(pawn, miningCells, ref cellBefore))
            //{
            //    Pawn_CustomDutyTracker.CustomPawnDuty custom = new Pawn_CustomDutyTracker.CustomPawnDuty();
            //    if (cellBefore.IsValid && miningCells.Count != 0)
            //    {
            //        Building building = miningCells[0].GetEdifice(pawn.Map);
            //        if (building != null)
            //        {
            //            Job job = DigUtility.PassBlockerJob(pawn, building, cellBefore, true, true);
            //            if (job != null)
            //            {
            //                pawn.jobs.StopAll();
            //                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            //                return true;
            //            }
            //        }
            //    }
            //}
            //this.cellBefore = IntVec3.Invalid;
            //miningCells.Clear();
            return false;
        }

        public void Notify_EnemiesVisible(IEnumerable<Thing> things)
        {
            if (!scanning)
            {                
                Log.Warning($"ISMA: Notify_EnemiesVisible called while not scanning. ({visibleEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
                return;
            }            
            visibleEnemies.AddRange(things);            
        }

		public void Notify_EnemyVisible(Thing thing)
		{
			if (!scanning)
			{
				Log.Warning($"ISMA: Notify_EnemiesVisible called while not scanning. ({visibleEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
				return;
			}
			visibleEnemies.Add(thing);
		}

		public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref duties, "duties");
            Scribe_Collections.Look(ref miningCells, "miningCells", LookMode.Value);
            if (miningCells == null)
            {
                miningCells = new List<IntVec3>();
            }
            Scribe_Values.Look(ref cellBefore, "cellBefore");
            if (parent is Pawn pawn)
            {
                if (duties == null)
                {
                    duties = new Pawn_CustomDutyTracker(pawn);
                }
                duties.pawn = pawn;
            }
        }        

        public void Notify_SightReaderChanged(SightTracker.SightReader reader)
        {
            this.sightReader = reader;
        }        
    }
}

