using System;
using System.Collections.Generic;
using System.IO;
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
				int timeCraft = (int) value.SolutionValue();
				if (timeCraft == 0)
					continue;
				Recipe recipe = Main.recipe[key];
				RecipeUsed.Add(recipe, timeCraft);

				if (!UsedItems.TryAdd(recipe.createItem.type, -recipe.createItem.stack * timeCraft))
					UsedItems[recipe.createItem.type] -= recipe.createItem.stack * timeCraft;

				foreach (Item item in recipe.requiredItem.Where(item =>
					!UsedItems.TryAdd(item.type, item.stack * timeCraft)))
					UsedItems[item.type] += item.stack * timeCraft;
			}

			if (true)

			{
				using (StreamWriter file =
					new StreamWriter(@"D:\debug.txt", true))
				{
					file.WriteLine(recursiveSearch.Solver.ExportModelAsLpFormat(false));
					foreach (var keyValuePair in recursiveSearch.Recipes)
					{
						file.WriteLine("Recipe " + keyValuePair.Key + " : " + keyValuePair.Value.SolutionValue());
					}
				}
			}
		}
	}
}