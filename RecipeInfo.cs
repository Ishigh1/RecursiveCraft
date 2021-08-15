using System.Collections.Generic;
using Terraria;

namespace RecursiveCraft
{
	public class RecipeInfo
	{
		public readonly Dictionary<Recipe, int> RecipeUsed;

		public RecipeInfo(RecursiveSearch recursiveSearch, Recipe mainRecipe)
		{
			RecipeUsed = new Dictionary<Recipe, int>();
			
			foreach (Recipe recipe in recursiveSearch.LinkedRecipes[mainRecipe])
			{
				int recipeUsage = (int) recursiveSearch.Variables[mainRecipe][(VariableType.Recipe, recipe.RecipeIndex)].SolutionValue();
				if(recipeUsage == 0)
					continue;
				RecipeUsed.Add(recipe, recipeUsage);
			}
		}
	}
}