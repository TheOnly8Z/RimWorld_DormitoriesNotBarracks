using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

using HarmonyLib;
using RimWorld;
using Verse;

namespace DormitoriesNotBarracks
{
    [StaticConstructorOnStartup]
    public static class DormitoriesNotBarracks
    {
		// public static string RoomRoleDefName = "Dormitory";
		// public static string ThoughtDefName = "SleptInDormitory";

		static DormitoriesNotBarracks()
        {
			Harmony harmony = new Harmony("8z.dormitoriesnotbarracks");
			harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(Toils_LayDown), "ApplyBedThoughts")]
	public static class BedThoughtsPatch
	{

		// Remove Slept in dormitory thought
		static void Prefix(Pawn actor)
		{
			if (actor.needs.mood != null)
			{
				actor.needs.mood.thoughts.memories.RemoveMemoriesOfDef(DefDatabase<ThoughtDef>.GetNamed("SleptInDormitory"));
			}
		}

		static MethodInfo GetRoom = AccessTools.Method(typeof(RegionAndRoomQuery), nameof(RegionAndRoomQuery.GetRoom));
		static MethodInfo RoomGetRole = AccessTools.Method(typeof(Room), "get_Role");
		static FieldInfo SleptInBarracks = AccessTools.Field(typeof(ThoughtDefOf), nameof(ThoughtDefOf.SleptInBarracks));

		static ThoughtDef CheckDomitoryThought(ThoughtDef def, Building_Bed bed)
        {
			if (bed != null && bed.GetRoom().Role == DefDatabase<RoomRoleDef>.GetNamed("Dormitory"))
            {
				return DefDatabase<ThoughtDef>.GetNamed("SleptInDormitory");
			}
			return def;
        }
		static MethodInfo CheckDomitoryThoughtInfo = AccessTools.Method(typeof(BedThoughtsPatch), nameof(CheckDomitoryThought));

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
			Label skipIf = il.DefineLabel();
			var counter = -1;
			foreach (var i in instructions)
            {
				if (i.opcode == OpCodes.Ldsfld && (FieldInfo) i.operand == SleptInBarracks && counter == -1)
                {
					// do something after the next instruction, before if (thoughtDef != null)
					counter = 2;
				}
				if (counter == 0)
				{
					// thoughtDef = CheckDormitoryThought(thoughtDef, bed)
#if v14
					yield return new CodeInstruction(OpCodes.Ldloc_0); // thoughtDef
					yield return new CodeInstruction(OpCodes.Ldarg_1); // bed
#else
					yield return new CodeInstruction(OpCodes.Ldloc_1); // thoughtDef
					yield return new CodeInstruction(OpCodes.Ldloc_0); // bed
#endif
					yield return new CodeInstruction(OpCodes.Call, CheckDomitoryThoughtInfo);
#if v14
					yield return new CodeInstruction(OpCodes.Stloc_0);
#else
					yield return new CodeInstruction(OpCodes.Stloc_1);
#endif
				}

				yield return i;

				if (counter > 0)
					counter--;
			}
        }

	}
}

namespace Verse
{
	public class RoomRoleWorker_Dormitory : RoomRoleWorker
	{
		private static List<Building_Bed> tmpBeds = new List<Building_Bed>();
		private static List<Pawn> children = new List<Pawn>();
		private static List<Pawn> adults = new List<Pawn>();

		public override float GetScore(Room room)
		{
			tmpBeds.Clear();
			int num = 0;
			List<Thing> containedAndAdjacentThings = room.ContainedAndAdjacentThings;
#if v14
			int num2 = 0;
			for (int i = 0; i < containedAndAdjacentThings.Count; i++)
			{
				if (containedAndAdjacentThings[i] is Building_Bed building_Bed && building_Bed.def.building.bed_humanlike
						&& building_Bed.def.building.bed_countsForBedroomOrBarracks)
				{
					if (building_Bed.ForPrisoners || building_Bed.SleepingSlotsCount > 1)
					{
						tmpBeds.Clear();
						return 0f;
					}
					tmpBeds.Add(building_Bed);
					if (building_Bed.def.building.bed_emptyCountsForBarracks)
                    {
						num2++; // Use this for actual bed counting (excludes cribs)
                    }
					num++; // Total bed count needs to be tracked for weight since barracks also track total beds
				}
			}
			bool isBedroom = RoomRoleWorker_Bedroom.IsBedroom(tmpBeds);
			tmpBeds.Clear();
			if (isBedroom || num2 < 2 || num2 > 3)
			{
				return 0f;
			}
			return (float)num * 100500f;
		}
#else
			for (int i = 0; i < containedAndAdjacentThings.Count; i++)
			{
				if (containedAndAdjacentThings[i] is Building_Bed building_Bed && building_Bed.def.building.bed_humanlike)
				{
					if (building_Bed.ForPrisoners || building_Bed.SleepingSlotsCount > 1)
					{
						tmpBeds.Clear();
						return 0f;
					}
					tmpBeds.Add(building_Bed);
					num++; // Total bed count needs to be tracked for weight since barracks also track total beds
				}
			}
			if (num < 2 || num > 3)
			{
				return 0f;
			}
			return (float)num * 100500f;
		}
#endif
	}

	public class RoomRoleWorker_PrisonDormitory : RoomRoleWorker
	{
		public override float GetScore(Room room)
		{
			int num = 0;
			int num2 = 0;
			List<Thing> containedAndAdjacentThings = room.ContainedAndAdjacentThings;
			for (int i = 0; i < containedAndAdjacentThings.Count; i++)
			{
				if (containedAndAdjacentThings[i] is Building_Bed building_Bed && building_Bed.def.building.bed_humanlike)
				{
					if (!building_Bed.ForPrisoners || building_Bed.SleepingSlotsCount > 1)
					{
						return 0f;
					}
					if (building_Bed.Medical)
					{
						num++;
					}
					else
					{
						num2++;
					}
				}
			}
			if (num2 + num <= 1 || num2 + num > 3)
			{
				return 0f;
			}
			return (float)num2 * 100500f + (float)num * 50001f;
		}
	}

	public class ThoughtWorker_PrisonDormitoryImpressiveness : ThoughtWorker_RoomImpressiveness
	{
		protected override ThoughtState CurrentStateInternal(Pawn p)
		{
			if (!p.IsPrisoner)
			{
				return ThoughtState.Inactive;
			}
			ThoughtState result = base.CurrentStateInternal(p);
			if (result.Active && p.GetRoom().Role == DefDatabase<RoomRoleDef>.GetNamed("PrisonDormitory"))
			{
				return result;
			}
			return ThoughtState.Inactive;
		}
	}

}