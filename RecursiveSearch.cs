using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Google.OrTools.LinearSolver;
using Terraria;
using Terraria.ID;
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
			foreach ((Recipe recipe, HashSet<Recipe> parents) in parentRecipes)
			{
				Solver solver = Solver.CreateSolver("SCIP");
				Solvers.Add(recipe, solver);
				Constraints.Add(recipe, new Dictionary<(VariableType variableType, int index), Constraint>());
				Variables.Add(recipe, new Dictionary<(VariableType variableType, int index), Variable>());

				LinearExpr toMaximize = new();

				Variable variable;
				foreach (Recipe linkedRecipe in parents)
				{
					variable = solver.MakeIntVar(0, int.MaxValue, "recipe " + linkedRecipe.RecipeIndex);
					Variables[recipe].Add((VariableType.Recipe, linkedRecipe.RecipeIndex), variable);
					if (linkedRecipe == recipe)
					{
						toMaximize += 10_000_000_000 * variable;
						variable.SetLb(1);
					}
					else
					{
						toMaximize -= 100_000 * variable;
					}

					AddItemConsumption(recipe, variable, toMaximize, linkedRecipe.createItem.type,
						-linkedRecipe.createItem.stack);

					foreach (Item item in linkedRecipe.requiredItem)
						AddItemConsumption(recipe, variable, toMaximize, item.type, item.stack);
				}

				if (Variables[recipe].TryGetValue((VariableType.InventoryItem, recipe.createItem.type), out variable))
				{
					Constraint recipeEffectiveness = solver.MakeConstraint(int.MinValue, 0, "recipeEffectiveness");
					recipeEffectiveness.SetCoefficient(Variables[recipe][(VariableType.ProducedItem, recipe.createItem.type)], 1);
					recipeEffectiveness.SetCoefficient(variable, 1);
					Constraints[recipe].Add((VariableType.RecipeEffectiveness, 0), recipeEffectiveness);
				}

				solver.Maximize(toMaximize);
			}
		}

		public void AddItemConsumption(Recipe recipe, Variable variable, LinearExpr toMaximize, int itemType,
			int itemConsumed,
			bool enableGroups = true)
		{
			Solver solver = Solvers[recipe];
			if (enableGroups)
				foreach (int recipeAcceptedGroup in recipe.acceptedGroups)
				{
					HashSet<int> validItems = RecipeGroup.recipeGroups[recipeAcceptedGroup].ValidItems;
					if (validItems.Contains(itemType))
					{
						if (!Constraints[recipe].TryGetValue((VariableType.ItemGroup, recipeAcceptedGroup),
							out Constraint groupConstraint))
						{
							variable = solver.MakeIntVar(0, int.MaxValue, "recipe group " + recipeAcceptedGroup);
							toMaximize -= 1 * variable;

							groupConstraint = solver.MakeConstraint(0, 0, "recipe group " + recipeAcceptedGroup);
							groupConstraint.SetCoefficient(variable, 1);
							Constraints[recipe].Add((VariableType.ItemGroup, recipeAcceptedGroup), groupConstraint);

							foreach (int validItem in validItems)
							{
								Variable specialItem = solver.MakeIntVar(0, int.MaxValue,
									"special item " + validItem + " for group " + recipeAcceptedGroup);
								AddItemConsumption(recipe, specialItem, toMaximize, validItem, 1, false);

								groupConstraint.SetCoefficient(specialItem, -1);
							}
						}

						groupConstraint.SetCoefficient(variable, itemConsumed);

						return;
					}
				}

			if (!Constraints[recipe].TryGetValue((VariableType.ProducedItem, itemType), out Constraint constraint))
			{
				constraint = solver.MakeConstraint(0, 0);
				Constraints[recipe].Add((VariableType.ProducedItem, itemType), constraint);

				Variable item = solver.MakeIntVar(0, 0, "inventory item " + ItemID.Search.GetName(itemType));
				Variables[recipe].Add((VariableType.InventoryItem, itemType), item);
				constraint.SetCoefficient(item, 1);
				toMaximize += 2 * item;

				item = solver.MakeIntVar(int.MinValue, 0, "item produced " + ItemID.Search.GetName(itemType));
				Variables[recipe].Add((VariableType.ProducedItem, itemType), item);
				constraint.SetCoefficient(item, 1);
				toMaximize -= 1 * item;
			}

			constraint.SetCoefficient(variable, itemConsumed);
		}

		public RecipeInfo FindIngredientsForRecipe(Recipe recipe, Dictionary<int, int> inventory, int timeCraft = 1)
		{
			if (!IsAvailable(recipe)) return null;

			Solver solver = Solvers[recipe];
			if (!VerifyPreviousSolution(recipe, inventory, timeCraft))
			{
				solver.Reset();

				foreach (((VariableType variableType, int index), Variable variable) in Variables[recipe])
					switch (variableType)
					{
						case VariableType.Recipe:
							Recipe linkedRecipe = Main.recipe[index];
							if (linkedRecipe == recipe)
								variable.SetUb(timeCraft);
							else if (!IsAvailable(linkedRecipe))
								variable.SetUb(0);
							else
								variable.SetUb(int.MaxValue);
							break;
						case VariableType.InventoryItem:
							inventory.TryGetValue(index, out int availableAmount);
							variable.SetUb(availableAmount);
							break;
					}

				if (Constraints[recipe].TryGetValue((VariableType.RecipeEffectiveness, 0), out Constraint recipeEffectiveness))
					recipeEffectiveness.SetUb(-timeCraft);
			}

			Solver.ResultStatus resultStatus = solver.Solve();
			if (resultStatus != Solver.ResultStatus.OPTIMAL) return null;

			RecipeInfo recipeInfo = new(this, recipe);
			return recipeInfo;
		}

		public bool VerifyPreviousSolution(Recipe recipe, Dictionary<int, int> inventory, int timeCraft)
		{
			Solver solver = Solvers[recipe];
			bool previouslyDoable = solver.Solve() == Solver.ResultStatus.OPTIMAL;
			bool optimalPreviousObjective =
				previouslyDoable && (int) ((solver.Objective().Value() + 0.999999) / 100000) == timeCraft;
			foreach (((VariableType variableType, int index), Variable variable) in Variables[recipe])
				switch (variableType)
				{
					case VariableType.Recipe:
						Recipe linkedRecipe = Main.recipe[index];
						if (linkedRecipe == recipe)
						{
							if (variable.Ub() != timeCraft)
								return false;
						}

						else if (!IsAvailable(linkedRecipe))
						{
							if (optimalPreviousObjective && variable.SolutionValue() != 0 ||
							    !optimalPreviousObjective && variable.Ub() != 0)
								return false;
						}

						else
						{
							if (!optimalPreviousObjective && variable.Ub() != int.MaxValue)
								return false;
						}

						break;
					case VariableType.InventoryItem:
						inventory.TryGetValue(index, out int currentAmount);
						if (optimalPreviousObjective)
						{
							if (currentAmount < variable.SolutionValue()) return false;
						}
						else if (previouslyDoable)
						{
							if (currentAmount != variable.Ub()) return false;
						}
						else
						{
							if (currentAmount > variable.Ub()) return false;
						}

						break;
				}

			return true;
		}

		public static bool IsAvailable(Recipe recipe)
		{
			return RecipeLoader.RecipeAvailable(recipe) &&
			       recipe.requiredTile.All(tile => Main.LocalPlayer.adjTile[tile]);
		}
	}
}