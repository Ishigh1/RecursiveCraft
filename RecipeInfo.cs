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

		public RecipeInfo(Solver solver, IReadOnlyCollection<Recipe> linkedRecipes)
		{
			RecipeUsed = new Dictionary<Recipe, int>();

			for (var i = 0; i < linkedRecipes.Count; i++)
			{
				int recipeUsage = (int) solver.Variable(i).SolutionValue();
				if(recipeUsage == 0)
					continue;
				Recipe recipe = linkedRecipes.ElementAt(i);
				RecipeUsed.Add(recipe, recipeUsage);
			}
		}
	}
}