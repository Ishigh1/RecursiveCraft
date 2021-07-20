using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Google.OrTools.LinearSolver;
using Terraria;
using Terraria.ID;
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
			for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
			{
				if (!inventory.TryGetValue(itemId, out int quantity))
					quantity = 0;
				Ingredients.Add(Solver.MakeConstraint(-quantity, double.PositiveInfinity, ItemID.Search.GetName(itemId)));
			}

			for (int index = 0; index < Main.recipe.Length; index++)
			{
				Recipe recipe = Main.recipe[index];
				if (recipe.createItem.type == ItemID.None) break;
				if (!IsAvailable(recipe)) continue;
				Variable variable = Solver.MakeIntVar(0, double.PositiveInfinity, "recipe_" + index);
				Recipes.Add(index, variable);
				Ingredients[recipe.createItem.type].SetCoefficient(variable, recipe.createItem.stack);
				foreach (Item item in recipe.requiredItem) Ingredients[item.type].SetCoefficient(variable, -item.stack);
				ToMinimize += variable;
			}
		}

		public RecipeInfo FindIngredientsForRecipe(Recipe recipe, int index, int timeCraft = 1)
		{
			if (!IsAvailable(recipe)) return null;
			Variable recipeVariable = Recipes[index];
			recipeVariable.SetUb(timeCraft);
			Solver.Maximize(recipeVariable);
			Solver.Solve();
			timeCraft = (int) recipeVariable.SolutionValue();
			if (timeCraft == 0)
			{
				recipeVariable.SetUb(double.PositiveInfinity);
				return null;
			}

			recipeVariable.SetLb(timeCraft);
			Solver.Minimize(ToMinimize);
			Solver.Solve();
			RecipeInfo recipeInfo = new(this);
			recipeVariable.SetBounds(0, double.PositiveInfinity);
			return recipeInfo;
		}

		public bool IsAvailable(Recipe recipe)
		{
			return RecipeLoader.RecipeAvailable(recipe) &&
			       recipe.requiredTile.All(tile => Main.LocalPlayer.adjTile[tile]);
		}
	}
}