using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace RecursiveCraft
{
	public class CompoundRecipe
	{
		public Recipe CurrentRecipe;
		public Dictionary<int, int> DropItems;
		public RecipeInfo RecipeInfo;
		public Recipe OverridenRecipe;
		public int RecipeId;

		public CompoundRecipe(int recipeId, RecipeInfo recipeInfo)
		{
			RecipeId = recipeId;
			OverridenRecipe = Main.recipe[recipeId];

			if (recipeInfo.UsedItems.Count > Recipe.maxRequirements)
				Recipe.maxRequirements = recipeInfo.UsedItems.Count;//This may be a bit bigger than the needed value 
			
			CurrentRecipe = new Recipe
			{
				createItem = OverridenRecipe.createItem
			};
			DropItems = new Dictionary<int, int>();
			RecipeInfo = recipeInfo;

			List<KeyValuePair<int, int>> keyValuePairs = recipeInfo.UsedItems.ToList();
			int i = 0;
			foreach (KeyValuePair<int, int> keyValuePair in keyValuePairs.Where(keyValuePair => keyValuePair.Value > 0))
			{
				CurrentRecipe.requiredItem[i] = new Item();
				CurrentRecipe.requiredItem[i].SetDefaults(keyValuePair.Key);
				CurrentRecipe.requiredItem[i].stack = keyValuePair.Value;
				++i;
			}
		}

		public void BeforeCraft()
		{
			List<KeyValuePair<int, int>> keyValuePairs = RecipeInfo.TrueUsedItems.ToList();
			int i = 0;
			foreach (KeyValuePair<int, int> keyValuePair in keyValuePairs)
				if (keyValuePair.Value < 0)
				{
					DropItems.Add(keyValuePair.Key, -keyValuePair.Value);
				}
				else
				{
					CurrentRecipe.requiredItem[i] = new Item();
					CurrentRecipe.requiredItem[i].SetDefaults(keyValuePair.Key);
					CurrentRecipe.requiredItem[i].stack = keyValuePair.Value;
					++i;
				}

			for (; i < RecipeInfo.UsedItems.Count; i++)
			{
				CurrentRecipe.requiredItem[i].stack = 0;
			}
		}

		public void OnCraft()
		{
			foreach (KeyValuePair<int, int> keyValuePair in DropItems)
				Main.player[Main.myPlayer].QuickSpawnItem(keyValuePair.Key, keyValuePair.Value);

			foreach (KeyValuePair<Recipe, int> keyValuePair in RecipeInfo.RecipeUsed)
				for (int i = 0; i < keyValuePair.Value; i++)
				{
					RecipeHooks.OnCraft(Main.mouseItem, keyValuePair.Key);
					ItemLoader.OnCraft(Main.mouseItem, keyValuePair.Key);
				}
		}
	}
}