using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
namespace CombatAI.Patches
{
	public static class WealthWatcher_Patch
	{
		[HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.WealthItemsFilter))]
		public class WealthWatcher_WealthItemsFilter
		{
			private static void Postfix(IThingHolder x, ref bool __result)
			{
				if (Finder.Settings.EnableExcludeFoodFromWealth && __result && x is Thing thing && thing.def.thingCategories != null && (thing.def.thingCategories.Contains(ThingCategoryDefOf.PlantFoodRaw) || thing.def.thingCategories.Contains(ThingCategoryDefOf.MeatRaw)))
				{
					__result = false;
				}
			}
		}
		
		[HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.CalculateWealthItems))]

		public class WealthWatcher_MarketValue
		{
			private static readonly MethodInfo pMarketValue = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.MarketValue));

			private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var codes = instructions.ToList();
				for (int index = 0; index < codes.Count; index++)
				{
					if (codes[index].opcode == OpCodes.Callvirt && codes[index].operand is MethodInfo methodInfo && methodInfo == pMarketValue)
					{
						yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WealthWatcher_MarketValue), nameof(WealthWatcher_MarketValue.MarketValueMultiplier)));
						Log.Message("Patched Wealth");
						continue;
					}
					yield return codes[index];
				}
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private static float MarketValueMultiplier(Thing thing)
			{
				if (Finder.Settings.EnableExcludeFoodFromWealth && thing?.def.thingCategories != null && (thing.def.thingCategories.Contains(ThingCategoryDefOf.MeatRaw) || thing.def.thingCategories.Contains(ThingCategoryDefOf.PlantFoodRaw)))
				{
					return 0f;
				}
				return thing.MarketValue;
			}
		}
	}
}
