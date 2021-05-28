using Terraria;

namespace RecursiveCraft
{
	public class CraftingSource
	{
		public bool AdjHoney;
		public bool AdjLava;
		public bool[] AdjTile;
		public bool AdjWater;
		public bool AlchemyTable;
		public bool ZoneSnow;

		public static CraftingSource PlayerAsCraftingSource()
		{
			return new CraftingSource
			{
				AdjTile = Main.LocalPlayer.adjTile,
				AdjWater = Main.LocalPlayer.adjWater,
				AdjHoney = Main.LocalPlayer.adjHoney,
				AdjLava = Main.LocalPlayer.adjLava,
				ZoneSnow = Main.LocalPlayer.ZoneSnow,
				AlchemyTable = Main.LocalPlayer.alchemyTable
			};
		}
	}
}