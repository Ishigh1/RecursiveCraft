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
			RecipeInfo = recipeInfo;
			
			createItem = OverridenRecipe.createItem;

			if (recipeInfo.UsedItems.Count > maxRequirements)
			{
				maxRequirements = recipeInfo.UsedItems.Count; //This may be a bit bigger than the needed value
				requiredItem = new Item[maxRequirements];
				requiredTile = new int[maxRequirements];
				for (int j = 0; j < maxRequirements; j++) requiredItem[j] = new Item();
			}
			
			SetRequiredItems();
			SetRequiredTiles();
		}

		public void SetRequiredItems()
		{
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

		public void SetRequiredTiles()
		{
			needWater = false;
			needLava = false;
			needHoney = false;
			needSnowBiome = false;
			Dictionary<Recipe, int> keyValuePairs = RecipeInfo.RecipeUsed;
			int i = 0;
			foreach (Recipe recipe in keyValuePairs.Select(keyValuePair => keyValuePair.Key))
			{
				needWater |= recipe.needWater;
				needLava |= recipe.needLava;
				needHoney |= recipe.needHoney;
				needSnowBiome |= recipe.needSnowBiome;
				
				foreach (int requiredTile in recipe.requiredTile)
				{
					if(requiredTile == -1) break;
					bool alreadyRequired = false;
					for (int j = 0; j < i; j++)
						if (this.requiredTile[j] == requiredTile)
						{
							alreadyRequired = true;
							break;
						}

					if (!alreadyRequired)
						this.requiredTile[i++] = requiredTile;
				}
			}

			for (; i < maxRequirements; i++) requiredTile[i] = -1;
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