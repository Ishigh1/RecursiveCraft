using System.Collections.Generic;
using System.Linq;
using Google.OrTools.LinearSolver;
using Terraria;

namespace RecursiveCraft
{
	public class RecipeInfo
	{
		public readonly Dictionary<Recipe, int> RecipeUsed;
		public readonly Dictionary<int, int> TrueUsedItems;
		public readonly Dictionary<int, int> UsedItems;

		public RecipeInfo(RecursiveSearch recursiveSearch)
		{
			RecipeUsed = new Dictionary<Recipe, int>();
			UsedItems = new Dictionary<int, int>();
			TrueUsedItems = UsedItems;
			foreach ((int key, Variable value) in recursiveSearch.Recipes)
			{
				Recipe recipe = Main.recipe[key];
				int timeCraft = (int) value.SolutionValue();
				RecipeUsed.Add(recipe, timeCraft);

				if (!UsedItems.TryAdd(recipe.createItem.type, -recipe.createItem.stack * timeCraft))
					UsedItems[recipe.createItem.type] -= recipe.createItem.stack * timeCraft;

				foreach (Item item in recipe.requiredItem.Where(item =>
					!UsedItems.TryAdd(item.type, item.stack * timeCraft)))
					UsedItems[item.type] += item.stack * timeCraft;
			}
		}
	}
}