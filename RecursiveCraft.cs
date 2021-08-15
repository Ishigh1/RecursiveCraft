using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ILRecipe = IL.Terraria.Recipe;
using OnMain = On.Terraria.Main;
using OnContentSamples = On.Terraria.ID.ContentSamples;

namespace RecursiveCraft
{
	public class RecursiveCraft : Mod
	{
		public static Dictionary<Recipe, RecipeInfo> RecipeInfoCache;
		public static RecursiveSearch RecursiveSearch;
		public static CompoundRecipe CompoundRecipe;

		public static bool InventoryIsOpen;
		public static ModKeybind[] Hotkeys;
		public static List<Func<bool>> InventoryChecks;

		public static IntPtr Ptr;

		public override void Load()
		{
			ILRecipe.FindRecipes += ApplyRecursiveSearch;

			OnMain.DrawInventory += EditFocusRecipe;

			OnContentSamples.RebuildItemCreativeSortingIDsAfterRecipesAreSetUp += InitializeSolvers; 

			RecipeInfoCache = new Dictionary<Recipe, RecipeInfo>();

			InventoryChecks = new List<Func<bool>>
			{
				() => Main.playerInventory
			};

			Ptr = NativeLibrary.Load(Path.Combine(Main.SavePath, "Mod Sources", Name, "lib",
				"google-ortools-native.dll"));
		}

		public override void Unload()
		{
			ILRecipe.FindRecipes -= ApplyRecursiveSearch;
			
			OnMain.DrawInventory -= EditFocusRecipe;

			OnContentSamples.RebuildItemCreativeSortingIDsAfterRecipesAreSetUp -= InitializeSolvers; 

			RecipeInfoCache = null;
			RecursiveSearch = null;

			if (CompoundRecipe?.OverridenRecipe != null)
				Main.recipe[CompoundRecipe.RecipeId] = CompoundRecipe.OverridenRecipe;
			CompoundRecipe = null;

			InventoryChecks = null;

			NativeLibrary.Free(Ptr);
		}

		public override void AddRecipes()
		{
			CompoundRecipe = new CompoundRecipe(this);
		}

		public static void InitializeSolvers(OnContentSamples.orig_RebuildItemCreativeSortingIDsAfterRecipesAreSetUp orig)
		{
			Dictionary<Recipe, HashSet<Recipe>> parentRecipes = ExtractParentRecipes();
			RecursiveSearch = new RecursiveSearch();

			RecursiveSearch.InitializeSolvers(parentRecipes);

			orig();
		}

		private static Dictionary<Recipe, HashSet<Recipe>> ExtractParentRecipes()
		{
			Dictionary<Recipe, HashSet<Recipe>> parentRecipes = new(); //Recipes in the ingredient side
			Dictionary<Recipe, HashSet<Recipe>> childrenRecipes = new(); //Recipes in the product side
			Dictionary<int, HashSet<Recipe>> itemCreators = new();
			Dictionary<int, HashSet<Recipe>> itemUsers = new();
			for (int i = 0; i < ItemLoader.ItemCount; i++)
			{
				itemCreators[i] = new HashSet<Recipe>();
				itemUsers[i] = new HashSet<Recipe>();
			}

			foreach (Recipe recipe in Main.recipe)
			{
				if (recipe.createItem.type == ItemID.None) break;

				HashSet<Recipe> parents = new();
				HashSet<Recipe> children = new();
				parents.Add(recipe);
				children.Add(recipe);
				parentRecipes.Add(recipe, parents);
				childrenRecipes.Add(recipe, children);

				#region createItem checks

				itemCreators[recipe.createItem.type].Add(recipe);
				children.UnionWith(itemUsers[recipe.createItem.type]);

				#endregion

				#region requiredItem checks

				foreach (Item item in recipe.requiredItem)
				{
					HashSet<int> ingredients = new() {item.type};
					foreach (int recipeAcceptedGroup in recipe.acceptedGroups)
					{
						HashSet<int> validItems = RecipeGroup.recipeGroups[recipeAcceptedGroup].ValidItems;
						if (validItems.Contains(item.type))
						{
							ingredients.UnionWith(validItems);
							break;
						}
					}

					foreach (int type in ingredients)
					{
						itemUsers[type].Add(recipe);
						parents.UnionWith(itemCreators[type]);
					}
				}

				#endregion

				#region enlarge children

				for (var i = 0; i < children.Count; i++)
				{
					Recipe child = children.ElementAt(i);
					if (child != recipe)
						children.UnionWith(childrenRecipes[child]);
				}

				#endregion

				#region enlarge parents

				for (var i = 0; i < parents.Count; i++)
				{
					Recipe parent = parents.ElementAt(i);
					if (parent != recipe)
						parents.UnionWith(parentRecipes[parent]);
				}

				#endregion

				#region propagate children

				foreach (Recipe child in children.Where(child => child != recipe))
					parentRecipes[child].UnionWith(parents);

				foreach (Recipe parent in parents.Where(parent => parent != recipe))
					childrenRecipes[parent].UnionWith(children);

				#endregion
			}

			return parentRecipes;
		}

		public static bool UpdateInventoryState()
		{
			bool wasOpen = InventoryIsOpen;
			InventoryIsOpen = false;
			foreach (Func<bool> inventoryCheck in InventoryChecks) InventoryIsOpen |= inventoryCheck.Invoke();

			return InventoryIsOpen == wasOpen;
		}

		public static void EditFocusRecipe(OnMain.orig_DrawInventory orig, Main self)
		{
			if (CompoundRecipe.OverridenRecipe != null)
				Main.recipe[CompoundRecipe.RecipeId] = CompoundRecipe.OverridenRecipe;
			int i = Main.availableRecipe[Main.focusRecipe];
			Recipe recipe = Main.recipe[i];
			if (RecipeInfoCache.TryGetValue(recipe, out RecipeInfo recipeInfo))
			{
				CompoundRecipe.Apply(i, recipeInfo);
				Main.recipe[i] = CompoundRecipe.Compound;
			}
			else
			{
				CompoundRecipe.OverridenRecipe = null;
			}

			orig(self);
		}

		public static void ApplyRecursiveSearch(ILContext il)
		{
			ILCursor cursor = new(il);
			/*
			 * Go before 'for (int n = 0; n < maxRecipes && Main.recipe[n].createItem.type != 0; n++)' that is after the
			 * 'for (int m = 0; m < 40; m++)' loop
			 */

			if (!cursor.TryGotoNext(MoveType.After,
				instruction => instruction.OpCode == OpCodes.Brtrue && instruction.Previous.MatchLdloc(30)))
				throw new Exception("The first hook on ApplyRecursiveSearch wasn't found");
			while (cursor.Next.MatchNop()) cursor.GotoNext();
			ILLabel label = cursor.DefineLabel();
			IEnumerable<ILLabel> incomingLabels = cursor.IncomingLabels.ToList();
			foreach (ILLabel cursorIncomingLabel in incomingLabels) cursor.MarkLabel(cursorIncomingLabel);

			cursor.Emit(OpCodes.Ldloc, 12); //Inventory
			cursor.Emit(OpCodes.Call, typeof(RecursiveCraft).GetMethod("FindRecipes"));
			cursor.Emit(OpCodes.Br_S, label);

			// Go before 'for (int num6 = 0; num6 < Main.numAvailableRecipes; num6++)'
			if (!cursor.TryGotoNext(MoveType.Before,
				instruction => instruction.OpCode == OpCodes.Ldc_I4_0 &&
				               (instruction.Previous.OpCode == OpCodes.Brtrue || instruction.Previous.MatchNop() &&
					               instruction.Previous.Previous.OpCode == OpCodes.Brtrue)))
				throw new Exception("The second hook on ApplyRecursiveSearch wasn't found");

			cursor.MarkLabel(label);
		}

		public static void FindRecipes(Dictionary<int, int> inventory)
		{
			RecipeInfoCache.Clear();

			SortedSet<int> sortedAvailableRecipes = new();
			for (int index = 0; index < Recipe.numRecipes; index++)
			{
				Recipe r = Main.recipe[index];
				Recipe recipe = r == CompoundRecipe.Compound ? CompoundRecipe.OverridenRecipe : r;
				RecipeInfo recipeInfo = RecursiveSearch.FindIngredientsForRecipe(recipe, inventory);
				if (recipeInfo != null)
				{
					if (recipeInfo.RecipeUsed.Count > 1)
						RecipeInfoCache.Add(recipe, recipeInfo);
					sortedAvailableRecipes.Add(recipe.RecipeIndex);
				}
			}

			foreach (int availableRecipe in sortedAvailableRecipes)
				Main.availableRecipe[Main.numAvailableRecipes++] = availableRecipe;
		}
	}
}