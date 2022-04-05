using System.Collections.Generic;
using Terraria;

namespace RecursiveCraft;

public class RecipeInfo
{
	public readonly Dictionary<Recipe, int> RecipeUsed;
	public readonly Dictionary<int, int> TrueUsedItems;
	public readonly Dictionary<int, int> UsedItems;

	public RecipeInfo(Dictionary<int, int> usedItems, Dictionary<int, int> trueUsedItems,
		Dictionary<Recipe, int> recipeUsed)
	{
		UsedItems = usedItems;
		TrueUsedItems = trueUsedItems;
		RecipeUsed = recipeUsed;
	}
}