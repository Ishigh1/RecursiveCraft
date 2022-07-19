using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace RecursiveCraft;

public class RecursiveSettings : ModConfig
{
	[Label("Max recursive depth")] [Tooltip("Max number of different recipes that can be used to obtain an ingredient")] [DefaultValue(-1)] [Range(-1, 5)]
	public int DefaultDepth;

	public override ConfigScope Mode => ConfigScope.ClientSide;
}