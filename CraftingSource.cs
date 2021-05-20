using Terraria;

namespace RecursiveCraft
{
	public abstract class CraftingSource
	{
		public abstract bool[] AdjTile { get; }
		public abstract bool AdjWater{ get; }
		public abstract bool AdjHoney{ get; }
		public abstract bool AdjLava{ get; }
		public abstract bool ZoneSnow{ get; }
	}

	public class PlayerAsCraftingSource : CraftingSource
	{
		public override bool[] AdjTile => Main.LocalPlayer.adjTile;
		public override bool AdjWater => Main.LocalPlayer.adjWater;
		public override bool AdjHoney => Main.LocalPlayer.adjHoney;
		public override bool AdjLava => Main.LocalPlayer.adjLava;
		public override bool ZoneSnow => Main.LocalPlayer.ZoneSnow;
	}
}