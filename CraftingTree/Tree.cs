using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace RecursiveCraft.CraftingTree
{
	public class Tree
	{
		public int Count = 0;
		public Dictionary<int, TreeItem> Items = new();
		public SortedList<int, TreeRecipe> Recipes = new();
		public List<TreeRecipe> SortedRecipes;

		public void AddRecipe(Recipe recipe)
		{
			Recipes.Add(recipe.RecipeIndex, new TreeRecipe(this, recipe));
		}

		public TreeItem GetTreeItem(int itemId)
		{
			if (!Items.TryGetValue(itemId, out TreeItem treeItem))
			{
				treeItem = new TreeItem(itemId);
				Items.Add(itemId, treeItem);
			}

			return treeItem;
		}

		public void AnalyseTree()
		{
			foreach ((int _, TreeRecipe treeRecipe) in Recipes) AnalyseRecipe(treeRecipe);
			SortedRecipes = new List<TreeRecipe>(Recipes.Values);
			SortedRecipes.Sort((treeRecipe1, treeRecipe2) =>
				treeRecipe1.Dependencies.Count - treeRecipe2.Dependencies.Count);
		}

		public void AnalyseRecipe(TreeRecipe treeRecipe)
		{
			treeRecipe.Dependencies.Add(treeRecipe);
			for (int i = 0; i < treeRecipe.Dependencies.Count; i++)
			{
				TreeRecipe treeRecipeDependency = treeRecipe.Dependencies.ElementAt(i);
				treeRecipe.Dependencies.UnionWith(treeRecipeDependency.Dependencies);
				foreach (int tile in treeRecipe.Recipe.requiredTile)
					treeRecipe.Tiles[tile] = false;
				foreach (Recipe.Condition recipeCondition in treeRecipe.Recipe.Conditions)
					treeRecipe.Conditions[recipeCondition] = false;

				foreach ((List<TreeItem> treeItems, int _) in treeRecipeDependency.Ingredients)
				foreach (TreeItem treeItem in treeItems)
					treeRecipe.Dependencies.UnionWith(treeItem.RecipesCreating);
			}
		}
	}
}