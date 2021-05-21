using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ID;

namespace RecursiveCraft
{
	public class CompoundRecipe : ModRecipe
	{
		public RecipeInfo RecipeInfo;
		public Recipe OverridenRecipe;
		public int RecipeId;

		public CompoundRecipe(Mod mod) : base(mod)
		{
		}

		public void Apply(int recipeId, RecipeInfo recipeInfo)
		{
			RecipeId = recipeId;
			OverridenRecipe = Main.recipe[recipeId];

			if (recipeInfo.UsedItems.Count > maxRequirements)
			{
				maxRequirements = recipeInfo.UsedItems.Count; //This may be a bit bigger than the needed value
				requiredItem = new Item[maxRequirements];
				for (int j = 0; j < maxRequirements; j++) requiredItem[j] = new Item();
			}

			createItem = createItem = OverridenRecipe.createItem;
			RecipeInfo = recipeInfo;

			List<KeyValuePair<int, int>> keyValuePairs = RecipeInfo.UsedItems.ToList();
			int i = 0;
			foreach (KeyValuePair<int, int> keyValuePair in keyValuePairs.Where(keyValuePair => keyValuePair.Value > 0))
			{
				requiredItem[i].SetDefaults(keyValuePair.Key);
				requiredItem[i].stack = keyValuePair.Value;
				++i;
			}

			for (; i < maxRequirements; i++) requiredItem[i].type = ItemID.None;
		}

		public override int ConsumeItem(int type, int numRequired)
		{
			return RecipeInfo.TrueUsedItems.TryGetValue(type, out numRequired) ? numRequired : 0;
		}

		public override void OnCraft(Item item)
		{
			List<KeyValuePair<int, int>> keyValuePairs = RecipeInfo.TrueUsedItems.ToList();
			foreach (KeyValuePair<int, int> keyValuePair in keyValuePairs.Where(keyValuePair => keyValuePair.Value < 0))
				Main.LocalPlayer.QuickSpawnItem(keyValuePair.Key, -keyValuePair.Value);

			foreach (KeyValuePair<Recipe, int> keyValuePair in RecipeInfo.RecipeUsed)
				for (int i = 0; i < keyValuePair.Value; i++)
				{
					RecipeHooks.OnCraft(Main.mouseItem, keyValuePair.Key);
					ItemLoader.OnCraft(Main.mouseItem, keyValuePair.Key);
				}
		}
	}
}