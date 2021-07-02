using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Google.OrTools.LinearSolver;
using Terraria;
using Terraria.ModLoader;

namespace RecursiveCraft
{
	public class RecursiveSearch
	{
		public CraftingState CraftingState;
		public List<Constraint> Ingredients;
		public Dictionary<int, Variable> Recipes;
		public Solver Solver;
		public LinearExpr ToMinimize;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public RecursiveSearch(Dictionary<int, int> inventory, int maxDepth)
		{
			Solver = Solver.CreateSolver("GLOP");
			ToMinimize = new LinearExpr();
			Ingredients = new List<Constraint>();
			Recipes = new Dictionary<int, Variable>();
			PrepareSolver(inventory);
		}
		
		[MethodImpl(MethodImplOptions.NoInlining)]
		public RecursiveSearch(Dictionary<int, int> inventory) : this(inventory, RecursiveCraft.DepthSearch)
		{
		}

		public static void InitializeOrTools()
		{
			NativeLibrary.SetDllImportResolver(typeof(LinearExpr).Assembly,
				(name, assembly, path) => RecursiveCraft.Ptr);
		}

		public void PrepareSolver(Dictionary<int, int> inventory)
		{
			Solver.SuppressOutput();

			for (int i = 0; i < ItemLoader.ItemCount; i++)
			{
				if (!inventory.TryGetValue(i, out int quantity))
					quantity = 0;
				Ingredients.Add(Solver.MakeConstraint(-quantity, double.PositiveInfinity, i.ToString()));
			}

			foreach (Recipe recipe in Main.recipe)
			{
				if (recipe.createItem.type == -1) break;
				if (!IsAvailable(recipe)) continue;
				Variable variable = Solver.MakeIntVar(0, double.PositiveInfinity, recipe.RecipeIndex.ToString());
				Recipes.Add(recipe.RecipeIndex, variable);
				Ingredients[recipe.createItem.type].SetCoefficient(variable, recipe.createItem.stack);
				foreach (Item item in recipe.requiredItem) Ingredients[item.type].SetCoefficient(variable, -item.stack);
				ToMinimize += variable;
			}
		}

		public RecipeInfo FindIngredientsForRecipe(Recipe recipe, int timeCraft = 1)
		{
			if (!IsAvailable(recipe)) return null;
			Variable recipeVariable = Recipes[recipe.RecipeIndex];
			recipeVariable.SetUb(timeCraft);
			Solver.Maximize(recipeVariable);
			Solver.Solve();
			timeCraft = (int) recipeVariable.SolutionValue();
			if (timeCraft == 0)
			{
				recipeVariable.SetUb(double.PositiveInfinity);
				return null;
			}

			recipeVariable.SetBounds(timeCraft, timeCraft);

			Solver.Minimize(ToMinimize);
			Solver.Solve();
			RecipeInfo recipeInfo = new(this);
			recipeVariable.SetUb(double.PositiveInfinity);
			return recipeInfo;
		}

		public bool IsAvailable(Recipe recipe)
		{
			return RecipeLoader.RecipeAvailable(recipe) &&
			       recipe.requiredTile.All(tile => Main.LocalPlayer.adjTile[tile]);
		}
	}
}