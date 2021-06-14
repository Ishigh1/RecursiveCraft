using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace RecursiveCraft
{
	public class RecursiveSearch
	{
		public CraftingState CraftingState;
		public Dictionary<int, int> Inventory;
		public int MaxDepth;
		public Dictionary<Recipe, bool> PossibleCraftCache;

		public RecursiveSearch(Dictionary<int, int> inventory, int maxDepth)
		{
			PossibleCraftCache = new Dictionary<Recipe, bool>();
			Inventory = inventory;
			MaxDepth = maxDepth;
		}

		public RecursiveSearch(Dictionary<int, int> inventory) : this(inventory, RecursiveCraft.DepthSearch)
		{
		}

		public RecipeInfo FindIngredientsForRecipe(Recipe recipe, int timeCraft = 1)
		{
			CraftingState = new CraftingState(Inventory);
			bool craftable = CraftableAmount(recipe, recipe.createItem.stack * timeCraft,
				recipe.createItem.stack * timeCraft, new List<int>()).Item1 > 0;
			if (timeCraft == 1)
				PossibleCraftCache[recipe] = craftable;
			return !craftable ? null : CreateRecipeInfo();
		}

		public RecipeInfo CreateRecipeInfo()
		{
			Dictionary<int, int> usedItems = new();
			foreach ((int key, int value) in CraftingState.Inventory)
			{
				if (!Inventory.TryGetValue(key, out int amount))
					amount = 0;
				amount -= value;
				if (amount != 0)
					usedItems.Add(key, amount);
			}

			Dictionary<int, int> trueUsedItems = new();
			foreach ((int key, int value) in CraftingState.TrueInventory)
			{
				if (!Inventory.TryGetValue(key, out int amount))
					amount = 0;
				amount -= value;
				if (amount != 0)
					trueUsedItems.Add(key, amount);
			}

			return new RecipeInfo(usedItems, trueUsedItems, CraftingState.RecipeUsed);
		}

		public (int, int) CraftableAmount(Recipe recipe, int amount, int trueAmount, List<int> forbiddenItems)
		{
			if (!IsAvailable(recipe)) return (0, 0);
			CraftingState oldState = CraftingState;
			CraftingState = new CraftingState(oldState);

			List<int> newForbiddenItems = forbiddenItems.ToList();

			int timeCraft = (amount + recipe.createItem.stack - 1) / recipe.createItem.stack;
			int trueTimeCraft = (trueAmount + recipe.createItem.stack - 1) / recipe.createItem.stack;
			foreach (Item ingredient in recipe.requiredItem)
			{
				if (ingredient.type == ItemID.None) break;

				int ingredientsNeeded = timeCraft * ingredient.stack;
				int trueIngredientsNeeded =
					trueTimeCraft * ingredient.stack - DiscountRecipe(recipe, trueTimeCraft, ingredient);

				List<int> ingredientList = ListAllIngredient(recipe, ingredient);

				ingredientList.RemoveAll(forbiddenItems.Contains);

				UseExistingIngredients(ingredientList, ref ingredientsNeeded, ref trueIngredientsNeeded);

				if (ingredientsNeeded > 0 && MaxDepth - CraftingState.Depth != 0)
				{
					if (!newForbiddenItems.Contains(recipe.createItem.type))
						newForbiddenItems.Add(recipe.createItem.type);
					foreach (int validItem in ingredientList)
					{
						UseIngredientFromRecipe(newForbiddenItems,
							validItem, ref ingredientsNeeded, ref trueIngredientsNeeded);

						if (ingredientsNeeded <= 0)
							break;
					}
				}

				if (ingredientsNeeded > 0)
				{
					timeCraft -= (ingredientsNeeded + ingredient.stack - 1) / ingredient.stack;
					if (trueTimeCraft != 0)
						trueTimeCraft -= (ingredientsNeeded + ingredient.stack - 1) / ingredient.stack;
					break;
				}
			}

			if (timeCraft <= 0)
			{
				CraftingState = oldState;
				return (0, 0);
			}
			else if (amount > timeCraft * recipe.createItem.stack)
			{
				CraftingState = oldState;
				return CraftableAmount(recipe, timeCraft * recipe.createItem.stack,
					trueTimeCraft * recipe.createItem.stack, forbiddenItems);
			}
			else
			{
				UseItems(recipe, amount, trueAmount, timeCraft, trueTimeCraft);
				return (amount, trueAmount);
			}
		}

		public void UseIngredientFromRecipe(List<int> newForbiddenItems,
			int validItem, ref int ingredientsNeeded, ref int trueIngredientsNeeded)
		{
			if (!newForbiddenItems.Contains(validItem) &&
			    RecursiveCraft.RecipeByResult.TryGetValue(validItem, out List<Recipe> usableRecipes))
				foreach (Recipe ingredientRecipe in usableRecipes)
				{
					(int craftedAmount, int trueCraftedAmount) = CraftableAmount(ingredientRecipe, ingredientsNeeded,
						trueIngredientsNeeded, newForbiddenItems);
					ingredientsNeeded -= craftedAmount;
					trueIngredientsNeeded -= trueCraftedAmount;
					if (ingredientsNeeded <= 0) return;
				}
		}

		public static int DiscountRecipe(Recipe recipe, int trueTimeCraft, Item ingredient)
		{
			PropertyInfo propertyInfo = typeof(Recipe).GetProperty("ConsumeItemHooks", BindingFlags.Instance | BindingFlags.NonPublic);
			Recipe.ConsumeItemCallback consumeItemHooks =
				propertyInfo.GetMethod.Invoke(recipe, null) as Recipe.ConsumeItemCallback;
			int discount = 0;
			if (consumeItemHooks != null)
				for (int i = 0; i < trueTimeCraft; i++)
				{
					int consumedItems = ingredient.stack;
					consumeItemHooks.Invoke(recipe, ingredient.type, ref consumedItems);
					discount += ingredient.stack - consumedItems;
				}

			return discount;
		}

		public static List<int> ListAllIngredient(Recipe recipe, Item ingredient)
		{
			List<int> ingredientList = new();

			foreach (int validItem in recipe.acceptedGroups
				.Select(recipeAcceptedGroup => RecipeGroup.recipeGroups[recipeAcceptedGroup])
				.Where(recipeGroup => recipeGroup.ContainsItem(ingredient.netID)).SelectMany(recipeGroup =>
					recipeGroup.ValidItems.Where(validItem => !ingredientList.Contains(validItem))))
				ingredientList.Add(validItem);

			if (ingredientList.Count == 0)
				ingredientList.Add(ingredient.type);
			return ingredientList;
		}

		public void UseExistingIngredients(IEnumerable<int> ingredientList, ref int ingredientsNeeded,
			ref int trueIngredientsNeeded)
		{
			foreach (int validItem in ingredientList)
				if (CraftingState.Inventory.TryGetValue(validItem, out int availableAmount))
				{
					int usedAmount = Math.Min(ingredientsNeeded, availableAmount);
					CraftingState.Inventory[validItem] -= usedAmount;
					ingredientsNeeded -= usedAmount;

					usedAmount = Math.Min(trueIngredientsNeeded, availableAmount);
					CraftingState.TrueInventory[validItem] -= usedAmount;
					trueIngredientsNeeded -= usedAmount;

					if (ingredientsNeeded == 0)
						break;
				}
		}

		public void UseItems(Recipe recipe, int amount, int trueAmount, int timeCraft, int trueTimeCraft)
		{
			if (amount < timeCraft * recipe.createItem.stack)
			{
				if (CraftingState.Inventory.ContainsKey(recipe.createItem.type))
					CraftingState.Inventory[recipe.createItem.type] += timeCraft * recipe.createItem.stack - amount;
				else
					CraftingState.Inventory.Add(recipe.createItem.type, timeCraft * recipe.createItem.stack - amount);
			}

			if (trueAmount < trueTimeCraft * recipe.createItem.stack)
			{
				if (CraftingState.TrueInventory.ContainsKey(recipe.createItem.type))
					CraftingState.TrueInventory[recipe.createItem.type] +=
						trueTimeCraft * recipe.createItem.stack - trueAmount;
				else
					CraftingState.TrueInventory.Add(recipe.createItem.type,
						trueTimeCraft * recipe.createItem.stack - trueAmount);
			}

			if (CraftingState.RecipeUsed.ContainsKey(recipe))
				CraftingState.RecipeUsed[recipe] += trueTimeCraft;
			else
				CraftingState.RecipeUsed.Add(recipe, trueTimeCraft);
		}

		public bool IsAvailable(Recipe recipe)
		{
			if (PossibleCraftCache.TryGetValue(recipe, out bool craftable) && !craftable)
				return false;
			if (!RecipeLoader.RecipeAvailable(recipe))
				return false;
			if (recipe.requiredTile.Any(tile => !Main.LocalPlayer.adjTile[tile]))
				return false;

			return true;
		}
	}
}