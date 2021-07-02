using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
		public static CompoundRecipe CompoundRecipe;

		public static int DepthSearch;
		public static bool InventoryIsOpen;
		public static ModKeybind[] Hotkeys;
		public static List<Func<bool>> InventoryChecks;

		public static IntPtr Ptr;

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
			
			Ptr = NativeLibrary.Load(Path.Combine(Main.SavePath, "Mod Sources", Name, "lib",
				"google-ortools-native.dll"));
		}

		public override void PostAddRecipes()
		{
			CompoundRecipe = new CompoundRecipe(this);
			RecipeByResult = new Dictionary<int, List<Recipe>>();

			foreach (Recipe recipe in Main.recipe)
			{
				int type = recipe.createItem.type;
				if (type == ItemID.None) break;

				if (!RecipeByResult.TryGetValue(type, out List<Recipe> list))
				{
					list = new List<Recipe>();
					RecipeByResult.Add(type, list);
				}

				list.Add(recipe);
			}
		}

		public override void Unload()
		{
			ILRecipe.FindRecipes -= ApplyRecursiveSearch;
			OnMain.DrawInventory -= EditFocusRecipe;
			OnMain.Update -= ApplyKey;

			RecipeInfoCache = null;
			RecipeByResult = null;

			Hotkeys = null;

			if (CompoundRecipe?.OverridenRecipe != null)
				Main.recipe[CompoundRecipe.RecipeId] = CompoundRecipe.OverridenRecipe;
			CompoundRecipe = null;

			InventoryChecks = null;

			NativeLibrary.Free(Ptr);
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
				instruction => instruction.OpCode == OpCodes.Blt_S && instruction.Previous.MatchLdcI4(40)))
				throw new Exception("The first hook on ApplyRecursiveSearch wasn't found");
			ILLabel label = cursor.DefineLabel();
			IEnumerable<ILLabel> incomingLabels = cursor.IncomingLabels.ToList();
			foreach (ILLabel cursorIncomingLabel in incomingLabels) cursor.MarkLabel(cursorIncomingLabel);

			cursor.Emit(OpCodes.Ldloc, 6);
			cursor.Emit(OpCodes.Call, typeof(RecursiveCraft).GetMethod("FindRecipes"));
			cursor.Emit(OpCodes.Br_S, label);

			// Go before 'for (int num7 = 0; num7 < Main.numAvailableRecipes; num7++)'
			if (!cursor.TryGotoNext(MoveType.Before,
				instruction => instruction.OpCode == OpCodes.Ldc_I4_0 &&
				               instruction.Previous.OpCode == OpCodes.Brtrue))
				throw new Exception("The second hook on ApplyRecursiveSearch wasn't found");

			cursor.MarkLabel(label);

			using (StreamWriter file =
				new StreamWriter(@"D:\debug.txt", true))
			{
				foreach (Instruction ilInstr in il.Instrs)
				{
					if (ilInstr.Operand is ILLabel ilLabel)
					{
						file.WriteLine("IL_" + ilInstr.Offset.ToString("x4") + ": " + ilLabel + " label to " +
						               "IL_" + ilLabel.Target.Offset.ToString("x4") + " " + ilLabel.Target.Operand);
					}
					else
					{
						try
						{
							file.WriteLine(ilInstr);
						}
						catch (Exception)
						{
							file.WriteLine("bad code : " + ilInstr.Operand + " " + ilInstr.OpCode);
						}
					}
				}
			}
		}

		public static void FindRecipes(Dictionary<int, int> inventory)
		{
			RecipeInfoCache.Clear();
			RecursiveSearch recursiveSearch = new(inventory);

			SortedSet<int> sortedAvailableRecipes = new();
			for (int i = 0; i < Main.recipe.Length; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == CompoundRecipe.Compound)
					recipe = CompoundRecipe.OverridenRecipe;
				RecipeInfo recipeInfo = recursiveSearch.FindIngredientsForRecipe(recipe);
				if (recipeInfo != null)
				{
					if (recipeInfo.RecipeUsed.Count > 1)
						RecipeInfoCache.Add(recipe, recipeInfo);
					sortedAvailableRecipes.Add(i);
				}
			}

			foreach (int availableRecipe in sortedAvailableRecipes)
				Main.availableRecipe[Main.numAvailableRecipes++] = availableRecipe;
		}
	}
}