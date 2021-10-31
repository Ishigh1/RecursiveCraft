using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace RecursiveCraft.CraftingTree
{
	public class TreeRecipe
	{
		public Dictionary<Recipe.Condition, bool> Conditions = new();
		public HashSet<TreeRecipe> Dependencies = new();
		public List<(List<TreeItem> items, int stack)> Ingredients = new();
		public Recipe Recipe;
		public (TreeItem item, int stack) Result;
		public Dictionary<int, bool> Tiles = new();

		public TreeRecipe(Tree tree, Recipe recipe)
		{
			Recipe = recipe;
			TreeItem result = tree.GetTreeItem(recipe.createItem.type);
			Result = (result, recipe.createItem.stack);
			result.RecipesCreating.Add(this);

			foreach (Item item in recipe.requiredItem)
			{
				RecipeGroup recipeGroup = recipe.acceptedGroups
					.Select(recipeAcceptedGroup => RecipeGroup.recipeGroups[recipeAcceptedGroup])
					.FirstOrDefault(group => group.ContainsItem(item.type));

				if (recipeGroup == null)
					AddIngredient(tree, item.type, item.stack);
				else
					AddIngredient(tree, recipeGroup.ValidItems, item.stack);
			}
		}

		public void AddIngredient(Tree tree, HashSet<int> itemIds, int stack)
		{
			List<TreeItem> treeItems = itemIds.Select(tree.GetTreeItem).ToList();

			foreach (TreeItem treeItem in treeItems) treeItem.RecipesNeeding.Add(this);
			Ingredients.Add((treeItems, stack));
		}

		public void AddIngredient(Tree tree, int itemId, int stack)
		{
			List<TreeItem> treeItems = new() {tree.GetTreeItem(itemId)};

			foreach (TreeItem treeItem in treeItems) treeItem.RecipesNeeding.Add(this);
			Ingredients.Add((treeItems, stack));
		}
	}
}