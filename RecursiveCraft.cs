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
		public static Dictionary<Recipe, RecipeInfo> RecipeCache;
		public static CompoundRecipe CompoundRecipe;

		public static int DepthSearch;
		public static bool InventoryWasOpen;
		public static ModHotKey[] Hotkeys;

		public override void Load()
		{
			ILRecipe.FindRecipes += ApplyRecursiveSearch;
			OnMain.DrawInventory += EditFocusRecipe;
			OnMain.Update += ApplyKey;
			RecipeByResult = new Dictionary<int, List<Recipe>>();
			RecipeCache = new Dictionary<Recipe, RecipeInfo>();

			Hotkeys = new[]
			{
				RegisterHotKey("Infinite crafting depth", "Home"),
				RegisterHotKey("+1 crafting depth", "PageUp"),
				RegisterHotKey("-1 crafting depth", "PageDown"),
				RegisterHotKey("No crafting depth", "End")
			};
		}

		public override void Unload()
		{
			ILRecipe.FindRecipes -= ApplyRecursiveSearch;
			OnMain.DrawInventory -= EditFocusRecipe;
			OnMain.Update -= ApplyKey;
			RecipeByResult = null;
			RecipeCache = null;
			Hotkeys = null;

			if (CompoundRecipe.OverridenRecipe != null)
				Main.recipe[CompoundRecipe.RecipeId] = CompoundRecipe.OverridenRecipe;
			CompoundRecipe = null;
		}

		public override void PostAddRecipes()
		{
			CompoundRecipe = new CompoundRecipe(this);
			
			foreach (Recipe recipe in Main.recipe)
			{
				int type = recipe.createItem.type;
				if (!RecipeByResult.TryGetValue(type, out List<Recipe> list))
				{
					list = new List<Recipe>();
					RecipeByResult.Add(type, list);
				}

				list.Add(recipe);
			}
		}

		public void ApplyKey(OnMain.orig_Update orig, Main self, GameTime gameTime)
		{
			if (InventoryWasOpen != Main.playerInventory)
			{
				InventoryWasOpen = !InventoryWasOpen;
				DepthSearch = ((RecursiveSettings) GetConfig("RecursiveSettings")).DefaultDepth;
			}

			if (InventoryWasOpen)
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
			if (CompoundRecipe.OverridenRecipe != null) Main.recipe[CompoundRecipe.RecipeId] = CompoundRecipe.OverridenRecipe;
			int i = Main.availableRecipe[Main.focusRecipe];
			Recipe recipe = Main.recipe[i];
			if (RecipeCache.TryGetValue(recipe, out RecipeInfo recipeInfo))
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
			RecipeCache.Clear();
			CraftingSource craftingSource = CraftingSource.PlayerAsCraftingSource();
			for (int n = 0; n < Recipe.maxRecipes && Main.recipe[n].createItem.type != ItemID.None; n++)
			{
				Recipe recipe = Main.recipe[n];
				if (recipe is CompoundRecipe compoundRecipe)
					recipe = compoundRecipe.OverridenRecipe;
				RecipeInfo recipeInfo = RecursiveSearch.FindIngredientsForRecipe(inventory, craftingSource, recipe);
				if (recipeInfo != null)
				{
					if (recipeInfo.RecipeUsed.Count > 1)
						RecipeCache.Add(recipe, recipeInfo);
					Main.availableRecipe[Main.numAvailableRecipes++] = n;
				}
			}
		}

		
	}
}