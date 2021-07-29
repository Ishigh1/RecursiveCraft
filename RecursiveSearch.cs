using System.Collections.Generic;
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
		public Dictionary<Recipe, Dictionary<(VariableType variableType, int index), Constraint>> Constraints;
		public Dictionary<Recipe, Dictionary<(VariableType variableType, int index), Variable>> Variables;
		public Dictionary<Recipe, HashSet<Recipe>> LinkedRecipes;

		public static void InitializeOrTools()
		{
			NativeLibrary.SetDllImportResolver(typeof(LinearExpr).Assembly, (_, _, _) => RecursiveCraft.Ptr);
		}

		public void InitializeSolvers(Dictionary<Recipe, HashSet<Recipe>> parentRecipes)
		{
			Solvers = new Dictionary<Recipe, Solver>();
			Constraints = new Dictionary<Recipe, Dictionary<(VariableType variableType, int index), Constraint>>();
			Variables = new Dictionary<Recipe, Dictionary<(VariableType variableType, int index), Variable>>();
			LinkedRecipes = parentRecipes;
			foreach ((Recipe recipe, HashSet<Recipe> linkedRecipes) in parentRecipes)
			{
				linkedRecipes.Add(recipe);
				Solver solver = Solver.CreateSolver("SCIP");
				Solvers.Add(recipe, solver);
				Constraints.Add(recipe, new Dictionary<(VariableType variableType, int index), Constraint>());
				Variables.Add(recipe, new Dictionary<(VariableType variableType, int index), Variable>());

				LinearExpr toMaximize = new();

				foreach (Recipe linkedRecipe in linkedRecipes)
				{
					Variable variable = solver.MakeIntVar(0, int.MaxValue, "recipe " + linkedRecipe.RecipeIndex);
					Variables[recipe].Add((VariableType.Recipe, linkedRecipe.RecipeIndex), variable);
					if (linkedRecipe == recipe)
					{
						toMaximize += 100000 * variable;
						variable.SetLb(1);
					}
					else
					{
						toMaximize -= variable;
						AddItemConsumption(recipe, variable, linkedRecipe.createItem.type,
							-linkedRecipe.createItem.stack);
					}

					foreach (Item item in linkedRecipe.requiredItem)
						AddItemConsumption(recipe, variable, item.type, item.stack);
				}

				solver.Maximize(toMaximize);
			}
		}

		public void AddItemConsumption(Recipe recipe, Variable variable, int createItemType, int itemConsumed)
		{
			Solver solver = Solvers[recipe];
			Constraint constraint;
			foreach (int recipeAcceptedGroup in recipe.acceptedGroups)
			{
				HashSet<int> validItems = RecipeGroup.recipeGroups[recipeAcceptedGroup].ValidItems;
				if (validItems.Contains(createItemType))
				{
					if (!Variables[recipe].TryGetValue((VariableType.ItemGroup, recipeAcceptedGroup), out variable))
					{
						variable = solver.MakeIntVar(0, int.MaxValue, "recipe group " + recipeAcceptedGroup);
						solver.Objective().SetCoefficient(variable, -0.000001);
						constraint = solver.MakeConstraint(0, 0);
						constraint.SetCoefficient(variable, 1);

						foreach (Variable groupVar in validItems.Select(validItem =>
							solver.MakeIntVar(0, int.MaxValue, "special item " + validItem)))
						{
							if (!Constraints[recipe].TryGetValue((VariableType.Item, createItemType), out constraint))
							{
								constraint = solver.MakeConstraint(int.MinValue, 0);
								Constraints[recipe].Add((VariableType.Item, createItemType), constraint);
							}

							constraint.SetCoefficient(groupVar, -1);
						}
					}

					if (!Constraints[recipe].TryGetValue((VariableType.Item, createItemType), out constraint))
					{
						constraint = solver.MakeConstraint(int.MinValue, 0);
						Constraints[recipe].Add((VariableType.Item, createItemType), constraint);
					}

					constraint.SetCoefficient(variable, itemConsumed);

					return;
				}
			}

			if (!Constraints[recipe].TryGetValue((VariableType.Item, createItemType), out constraint))
			{
				constraint = solver.MakeConstraint(int.MinValue, 0);
				Constraints[recipe].Add((VariableType.Item, createItemType), constraint);
			}

			constraint.SetCoefficient(variable, itemConsumed);
		}

		public RecipeInfo FindIngredientsForRecipe(Recipe recipe, Dictionary<int, int> inventory, int timeCraft = 1)
		{
			if (!IsAvailable(recipe)) return null;

			Solver solver = Solvers[recipe];

			HashSet<Recipe> linkedRecipes = LinkedRecipes[recipe];
			foreach (var linkedRecipe in linkedRecipes)
			{
				Variable variable = Variables[linkedRecipe][(VariableType.Recipe, linkedRecipe.RecipeIndex)];
				if (linkedRecipe == recipe)
					variable.SetUb(timeCraft);
				else if (!IsAvailable(linkedRecipe))
					variable.SetUb(0);
				else
					variable.SetUb(int.MaxValue);
			}

			foreach (((VariableType _, int itemId), Constraint constraint) in Constraints[recipe]
				.Where(pair => pair.Key.variableType == VariableType.ItemGroup))
			{
				inventory.TryGetValue(itemId, out int availableAmount);
				constraint.SetUb(availableAmount);
			}

			Solver.ResultStatus resultStatus = solver.Solve();
			if (resultStatus != Solver.ResultStatus.OPTIMAL)
			{
				solver.Reset();
				return null;
			}

			RecipeInfo recipeInfo = new(this, recipe);
			solver.Reset();
			return recipeInfo;
		}

		public static bool IsAvailable(Recipe recipe)
		{
			return RecipeLoader.RecipeAvailable(recipe) &&
			       recipe.requiredTile.All(tile => Main.LocalPlayer.adjTile[tile]);
		}
	}
}