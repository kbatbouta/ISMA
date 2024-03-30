using System;
using System.Collections.Generic;
using CombatAI.Gui;
using CombatAI.Patches;
using CombatAI.R;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using GUIUtility = CombatAI.Gui.GUIUtility;
namespace CombatAI
{
    public class CombatAIMod : Mod
    {
        private readonly Listing_Collapsible                   collapsible_advance     = new Listing_Collapsible(true);
        private readonly Listing_Collapsible                   collapsible_basic       = new Listing_Collapsible(true);
        private readonly Listing_Collapsible                   collapsible_debug       = new Listing_Collapsible(true);
        private readonly Listing_Collapsible                   collapsible_fog         = new Listing_Collapsible();
        private readonly Listing_Collapsible                   collapsible_tech        = new Listing_Collapsible();
        private readonly Listing_Collapsible.Group_Collapsible collapsible_groupLeft   = new Listing_Collapsible.Group_Collapsible();
        private readonly Listing_Collapsible.Group_Collapsible collapsible_groupRight  = new Listing_Collapsible.Group_Collapsible();
        private readonly Listing_Collapsible                   collapsible_performance = new Listing_Collapsible(true);

        /*  -----------------------
         *  
         *  -- Settings Gui core --
         *          
         */

        private bool collapsibleGroupInited;
        public CombatAIMod(ModContentPack contentPack) : base(contentPack)
        {
            Finder.Mod      = this;
            Finder.Settings = GetSettings<Settings>();
            Finder.Harmony  = new Harmony("Krkr.Rule56");
            HarmonyPatches.Initialize();
            if (Finder.Settings == null)
            {
                Finder.Settings = new Settings();
            }
            LongEventHandler.QueueLongEvent(ArmorUtility.Initialize, "CombatAI.Preparing", false, null);
            LongEventHandler.QueueLongEvent(CompatibilityManager.Initialize, "CombatAI.Preparing", false, null);
            LongEventHandler.QueueLongEvent(ThinkNodeDatabase.Initialize, "CombatAI.Preparing", false, null);
            LongEventHandler.QueueLongEvent(RaidTargetDatabase.Initialize, "CombatAI.Preparing", false, null);
        }

        public override string SettingsCategory()
        {
            return Keyed.CombatAI;
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            Finder.Settings.Write();
        }

        /*  ----------------------
         *  
         *  ---- Settings Gui ----
         *          
         */

        private void FillCollapsible_Basic(Listing_Collapsible collapsible)
        {
            if (Mod_CE.active)
            {
                collapsible.Lambda(20, rect =>
                {
                    Widgets.DrawBoxSolid(rect, Color.white);
                    rect = rect.ContractedBy(1);
                    Widgets.DrawBoxSolid(rect, !Finder.Settings.LeanCE_Enabled ? Color.yellow : Color.green);
                    rect.xMin                   += 5;
                    GUI.color                   =  Color.black;
                    Text.CurFontStyle.fontStyle =  FontStyle.Bold;
                    Widgets.Label(rect, !Finder.Settings.LeanCE_Enabled ? "Combat Extended Detected! PLEASE ENABLE CE PROFILE!" : "Combat Extended Detected!");
                }, useMargins: false);
                collapsible.Line(1);
            }

            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_CELean, ref Finder.Settings.LeanCE_Enabled);
            collapsible.Line(1);

            collapsible.Label(Keyed.CombatAI_Settings_Basic_Presets);
            collapsible.Gap(1);
            collapsible.Lambda(22, rect =>
            {
	            if (Widgets.ButtonText(rect, Keyed.CombatAI_DefKindSettings_Title))
	            {
		            if (!Find.WindowStack.windows.Any(w => w is Window_DefKindSettings))
		            {
			            Find.WindowStack.Add(new Window_DefKindSettings());
		            }
	            }
            });
            collapsible.Label(Keyed.CombatAI_Settings_Basic_Presets_Description);
            collapsible.Lambda(25, inRect =>
            {
                GUIUtility.Row(inRect, new List<Action<Rect>>
                {
                    rect =>
                    {
                        GUI.color = Color.green;
                        if (Widgets.ButtonText(rect, Keyed.CombatAI_Settings_Basic_Presets_Easy))
                        {
                            DifficultyUtility.SetDifficulty(Difficulty.Easy);
                            Messages.Message(Keyed.CombatAI_Settings_Basic_Presets_Applied + " " + Keyed.CombatAI_Settings_Basic_Presets_Easy, MessageTypeDefOf.TaskCompletion);
                        }
                    },
                    rect =>
                    {
                        if (Widgets.ButtonText(rect, Keyed.CombatAI_Settings_Basic_Presets_Normal))
                        {
                            DifficultyUtility.SetDifficulty(Difficulty.Normal);
                            Messages.Message(Keyed.CombatAI_Settings_Basic_Presets_Applied + " " + Keyed.CombatAI_Settings_Basic_Presets_Normal, MessageTypeDefOf.TaskCompletion);
                        }
                    },
                    rect =>
                    {
                        if (Widgets.ButtonText(rect, Keyed.CombatAI_Settings_Basic_Presets_Hard))
                        {
                            DifficultyUtility.SetDifficulty(Difficulty.Hard);
                            Messages.Message(Keyed.CombatAI_Settings_Basic_Presets_Applied + " " + Keyed.CombatAI_Settings_Basic_Presets_Hard, MessageTypeDefOf.TaskCompletion);
                        }
                    },
                    rect =>
                    {
                        GUI.color = Color.red;
                        if (Widgets.ButtonText(rect, Keyed.CombatAI_Settings_Basic_Presets_Deathwish))
                        {
                            DifficultyUtility.SetDifficulty(Difficulty.DeathWish);
                            Messages.Message(Keyed.CombatAI_Settings_Basic_PerformanceOpt_Warning, MessageTypeDefOf.CautionInput);
                            Messages.Message(Keyed.CombatAI_Settings_Basic_Presets_Applied + " " + Keyed.CombatAI_Settings_Basic_Presets_Deathwish, MessageTypeDefOf.TaskCompletion);
                        }
                    }
                }, false);
            }, useMargins: true);
            collapsible.Line(1);
            if (collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_PerformanceOpt, ref Finder.Settings.PerformanceOpt_Enabled, Keyed.CombatAI_Settings_Basic_PerformanceOpt_Description) && !Finder.Settings.PerformanceOpt_Enabled)
            {
                Messages.Message(Keyed.CombatAI_Settings_Basic_PerformanceOpt_Warning, MessageTypeDefOf.CautionInput);
            }
            collapsible.Line(1);            
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_Caster, ref Finder.Settings.Caster_Enabled);
            if (collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_RandomizedPersonality, ref Finder.Settings.Personalities_Enabled))
            {
	            Finder.Settings.ResetTechSettings();
            }
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_FoodWealth, ref Finder.Settings.EnableExcludeFoodFromWealth);
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_Caster, ref Finder.Settings.Caster_Enabled);
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_Targeter, ref Finder.Settings.Targeter_Enabled);
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_Flanking, ref Finder.Settings.Flank_Enabled);
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_Groups, ref Finder.Settings.Enable_Groups, Keyed.CombatAI_Settings_Basic_Groups_Description);
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_Sprinting, ref Finder.Settings.Enable_Sprinting, Keyed.CombatAI_Settings_Basic_Sprinting_Description);
        }

        private void FillCollapsible_FogOfWar(Listing_Collapsible collapsible)
        {
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_FogOfWar_Enable, ref Finder.Settings.FogOfWar_Enabled);

            if (Finder.Settings.FogOfWar_Enabled)
            {
                collapsible.Line(1);
                if (collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_FogOfWar_OldShader, ref Finder.Settings.FogOfWar_OldShader))
                {
	                Messages.Message(R.Keyed.CombatAI_Settings_Basic_FogOfWar_OldShader_Restart, MessageTypeDefOf.CautionInput);
                }
	            collapsible.Line(1);
                collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_FogOfWar_Animals, ref Finder.Settings.FogOfWar_Animals);
                collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_FogOfWar_Animals_SmartOnly, ref Finder.Settings.FogOfWar_AnimalsSmartOnly, disabled: !Finder.Settings.FogOfWar_Animals);
                collapsible.Line(1);
                collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_FogOfWar_Allies, ref Finder.Settings.FogOfWar_Allies);
                collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Basic_FogOfWar_Turrets, ref Finder.Settings.FogOfWar_Turrets);

                collapsible.Line(1);

                collapsible.Label(Keyed.CombatAI_Settings_Basic_FogOfWar_Density);
                collapsible.Lambda(25, rect =>
                {
                    Finder.Settings.FogOfWar_FogColor = HorizontalSlider_NewTemp(rect, Finder.Settings.FogOfWar_FogColor, 0.0f, 1.0f, false, Keyed.CombatAI_Settings_Basic_FogOfWar_Density_Readouts.Formatted(Finder.Settings.FogOfWar_FogColor.ToString()), 0.05f);
                }, useMargins: true);

//				collapsible.Line(1);

                collapsible.Label(Keyed.CombatAI_Settings_Basic_FogOfWar_RangeMul);
                collapsible.Lambda(25, rect =>
                {
                    Finder.Settings.FogOfWar_RangeMultiplier = HorizontalSlider_NewTemp(rect, Finder.Settings.FogOfWar_RangeMultiplier, 0.75f, 8.0f, false, Keyed.CombatAI_Settings_Basic_FogOfWar_RangeMul_Readouts.Formatted(Finder.Settings.FogOfWar_RangeMultiplier.ToString()), 0.05f);
                }, useMargins: true);

//				collapsible.Line(1);

                collapsible.Label(Keyed.CombatAI_Settings_Basic_FogOfWar_FadeMul);
                collapsible.Lambda(25, rect =>
                {
                    Finder.Settings.FogOfWar_RangeFadeMultiplier = HorizontalSlider_NewTemp(rect, Finder.Settings.FogOfWar_RangeFadeMultiplier, 0.0f, 1.0f, false, Keyed.CombatAI_Settings_Basic_FogOfWar_FadeMul_Readouts.Formatted(Finder.Settings.FogOfWar_RangeFadeMultiplier.ToString()), 0.05f);
                }, useMargins: true);
            }
        }


        private void FillCollapsible_Performance(Listing_Collapsible collapsible)
        {
            collapsible.Label(Keyed.CombatAI_Settings_Basic_DestWeight);
            collapsible.Gap(1);
            collapsible.Label(Keyed.CombatAI_Settings_Basic_DestWeight_Description);
            collapsible.Label(Keyed.CombatAI_Settings_Basic_DestWeight_Warning, fontStyle: FontStyle.Bold);
            collapsible.Gap(1);
            collapsible.Lambda(25, rect =>
            {
                string color = Finder.Settings.Pathfinding_DestWeight < 0.75f ? "red" : "while";
                string extra = Finder.Settings.Pathfinding_DestWeight < 0.75f ? " <color=yellow>WILL IMPACT PERFORMANCE</color>" : "";
                Text.CurFontStyle.fontStyle            = FontStyle.Bold;
                Finder.Settings.Pathfinding_DestWeight = HorizontalSlider_NewTemp(rect, Finder.Settings.Pathfinding_DestWeight, 0.3f, 0.95f, true, $"<color={color}>{Math.Round(Finder.Settings.Pathfinding_DestWeight * 100f, 1)}%</color>{extra}", 0.05f);
            }, useMargins: true);

            collapsible.Line(2);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Description);
            /*
             * ----------------------
             */
            collapsible.Line(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_FrienldiesAndEnemies);
            collapsible.Gap(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Frequency.Formatted(60f / (Finder.Settings.SightSettings_FriendliesAndRaiders.buckets * Finder.Settings.SightSettings_FriendliesAndRaiders.interval)));
            collapsible.Lambda(25, rect =>
            {
                Finder.Settings.SightSettings_FriendliesAndRaiders.interval = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_FriendliesAndRaiders.interval, 1, 20, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Interval.Formatted(Finder.Settings.SightSettings_FriendliesAndRaiders.interval), 0.05f);
            }, useMargins: true);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Threads);
            collapsible.Lambda(25, rect =>
            {
	            Finder.Settings.SightSettings_FriendliesAndRaiders.threads = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_FriendliesAndRaiders.threads, 1, 3, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Threads_Number.Formatted(Finder.Settings.SightSettings_FriendliesAndRaiders.threads), 0.05f);
            }, useMargins: true);
            if (Current.ProgramState != ProgramState.Playing)
            {
                collapsible.Lambda(25, rect =>
                {
                    Finder.Settings.SightSettings_FriendliesAndRaiders.buckets = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_FriendliesAndRaiders.buckets, 1, 20, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Buckets.Formatted(Finder.Settings.SightSettings_FriendliesAndRaiders.buckets), 0.05f);
                }, useMargins: true);
            }
            collapsible.Lambda(25, rect =>
            {
                Finder.Settings.SightSettings_FriendliesAndRaiders.carryLimit = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_FriendliesAndRaiders.carryLimit, 4, 16, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_CarryLimit.Formatted(Finder.Settings.SightSettings_FriendliesAndRaiders.carryLimit), 0.05f);
                if (Mouse.IsOver(rect))
                {
                    TooltipHandler.TipRegion(rect, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_CarryLimit_Description);
                }
            }, useMargins: true);
            /*
             * ----------------------
             */
            collapsible.Line(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_MechsAndInsect);
            collapsible.Gap(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Frequency.Formatted(60f / (Finder.Settings.SightSettings_MechsAndInsects.buckets * Finder.Settings.SightSettings_MechsAndInsects.interval)));
            collapsible.Lambda(25, rect =>
            {
                Finder.Settings.SightSettings_MechsAndInsects.interval = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_MechsAndInsects.interval, 1, 20, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Interval.Formatted(Finder.Settings.SightSettings_MechsAndInsects.interval), 0.05f);
            }, useMargins: true);
            collapsible.Gap(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Threads);
            collapsible.Lambda(25, rect =>
            {
	            Finder.Settings.SightSettings_MechsAndInsects.threads = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_MechsAndInsects.threads, 1, 3, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Threads_Number.Formatted(Finder.Settings.SightSettings_MechsAndInsects.threads), 0.05f);
            }, useMargins: true);
            if (Current.ProgramState != ProgramState.Playing)
            {
                collapsible.Lambda(25, rect =>
                {
                    Finder.Settings.SightSettings_MechsAndInsects.buckets = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_MechsAndInsects.buckets, 1, 20, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Buckets.Formatted(Finder.Settings.SightSettings_MechsAndInsects.buckets), 0.05f);
                }, useMargins: true);
            }
            collapsible.Lambda(25, rect =>
            {
                Finder.Settings.SightSettings_MechsAndInsects.carryLimit = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_MechsAndInsects.carryLimit, 4, 16, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_CarryLimit.Formatted(Finder.Settings.SightSettings_MechsAndInsects.carryLimit), 0.05f);
                if (Mouse.IsOver(rect))
                {
                    TooltipHandler.TipRegion(rect, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_CarryLimit_Description);
                }
            }, useMargins: true);
            /*
             * ----------------------
             */
            collapsible.Line(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_WildLife);
            collapsible.Gap(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Frequency.Formatted(60f / (Finder.Settings.SightSettings_Wildlife.buckets * Finder.Settings.SightSettings_Wildlife.interval)));
            collapsible.Lambda(25, rect =>
            {
                Finder.Settings.SightSettings_Wildlife.interval = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_Wildlife.interval, 1, 20, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Interval.Formatted(Finder.Settings.SightSettings_Wildlife.interval), 0.05f);
            }, useMargins: true);
            collapsible.Gap(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Threads);
            collapsible.Lambda(25, rect =>
            {
	            Finder.Settings.SightSettings_Wildlife.threads = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_Wildlife.threads, 1, 3, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Threads_Number.Formatted(Finder.Settings.SightSettings_Wildlife.threads), 0.05f);
            }, useMargins: true);
            if (Current.ProgramState != ProgramState.Playing)
            {
                collapsible.Lambda(25, rect =>
                {
                    Finder.Settings.SightSettings_Wildlife.buckets = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_Wildlife.buckets, 1, 20, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Buckets.Formatted(Finder.Settings.SightSettings_Wildlife.buckets), 0.05f);
                }, useMargins: true);
            }
            collapsible.Lambda(25, rect =>
            {
                Finder.Settings.SightSettings_Wildlife.carryLimit = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_Wildlife.carryLimit, 4, 16, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_CarryLimit.Formatted(Finder.Settings.SightSettings_Wildlife.carryLimit), 0.05f);
                if (Mouse.IsOver(rect))
                {
                    TooltipHandler.TipRegion(rect, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_CarryLimit_Description);
                }
            }, useMargins: true);
            /*
             * ----------------------
             */
            collapsible.Line(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Turrets);
            collapsible.Gap(1);
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Frequency.Formatted((60f / ((float)Finder.Settings.SightSettings_SettlementTurrets.buckets * Finder.Settings.SightSettings_SettlementTurrets.interval)).ToString()));
            collapsible.Lambda(25, rect =>
            {
                Finder.Settings.SightSettings_SettlementTurrets.interval = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_SettlementTurrets.interval, 1, 20, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Interval.Formatted(Finder.Settings.SightSettings_SettlementTurrets.interval), 0.05f);
            }, useMargins: true);
            if (Current.ProgramState != ProgramState.Playing)
            {
                collapsible.Lambda(25, rect =>
                {
                    Finder.Settings.SightSettings_SettlementTurrets.buckets = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_SettlementTurrets.buckets, 1, 20, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_Buckets.Formatted(Finder.Settings.SightSettings_SettlementTurrets.buckets), 0.05f);
                }, useMargins: true);
            }
            collapsible.Lambda(25, rect =>
            {
                Finder.Settings.SightSettings_SettlementTurrets.carryLimit = (int)HorizontalSlider_NewTemp(rect, Finder.Settings.SightSettings_SettlementTurrets.carryLimit, 4, 16, false, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_CarryLimit.Formatted(Finder.Settings.SightSettings_SettlementTurrets.carryLimit), 0.05f);
                if (Mouse.IsOver(rect))
                {
                    TooltipHandler.TipRegion(rect, Keyed.CombatAI_Settings_Advance_Sight_Performance_Readouts_CarryLimit_Description);
                }
            }, useMargins: true);
        }

        private void FillCollapsible_Advance(Listing_Collapsible collapsible)
        {
            collapsible.Label(Keyed.CombatAI_Settings_Advance_Warning);
            collapsible.Line(1);
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Advance_Enable, ref Finder.Settings.AdvancedUser);
            if (Finder.Settings.AdvancedUser)
            {
                collapsible.Line(1);
                collapsible.Label(Keyed.CombatAI_Settings_Basic_SappingMul_Description);
                collapsible.Lambda(25, rect =>
                {
                    Widgets.HorizontalSlider(rect, ref Finder.Settings.Pathfinding_SappingMul, new FloatRange(0.5f, 1.5f), Keyed.CombatAI_Settings_Basic_SappingMul);
                }, useMargins: true);
                collapsible.Line(1);
                collapsible.Lambda(25, rect =>
                {
	                float val = Finder.Settings.Pathfinding_SquadPathWidth;
	                Widgets.HorizontalSlider(rect, ref val, new FloatRange(1, 10), Keyed.CombatAI_Settings_Advance_SquadPathWidth_Description.Formatted(Finder.Settings.Pathfinding_SquadPathWidth));
	                Finder.Settings.Pathfinding_SquadPathWidth = Mathf.RoundToInt(Mathf.Clamp(val, 1, 10));
                }, useMargins: true);
            }
        }

        public void FillCollapsible_FactionTechSettings(Listing_Collapsible collapsible)
        {
	        collapsible.Label(R.Keyed.CombatAI_Settings_FactionTech_Desciption);
	        collapsible.Line(1);
	        foreach (TechLevel tech in Enum.GetValues(typeof(TechLevel)))
	        {
		        Settings.FactionTechSettings techSettings = Finder.Settings.GetTechSettings(tech);
		        collapsible.Label(R.Keyed.CombatAI_Settings_FactionTech_Tech.Formatted(tech.ToStringHuman()));
		        collapsible.Gap(1);
		        collapsible.Lambda(25, rect =>
		        {
			        Widgets.HorizontalSlider(rect, ref techSettings.cover, new FloatRange(0.0f, 3.0f), Keyed.CombatAI_Settings_FactionTech_Cover.Formatted(Math.Round(techSettings.cover, 2)));
		        }, useMargins: true);
		        collapsible.Gap(1);
		        collapsible.Lambda(25, rect =>
		        {
			        Widgets.HorizontalSlider(rect, ref techSettings.retreat, new FloatRange(0.0f, 3.0f), Keyed.CombatAI_Settings_FactionTech_Retreat.Formatted(Math.Round(techSettings.retreat, 2)));
		        }, useMargins: true);
		        collapsible.Gap(1);
		        collapsible.Lambda(25, rect =>
		        {
			        Widgets.HorizontalSlider(rect, ref techSettings.pathing, new FloatRange(0.0f, 1.25f), Keyed.CombatAI_Settings_FactionTech_Pathing.Formatted(Math.Round(techSettings.pathing, 2)));
		        }, useMargins: true);
		        collapsible.Gap(1);
		        collapsible.Lambda(25, rect =>
		        {
			        Widgets.HorizontalSlider(rect, ref techSettings.duck, new FloatRange(0.0f, 3.0f), Keyed.CombatAI_Settings_FactionTech_Duck.Formatted(Math.Round(techSettings.duck, 2)));
		        }, useMargins: true);
		        collapsible.Gap(1);
		        collapsible.Lambda(25, rect =>
		        {
			        Widgets.HorizontalSlider(rect, ref techSettings.group, new FloatRange(0.0f, 3.0f), Keyed.CombatAI_Settings_FactionTech_Group.Formatted(Math.Round(techSettings.group, 2)));
		        }, useMargins: true);
		        collapsible.Gap(1);
		        collapsible.Lambda(25, rect =>
		        {
			        Widgets.HorizontalSlider(rect, ref techSettings.sapping, new FloatRange(0.0f, 3.0f), Keyed.CombatAI_Settings_FactionTech_Sapping.Formatted(Math.Round(techSettings.sapping, 2)));
		        }, useMargins: true);
		        collapsible.Line(1);
	        }
        }

        private void FillCollapsible_Debugging(Listing_Collapsible collapsible)
        {
            collapsible.CheckboxLabeled(Keyed.CombatAI_Settings_Debugging_Enable, ref Finder.Settings.Debug);
            collapsible.CheckboxLabeled("Disable quick setup menu", ref Finder.Settings.FinishedQuickSetup);
            collapsible.CheckboxLabeled("Enable job logging", ref Finder.Settings.Debug_LogJobs);
            collapsible.CheckboxLabeled("Enable cinematic mode", ref Finder.Settings.Debug_DisablePawnGuiOverlay);
            collapsible.CheckboxLabeled("Draw sight grid", ref Finder.Settings.Debug_DrawShadowCasts);
            collapsible.CheckboxLabeled("Draw sight vector field", ref Finder.Settings.Debug_DrawShadowCastsVectors);
            collapsible.CheckboxLabeled("Draw threat (pawn armor vs enemy)", ref Finder.Settings.Debug_DrawThreatCasts);
            collapsible.CheckboxLabeled("Draw proximity grid", ref Finder.Settings.Debug_DrawAvoidanceGrid_Proximity);
            collapsible.CheckboxLabeled("Draw danger grid", ref Finder.Settings.Debug_DrawAvoidanceGrid_Danger);
            collapsible.CheckboxLabeled("Draw path cost", ref Finder.Settings.Debug_DebugPathfinding);
            collapsible.CheckboxLabeled("Draw availability", ref Finder.Settings.Debug_DebugAvailability);
            collapsible.CheckboxLabeled("Debug things tracker", ref Finder.Settings.Debug_DebugThingsTracker);
            collapsible.CheckboxLabeled("Debug validate sight <color=red>EXTREMELY BAD FOR PERFORMANCE</color>", ref Finder.Settings.Debug_ValidateSight);
            collapsible.Line(1);
            collapsible.Label("DO NOT USE THESE AT ALL - FOR DEVS ONLY - WILL FILL YOUR DISK IN 10 MINUTES");
            collapsible.CheckboxLabeled("Dump data <color=red>EXTREMELY BAD FOR PERFORMANCE THIS IS DEAD DON'T TOUCH IT</color>", ref Finder.Settings.Debug_DebugDumpData);
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            if (!collapsibleGroupInited)
            {
                collapsibleGroupInited  = true;
                collapsible_debug.Group = collapsible_groupRight;
                collapsible_groupRight.Register(collapsible_debug);

                collapsible_performance.Group = collapsible_groupRight;
                collapsible_groupRight.Register(collapsible_performance);

                collapsible_advance.Group = collapsible_groupRight;
                collapsible_groupRight.Register(collapsible_advance);

                collapsible_fog.Group = collapsible_groupLeft;
                collapsible_groupLeft.Register(collapsible_fog);

                collapsible_tech.Group = collapsible_groupLeft;
                collapsible_groupLeft.Register(collapsible_tech);
                
                collapsible_basic.Group = collapsible_groupLeft;
                collapsible_groupLeft.Register(collapsible_basic);
                collapsible_basic.Expanded = true;
            }
            Rect rectLeft = inRect.LeftHalf();
            // -------------
            // Left  section
            //
            // general settings
            // collapsible_basic.Expanded = true;
            if (!collapsible_groupLeft.AnyExpanded)
            {
                collapsible_basic.Expanded = true;
            }
            collapsible_basic.Begin(rectLeft, Keyed.CombatAI_Settings_Basic);
            FillCollapsible_Basic(collapsible_basic);
            collapsible_basic.End(ref rectLeft);
            rectLeft.yMin += 5;
            
            collapsible_tech.Begin(rectLeft, R.Keyed.CombatAI_Settings_FactionTech);
            FillCollapsible_FactionTechSettings(collapsible_tech);
            collapsible_tech.End(ref rectLeft);
            rectLeft.yMin += 5;
            
            collapsible_fog.Begin(rectLeft, Keyed.CombatAI_Settings_Basic_FogOfWar);
            FillCollapsible_FogOfWar(collapsible_fog);
            collapsible_fog.End(ref rectLeft);
            rectLeft.yMin += 5;

            // -------------
            //
            // Right section
            Rect rectRight = inRect.RightHalf();
            rectRight.xMin += 5;
            collapsible_advance.Begin(rectRight, Keyed.CombatAI_Settings_Advance);
            FillCollapsible_Advance(collapsible_advance);
            collapsible_advance.End(ref rectRight);
            rectRight.yMin += 5;

            // debug settings
            if (Finder.Settings.AdvancedUser)
            {
                collapsible_debug.Begin(rectRight, Keyed.CombatAI_Settings_Debugging);
                FillCollapsible_Debugging(collapsible_debug);
                collapsible_debug.End(ref rectRight);
                rectRight.yMin += 5;

                collapsible_performance.Begin(rectRight, Keyed.CombatAI_Settings_Advance_Sight_Performance);
                FillCollapsible_Performance(collapsible_performance);
                collapsible_performance.End(ref rectRight);
                rectRight.yMin += 5;
            }
        }

        // TODO rework this.
        private float HorizontalSlider_NewTemp(Rect rect, float val, float min, float max, bool middleAlinment, string label, float roundTo = -1)
        {
            Widgets.HorizontalSlider(rect, ref val, new FloatRange(min, max), label, roundTo);
            return val;
        } 
    }
}
