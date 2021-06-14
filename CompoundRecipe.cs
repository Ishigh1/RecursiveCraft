using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace RecursiveCraft
{
	public class CompoundRecipe
	{
		public Recipe Compound;
		public Recipe OverridenRecipe;
		public int RecipeId;
		public RecipeInfo RecipeInfo;

		public CompoundRecipe(Mod mod)
		{
			Compound = mod.CreateRecipe(0);
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

			List<KeyValuePair<int, int>> usedItems = RecipeInfo.UsedItems.ToList();
			foreach ((int key, int value) in usedItems.Where(keyValuePair => keyValuePair.Value > 0))
				Compound.AddIngredient(key, value);

			Dictionary<Recipe, int> recipes = RecipeInfo.RecipeUsed;
			foreach (Recipe recipe in recipes.Select(keyValuePair => keyValuePair.Key))
			{
				Compound.AddCondition(recipe.Conditions);
				foreach (int requiredTile in recipe.requiredTile) Compound.AddTile(requiredTile);
			}
		}

		public void ConsumeItem(Recipe recipe, int type, ref int amount)
		{
			if (!RecipeInfo.TrueUsedItems.TryGetValue(type, out amount))
				amount = 0;
		}

		public void OnCraft(Recipe _, Item item)
		{
			List<KeyValuePair<int, int>> keyValuePairs = RecipeInfo.TrueUsedItems.ToList();
			foreach (KeyValuePair<int, int> keyValuePair in keyValuePairs.Where(keyValuePair => keyValuePair.Value < 0))
				Main.LocalPlayer.QuickSpawnItem(keyValuePair.Key, -keyValuePair.Value);

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