using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace RecursiveCraft
{
	public class CompoundRecipe
	{
		public Recipe Compound;
		public Recipe OverridenRecipe;
		public RecipeInfo RecipeInfo;
		public int RecipeId;
		public Dictionary<int, int> TrueUsedItems;

		public CompoundRecipe(Mod mod)
		{
			Compound = mod.CreateRecipe(ItemID.None);
			Compound.AddConsumeItemCallback(ConsumeItem);
			Compound.AddOnCraftCallback(OnCraft);
		}

		public void Apply(int recipeId, RecipeInfo recipeInfo)
		{
			RecipeId = recipeId;
			OverridenRecipe = Main.recipe[recipeId];
			RecipeInfo = recipeInfo;

			Compound.requiredItem.Clear();
			Compound.Conditions.Clear();
			Compound.requiredTile.Clear();
			Compound.ReplaceResult(OverridenRecipe.createItem.type,
				OverridenRecipe.createItem.stack * recipeInfo.RecipeUsed[OverridenRecipe]);

			TrueUsedItems = new Dictionary<int, int>();
			foreach ((Recipe recipe, int timeCraft) in recipeInfo.RecipeUsed)
			{
				Compound.AddCondition(recipe.Conditions);
				foreach (int tileId in recipe.requiredTile) Compound.AddTile(tileId);
				if (recipe == OverridenRecipe)
					continue;
				AddIngredient(recipe, recipe.createItem.type, -recipe.createItem.stack * timeCraft);
				foreach (Item item in recipe.requiredItem)
					AddIngredient(recipe, item.type, item.stack * timeCraft);
			}

			Compound.requiredItem.RemoveAll(item => item.stack <= 0);
		}

		public void AddIngredient(Recipe recipe, int itemId, int stack)
		{
			if (Compound.TryGetIngredient(itemId, out Item ingredient))
				ingredient.stack += stack;
			else
				Compound.AddIngredient(itemId, stack);

			if (stack > 0) RecipeLoader.ConsumeItem(recipe, itemId, ref stack);

			TrueUsedItems.TryGetValue(itemId, out int amount);
			TrueUsedItems[itemId] = amount + stack;
		}

		public void ConsumeItem(Recipe recipe, int itemId, ref int amount)
		{
			if (!TrueUsedItems.TryGetValue(itemId, out amount))
				amount = 0;
		}

		public void OnCraft(Recipe _, Item item)
		{
			foreach ((int itemId, int stack) in TrueUsedItems.Where(keyValuePair => keyValuePair.Value < 0))
				Main.LocalPlayer.QuickSpawnItem(itemId, -stack);

			List<KeyValuePair<Recipe, int>> recipes = RecipeInfo.RecipeUsed.ToList();
			for (int i = 0; i < recipes.Count; i++)
			{
				Recipe recipe = recipes[i].Key;
				int timesCrafted = recipes[i].Value;

				Item targetItem;
				if (i == 0)
				{
					targetItem = item;
				}
				else
				{
					targetItem = new Item();
					targetItem.SetDefaults(recipe.createItem.type);
					targetItem.stack = recipe.createItem.stack;
				}

				for (int j = 0; j < timesCrafted; j++)
					RecipeLoader.OnCraft(targetItem, recipe);

				//This still doesn't take into account any OnCraft editing intermediate recipe result
			}
		}
	}
}