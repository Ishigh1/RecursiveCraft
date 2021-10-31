using System.Collections.Generic;

namespace RecursiveCraft.CraftingTree
{
	public class TreeItem
	{
		public int ItemId;
		public List<TreeRecipe> RecipesCreating = new();
		public List<TreeRecipe> RecipesNeeding = new();

		public TreeItem(int itemId)
		{
			ItemId = itemId;
		}
	}
}