using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace RecursiveCraft
{
	public class RecursiveSearch
	{
		public CraftingSource CraftingSource;
		public CraftingState CraftingState;
		public Dictionary<int, int> Inventory;
		public int MaxDepth;
		public Dictionary<Recipe, bool> PossibleCraftCache;

		public RecursiveSearch(Dictionary<int, int> inventory, CraftingSource craftingSource, int maxDepth)
		{
			PossibleCraftCache = new Dictionary<Recipe, bool>();
			Inventory = inventory;
			CraftingSource = craftingSource;
			MaxDepth = maxDepth;
		}

		public RecursiveSearch(Dictionary<int, int> inventory, CraftingSource craftingSource) : this(inventory,
			craftingSource, RecursiveCraft.DepthSearch)
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
			Dictionary<int, int> usedItems = new Dictionary<int, int>();
			foreach (KeyValuePair<int, int> keyValuePair in CraftingState.Inventory)
			{
				if (!Inventory.TryGetValue(keyValuePair.Key, out int amount))
					amount = 0;
				amount -= keyValuePair.Value;
				if (amount != 0)
					usedItems.Add(keyValuePair.Key, amount);
			}

			Dictionary<int, int> trueUsedItems = new Dictionary<int, int>();
			foreach (KeyValuePair<int, int> keyValuePair in CraftingState.TrueInventory)
			{
				if (!Inventory.TryGetValue(keyValuePair.Key, out int amount))
					amount = 0;
				amount -= keyValuePair.Value;
				if (amount != 0)
					trueUsedItems.Add(keyValuePair.Key, amount);
			}

			return new RecipeInfo(usedItems, trueUsedItems, CraftingState.RecipeUsed);
		}

		public (int, int) CraftableAmount(Recipe recipe, int amount, int trueAmount, List<int> forbiddenItems)
		{
			if (!IsAvailable(recipe)) return (0, 0);
			CraftingState oldState = CraftingState;
			CraftingState = new CraftingState(oldState);

			List<int> newForbiddenItems = forbiddenItems.ToList();

			List<int> recipeAcceptedGroups =
				(List<int>) RecursiveCraft.GetAcceptedGroups.Invoke(null, new object[] {recipe});

			int timeCraft = (amount + recipe.createItem.stack - 1) / recipe.createItem.stack;
			int trueTimeCraft = (trueAmount + recipe.createItem.stack - 1) / recipe.createItem.stack;
			foreach (Item ingredient in recipe.requiredItem)
			{
				if (ingredient.type == ItemID.None) break;

				int ingredientsNeeded = timeCraft * ingredient.stack;
				int trueIngredientsNeeded =
					trueTimeCraft * ingredient.stack - DiscountRecipe(recipe, trueTimeCraft, ingredient);

				List<int> ingredientList = ListAllIngredient(recipeAcceptedGroups, ingredient);

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

		public int DiscountRecipe(Recipe recipe, int trueTimeCraft, Item ingredient)
		{
			int discount = 0;
			if (recipe.alchemy && CraftingSource.AlchemyTable)
				for (int i = 0; i < trueTimeCraft; i++)
					if (Main.rand.Next(3) == 0)
						discount += ingredient.stack;
			return discount;
		}

		public static List<int> ListAllIngredient(IEnumerable<int> recipeAcceptedGroups, Item ingredient)
		{
			List<int> ingredientList = new List<int>();

			foreach (int validItem in recipeAcceptedGroups
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
			if (!RecipeHooks.RecipeAvailable(recipe))
				return false;
			if (recipe.requiredTile.TakeWhile(tile => tile != -1).Any(tile => !CraftingSource.AdjTile[tile]))
				return false;

			if (recipe.needWater && !CraftingSource.AdjWater &&
			    !CraftingSource.AdjTile[172])
				return false;
			if (recipe.needHoney && !CraftingSource.AdjHoney)
				return false;
			if (recipe.needLava && !CraftingSource.AdjLava)
				return false;
			if (recipe.needSnowBiome && !CraftingSource.ZoneSnow)
				return false;

			return true;
		}
	}
}