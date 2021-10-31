using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RecursiveCraft.CraftingTree;
using Terraria;
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

		public static int DepthSearch;
		public static bool InventoryIsOpen;
		public static ModKeybind[] Hotkeys;
		public static List<Func<bool>> InventoryChecks;

		public override void Load()
		{
			ILRecipe.FindRecipes += ApplyRecursiveSearch;
			OnMain.DrawInventory += EditFocusRecipe;
			OnMain.Update += ApplyKey;

			RecipeInfoCache = new Dictionary<Recipe, RecipeInfo>();

			Hotkeys = new[]
			{
				KeybindLoader.RegisterKeybind(this, "Infinite crafting depth", "Home"),
				KeybindLoader.RegisterKeybind(this, "+1 crafting depth", "PageUp"),
				KeybindLoader.RegisterKeybind(this, "-1 crafting depth", "PageDown"),
				KeybindLoader.RegisterKeybind(this, "No crafting depth", "End")
			};

			InventoryChecks = new List<Func<bool>>
			{
				() => Main.playerInventory
			};
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

			if (CompoundRecipe?.OverridenRecipe != null)
				Main.recipe[CompoundRecipe.RecipeId] = CompoundRecipe.OverridenRecipe;
			CompoundRecipe = null;

			InventoryChecks = null;
		}

		public override void PostAddRecipes()
		{
			Tree tree = new();
			for (int i = 0; i < Recipe.numRecipes; i++) tree.AddRecipe(Main.recipe[i]);
			tree.AnalyseTree();
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
			RecursiveSearch recursiveSearch = new(inventory);

			SortedSet<int> sortedAvailableRecipes = new();
			foreach (int n in SortedRecipeList)
			{
				Recipe recipe = Main.recipe[n];
				if (recipe == CompoundRecipe.Compound)
					recipe = CompoundRecipe.OverridenRecipe;
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