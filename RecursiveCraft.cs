using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ILRecipe = IL.Terraria.Recipe;
using OnMain = On.Terraria.Main;

namespace RecursiveCraft
{
	public class RecursiveCraft : Mod
	{
		public static Dictionary<int, List<Recipe>> RecipeByResult;
		public static Dictionary<Recipe, RecipeInfo> RecipeInfoCache;
		public static List<int> SortedRecipeList;
		public static CompoundRecipe CompoundRecipe;
		public static MethodInfo GetAcceptedGroups;

		public static int DepthSearch;
		public static bool InventoryIsOpen;
		public static ModHotKey[] Hotkeys;
		public static List<Func<bool>> InventoryChecks;

		public override void Load()
		{
			ILRecipe.FindRecipes += ApplyRecursiveSearch;
			OnMain.DrawInventory += EditFocusRecipe;
			OnMain.Update += ApplyKey;

			RecipeInfoCache = new Dictionary<Recipe, RecipeInfo>();

			Hotkeys = new[]
			{
				RegisterHotKey("Infinite crafting depth", "Home"),
				RegisterHotKey("+1 crafting depth", "PageUp"),
				RegisterHotKey("-1 crafting depth", "PageDown"),
				RegisterHotKey("No crafting depth", "End")
			};

			InventoryChecks = new List<Func<bool>>
			{
				() => Main.playerInventory
			};

			GetAcceptedGroups =
				typeof(RecipeFinder).GetMethod("GetAcceptedGroups", BindingFlags.NonPublic | BindingFlags.Static);
		}

		public override void Unload()
		{
			ILRecipe.FindRecipes -= ApplyRecursiveSearch;
			OnMain.DrawInventory -= EditFocusRecipe;
			OnMain.Update -= ApplyKey;

			RecipeInfoCache = null;
			RecipeByResult = null;
			SortedRecipeList = null;

			Hotkeys = null;

			if (CompoundRecipe.OverridenRecipe != null)
				Main.recipe[CompoundRecipe.RecipeId] = CompoundRecipe.OverridenRecipe;
			CompoundRecipe = null;

			InventoryChecks = null;

			GetAcceptedGroups = null;
		}

		public override void PostAddRecipes()
		{
			CompoundRecipe = new CompoundRecipe(this);
			RecipeByResult = new Dictionary<int, List<Recipe>>();

			Dictionary<int, int> ingredientsNeeded = new Dictionary<int, int>();
			Dictionary<Recipe, int> correspondingId = new Dictionary<Recipe, int>();

			for (int index = 0; index < Recipe.maxRecipes; index++)
			{
				Recipe recipe = Main.recipe[index];
				int type = recipe.createItem.type;
				if (type == ItemID.None) break;

				correspondingId[recipe] = index;

				if (!RecipeByResult.TryGetValue(type, out List<Recipe> list))
				{
					list = new List<Recipe>();
					RecipeByResult.Add(type, list);
					if (!ingredientsNeeded.ContainsKey(type))
						ingredientsNeeded[type] = 0;
				}

				list.Add(recipe);

				foreach (Item ingredient in recipe.requiredItem)
				{
					if (ingredient.type == ItemID.None) break;
					List<int> recipeAcceptedGroups = (List<int>) GetAcceptedGroups.Invoke(null, new object[] {recipe});
					foreach (int possibleIngredient in RecursiveSearch.ListAllIngredient(recipeAcceptedGroups,
						ingredient))
						if (ingredientsNeeded.TryGetValue(possibleIngredient, out int timesUsed))
							ingredientsNeeded[possibleIngredient] = timesUsed + 1;
						else
							ingredientsNeeded[possibleIngredient] = 1;
				}
			}

			List<KeyValuePair<int, int>> sortedIngredientList = ingredientsNeeded.ToList();
			sortedIngredientList.Sort((pair, valuePair) => valuePair.Value - pair.Value);
			SortedRecipeList = new List<int>();
			foreach (KeyValuePair<int, int> keyValuePair in sortedIngredientList)
				if (RecipeByResult.TryGetValue(keyValuePair.Key, out List<Recipe> recipes))
					foreach (Recipe recipe in recipes)
						SortedRecipeList.Add(correspondingId[recipe]);
		}

		public static bool UpdateInventoryState()
		{
			bool wasOpen = InventoryIsOpen;
			InventoryIsOpen = false;
			foreach (Func<bool> inventoryCheck in InventoryChecks) InventoryIsOpen |= inventoryCheck.Invoke();

			return InventoryIsOpen == wasOpen;
		}

		public void ApplyKey(OnMain.orig_Update orig, Main self, GameTime gameTime)
		{
			if (UpdateInventoryState())
				DepthSearch = ((RecursiveSettings) GetConfig("RecursiveSettings")).DefaultDepth;

			if (InventoryIsOpen)
			{
				int oldDepth = DepthSearch;
				if (Hotkeys[0].JustPressed)
				{
					DepthSearch = -1;
				}
				else if (Hotkeys[1].JustPressed)
				{
					if (DepthSearch == -1)
						DepthSearch = 5;
					else
						DepthSearch++;
				}
				else if (Hotkeys[2].JustPressed)
				{
					if (DepthSearch == 0)
						DepthSearch = 0;
					else if (DepthSearch == 5)
						DepthSearch = -1;
					else
						DepthSearch++;
				}
				else if (Hotkeys[3].JustPressed)
				{
					DepthSearch = 0;
				}

				if (oldDepth != DepthSearch)
					Recipe.FindRecipes();
			}

			orig(self, gameTime);
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
				Main.recipe[i] = CompoundRecipe;
			}
			else
			{
				CompoundRecipe.OverridenRecipe = null;
			}

			orig(self);
		}

		public static void ApplyRecursiveSearch(ILContext il)
		{
			ILCursor cursor = new ILCursor(il);
			if (!cursor.TryGotoNext(MoveType.After,
				instruction => instruction.OpCode == OpCodes.Blt_S && instruction.Previous.MatchLdcI4(40)))
				return;
			ILLabel label = cursor.DefineLabel();
			IEnumerable<ILLabel> incomingLabels = cursor.IncomingLabels.ToList();
			foreach (ILLabel cursorIncomingLabel in incomingLabels) cursor.MarkLabel(cursorIncomingLabel);

			cursor.Emit(OpCodes.Ldloc, 6);
			cursor.Emit(OpCodes.Call, typeof(RecursiveCraft).GetMethod("FindRecipes"));
			cursor.Emit(OpCodes.Br_S, label);
			if (!cursor.TryGotoNext(MoveType.Before,
				instruction => instruction.OpCode == OpCodes.Ldc_I4_0 && instruction.Previous.OpCode == OpCodes.Brtrue))
			{
				cursor.Index -= 3;
				cursor.RemoveRange(3);
				foreach (ILLabel cursorIncomingLabel in incomingLabels) cursor.MarkLabel(cursorIncomingLabel);

				return;
			}

			cursor.MarkLabel(label);
		}

		public static void FindRecipes(Dictionary<int, int> inventory)
		{
			RecipeInfoCache.Clear();
			RecursiveSearch recursiveSearch = new RecursiveSearch(inventory, CraftingSource.PlayerAsCraftingSource());

			SortedSet<int> sortedAvailableRecipes = new SortedSet<int>();
			foreach (int n in SortedRecipeList)
			{
				Recipe recipe = Main.recipe[n];
				if (recipe is CompoundRecipe compoundRecipe)
					recipe = compoundRecipe.OverridenRecipe;
				RecipeInfo recipeInfo = recursiveSearch.FindIngredientsForRecipe(recipe);
				if (recipeInfo != null)
				{
					if (recipeInfo.RecipeUsed.Count > 1)
						RecipeInfoCache.Add(recipe, recipeInfo);
					sortedAvailableRecipes.Add(n);
				}
			}

			foreach (int availableRecipe in sortedAvailableRecipes)
				Main.availableRecipe[Main.numAvailableRecipes++] = availableRecipe;
		}
	}
}