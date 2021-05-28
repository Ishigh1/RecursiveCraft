using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace RecursiveCraft
{
	public class CraftingState
	{
		public int Depth;
		public Dictionary<int, int> Inventory;
		public Dictionary<Recipe, int> RecipeUsed;
		public Dictionary<int, int> TrueInventory;

		public CraftingState(Dictionary<int, int> inventory)
		{
			Inventory = inventory;
			TrueInventory = inventory;
			RecipeUsed = new Dictionary<Recipe, int>();
			Depth = -1;
		}

		public CraftingState(CraftingState oldCraftingState)
		{
			Inventory = oldCraftingState.Inventory.ToDictionary(keyValuePair => keyValuePair.Key,
				keyValuePair => keyValuePair.Value);
			TrueInventory = oldCraftingState.TrueInventory.ToDictionary(keyValuePair => keyValuePair.Key,
				keyValuePair => keyValuePair.Value);
			RecipeUsed = oldCraftingState.RecipeUsed.ToDictionary(keyValuePair => keyValuePair.Key,
				keyValuePair => keyValuePair.Value);
			Depth = oldCraftingState.Depth + 1;
		}
	}
}