using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace RecursiveCraft
{
	public class CompoundRecipe
	{
		public Recipe CurrentRecipe;
		public Dictionary<int, int> DropItems;
		public Recipe OverridenRecipe;
		public int RecipeId;

		public CompoundRecipe(int recipeId, Dictionary<int, int> dictionary)
		{
			RecipeId = recipeId;
			OverridenRecipe = Main.recipe[recipeId];
			CurrentRecipe = new Recipe
			{
				createItem = OverridenRecipe.createItem,
				alchemy = OverridenRecipe.alchemy
			};
			DropItems = new Dictionary<int, int>();
			
			List<KeyValuePair<int, int>> keyValuePairs = dictionary.ToList();
			keyValuePairs.Reverse();
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
		}

		public void OnCraft()
		{
			foreach (KeyValuePair<int, int> keyValuePair in DropItems)
				Main.player[Main.myPlayer].QuickSpawnItem(keyValuePair.Key, keyValuePair.Value);
		}
	}
}