using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using GUIUtility = CombatAI.Gui.GUIUtility;
namespace CombatAI.Patches
{
    public static class EditWindow_Log_Patch 
    {
        [HarmonyPatch(typeof(EditWindow_Log), nameof(EditWindow_Log.DoWindowContents))]
        private static class EditWindow_Log_DoWindowContents_Patch
        {
            private static FieldInfo fCanAutoOpen = AccessTools.Field(typeof(EditWindow_Log), nameof(EditWindow_Log.canAutoOpen));
            
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes    = instructions.ToList();
                bool                  finished = false;
                for (int i = 0; i < instructions.Count(); i++)
                {
                    if (!finished)
                    {
                        if (codes[i].opcode == OpCodes.Ldsfld && codes[i].OperandIs(fCanAutoOpen))
                        {
                            yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(codes[i]).MoveLabelsFrom(codes[i]);
                            yield return new CodeInstruction(OpCodes.Ldloca_S, 2);
                            yield return new CodeInstruction(OpCodes.Ldloc_3);
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EditWindow_Log_DoWindowContents_Patch), nameof(DoCAIWidgets)));
                            finished = true;
                        }
                    }
                    yield return codes[i];
                }
            }

            private static void DoCAIWidgets(EditWindow_Log instance, ref float x, float y)
            {
                if (Current.ProgramState == ProgramState.Playing)
                {
	                float Xo = x;
                    GUIUtility.ExecuteSafeGUIAction(() =>
                    {
	                    float Xi = Xo;
                        if (!Finder.Settings.Debug_LogJobs)
                        {
                            UnityEngine.GUI.color = Color.green;
                            instance.DoRowButton(ref Xi, y, "Enable CAI Job Logging", null, () =>
                            {
	                            Finder.Settings.Debug         = true;
	                            Finder.Settings.Debug_LogJobs = true;
	                            Messages.Message("WARNING: Please remember to disable job logging.", MessageTypeDefOf.CautionInput);
                            });
                        }
                        else if (Finder.Settings.Debug_LogJobs)
                        {
                            UnityEngine.GUI.color = Color.red;
                            instance.DoRowButton(ref Xi, y, "Disable CAI Job Logging", null, () =>
                            {
	                            Finder.Settings.Debug         = false;
	                            Finder.Settings.Debug_LogJobs = false;
                            });
                        }
                        Xo = Xi;
                    });
                    x = Xo;
                }
            }
        }
    }
}
