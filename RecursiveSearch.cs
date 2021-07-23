using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Google.OrTools.LinearSolver;
using Terraria;
using Terraria.ModLoader;

namespace RecursiveCraft
{
	public class RecursiveSearch
	{
		public Dictionary<Recipe, Solver> Solvers;
		public Dictionary<Recipe, Dictionary<int, Constraint>> Ingredients;
		public Dictionary<Recipe, HashSet<Recipe>> LinkedRecipes;

		public static void InitializeOrTools()
		{
			NativeLibrary.SetDllImportResolver(typeof(LinearExpr).Assembly, (_, _, _) => RecursiveCraft.Ptr);
		}

		public void InitializeSolvers(Dictionary<Recipe, HashSet<Recipe>> parentRecipes)
		{
			Solvers = new Dictionary<Recipe, Solver>();
			Ingredients = new Dictionary<Recipe, Dictionary<int, Constraint>>();
			LinkedRecipes = parentRecipes;
			foreach ((Recipe recipe, HashSet<Recipe> linkedRecipes) in parentRecipes)
			{
				linkedRecipes.Add(recipe);
				Solver solver = Solver.CreateSolver("SCIP");
				Solvers.Add(recipe, solver);
				Dictionary<int, Constraint> currentIngredients = new();
				Ingredients.Add(recipe, currentIngredients);

				solver.MakeIntVarArray(linkedRecipes.Count + 1, 0, int.MaxValue, "recipe");

				var toMinimize = new LinearExpr();

				for (int i = 0; i < linkedRecipes.Count; i++)
				{
					Recipe linkedRecipe = linkedRecipes.ElementAt(i);
					if (linkedRecipe == recipe)
						toMinimize += 100000 * solver.Variable(i);
					else
					{
						toMinimize -= solver.Variable(i);
						AddItemConsumption(currentIngredients, recipe, i, linkedRecipe.createItem.type,
							-linkedRecipe.createItem.stack);}
					foreach (Item item in linkedRecipe.requiredItem)
						AddItemConsumption(currentIngredients, recipe, i, item.type, item.stack);
				}
				solver.Minimize(toMinimize);

				using (StreamWriter file =
					new StreamWriter(@"D:\debug.txt", true))
				{
					file.WriteLine(solver.ExportModelAsLpFormat(false));
				}
			}
		}

		public void AddItemConsumption(Dictionary<int, Constraint> currentIngredients, Recipe recipe, int recipeIndex,
			int createItemType, int itemConsumed)
		{
			Solver solver = Solvers[recipe];
			if (!currentIngredients.TryGetValue(createItemType, out Constraint constraint))
			{
				constraint = solver.MakeConstraint(int.MinValue, 0);
				currentIngredients.Add(createItemType, constraint);
			}

			constraint.SetCoefficient(solver.Variable(recipeIndex), itemConsumed);
		}

		public RecipeInfo FindIngredientsForRecipe(Recipe recipe, Dictionary<int, int> inventory, int timeCraft = 1)
		{
			if (!IsAvailable(recipe)) return null;

			Solver solver = Solvers[recipe];

			HashSet<Recipe> linkedRecipes = LinkedRecipes[recipe];
			for (var i = 0; i < linkedRecipes.Count; i++)
			{
				Recipe linkedRecipe = linkedRecipes.ElementAt(i);
				if (linkedRecipe == recipe)
					solver.Variable(i).SetUb(timeCraft);
				else if (!IsAvailable(linkedRecipe))
					solver.Variable(i).SetUb(0);
				else
					solver.Variable(i).SetUb(int.MaxValue);
			}

			Dictionary<int, Constraint> ingredients = Ingredients[recipe];
			foreach ((int itemId, Constraint constraint) in ingredients)
			{
				inventory.TryGetValue(itemId, out int availableAmount);
				constraint.SetUb(availableAmount);
			}

			Solver.ResultStatus resultStatus = solver.Solve();
			return resultStatus == Solver.ResultStatus.INFEASIBLE ? null : new RecipeInfo(solver, linkedRecipes);
		}

		public static bool IsAvailable(Recipe recipe)
		{
			return RecipeLoader.RecipeAvailable(recipe) &&
			       recipe.requiredTile.All(tile => Main.LocalPlayer.adjTile[tile]);
		}
	}
}