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
		public static Dictionary<Recipe, Dictionary<int, int>> RecipeCache;
		public static CompoundRecipe CurrentCompound;

		public static int DepthSearch;
		public static bool InventoryWasOpen;
		public static ModHotKey[] Hotkeys;

		public override void Load()
		{
			ILRecipe.FindRecipes += ApplyRecursiveSearch;
			OnMain.DrawInventory += EditFocusRecipe;
			OnMain.Update += ApplyKey;
			On.Terraria.Recipe.Create += CraftCompoundRecipe;
			RecipeByResult = new Dictionary<int, List<Recipe>>();
			RecipeCache = new Dictionary<Recipe, Dictionary<int, int>>();

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
			On.Terraria.Recipe.Create -= CraftCompoundRecipe;
			RecipeByResult = null;
			RecipeCache = null;
			Hotkeys = null;

			if (CurrentCompound != null)
				Main.recipe[CurrentCompound.RecipeId] = CurrentCompound.OverridenRecipe;
			CurrentCompound = null;
		}

		public override void PostAddRecipes()
		{
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

		public static void CraftCompoundRecipe(On.Terraria.Recipe.orig_Create orig, Recipe self)
		{
			orig(self);
			if (CurrentCompound != null && self == CurrentCompound.CurrentRecipe)
			{
				CurrentCompound.OnCraft();
				Recipe.FindRecipes();
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
			if (CurrentCompound != null) Main.recipe[CurrentCompound.RecipeId] = CurrentCompound.OverridenRecipe;
			int i = Main.availableRecipe[Main.focusRecipe];
			Recipe recipe = Main.recipe[i];
			if (RecipeCache.TryGetValue(recipe, out Dictionary<int, int> dictionary))
			{
				CurrentCompound = new CompoundRecipe(i, dictionary);
				Main.recipe[i] = CurrentCompound.CurrentRecipe;
			}
			else
			{
				CurrentCompound = null;
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
			cursor.Emit(OpCodes.Call, typeof(RecursiveCraft).GetMethod("RecursiveSearch"));
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

		public static void RecursiveSearch(Dictionary<int, int> inventory)
		{
			RecipeCache.Clear();
			CraftingSource craftingSource = new PlayerAsCraftingSource();
			for (int n = 0; n < Recipe.maxRecipes && Main.recipe[n].createItem.type != ItemID.None; n++)
			{
				Recipe recipe = CurrentCompound?.RecipeId == n ? CurrentCompound.OverridenRecipe : Main.recipe[n];
				Dictionary<int, int> usedItems = FindIngredientsForRecipe(inventory, craftingSource, recipe);
				if (usedItems != null)
				{
					RecipeCache.Add(recipe, usedItems);
					Main.availableRecipe[Main.numAvailableRecipes++] = n;
				}
			}
		}

		public static Dictionary<int, int> FindIngredientsForRecipe(Dictionary<int, int> dictionary,
			CraftingSource craftingSource, Recipe recipe)
		{
			Dictionary<int, int> inventoryToUse = new Dictionary<int, int>(dictionary);
			Dictionary<int, int> inventoryOnceUsed = inventoryToUse;
			List<int> craftedItems = new List<int>();

			if (AmountOfDoableRecipe(ref inventoryOnceUsed, craftingSource, recipe.createItem.stack, recipe,
				craftedItems, 0) == 0) return null;

			Dictionary<int, int> usedItems = new Dictionary<int, int>();
			foreach (KeyValuePair<int, int> keyValuePair in inventoryOnceUsed)
			{
				if (!inventoryToUse.TryGetValue(keyValuePair.Key, out int amount))
					amount = 0;
				amount -= keyValuePair.Value;
				if (amount != 0)
					usedItems.Add(keyValuePair.Key, amount);
			}

			return usedItems;
		}

		public static int AmountOfDoableRecipe(ref Dictionary<int, int> inventoryToUse, CraftingSource craftingSource,
			int amount, Recipe recipe, List<int> craftedItems, int depth)
		{
			if (!IsAvailable(recipe, craftingSource)) return 0;

			Dictionary<int, int> inventoryOnceUsed =
				inventoryToUse.ToDictionary(keyValuePair => keyValuePair.Key, keyValuePair => keyValuePair.Value);
			List<int> craftedItemsOnceUsed = craftedItems.ToList();
			if (!craftedItemsOnceUsed.Contains(recipe.createItem.type))
				craftedItemsOnceUsed.Add(recipe.createItem.type);

			MethodInfo getAcceptedGroups =
				typeof(RecipeFinder).GetMethod("GetAcceptedGroups", BindingFlags.NonPublic | BindingFlags.Static);
			List<int> recipeAcceptedGroups = (List<int>) getAcceptedGroups.Invoke(null, new object[] {recipe});

			int timeCraft = (amount + recipe.createItem.stack - 1) / recipe.createItem.stack;
			for (int numIngredient = 0; numIngredient < Recipe.maxRequirements; numIngredient++)
			{
				Item ingredient = recipe.requiredItem[numIngredient];
				if (ingredient.type == ItemID.None) break;

				int ingredientsNeeded = timeCraft * ingredient.stack;

				#region UseIngredients

				List<int> ingredientList = new List<int>();

				#region ListAllPossibleIngredients

				foreach (int validItem in recipeAcceptedGroups
					.Select(recipeAcceptedGroup => RecipeGroup.recipeGroups[recipeAcceptedGroup])
					.Where(recipeGroup => recipeGroup.ContainsItem(ingredient.netID)).SelectMany(recipeGroup =>
						recipeGroup.ValidItems.Where(validItem => !ingredientList.Contains(validItem))))
					ingredientList.Add(validItem);

				if (ingredientList.Count == 0)
					ingredientList.Add(ingredient.type);

				#endregion

				if (depth != 0)
					ingredientList.RemoveAll(craftedItems.Contains);

				foreach (int validItem in ingredientList)
					if (inventoryOnceUsed.TryGetValue(validItem, out int availableAmount))
					{
						int usedAmount = Math.Min(ingredientsNeeded, availableAmount);
						inventoryOnceUsed[validItem] -= usedAmount;
						ingredientsNeeded -= usedAmount;

						if (ingredientsNeeded == 0)
							break;
					}

				#endregion

				if (ingredientsNeeded > 0)
				{
					#region Recursive part

					if (DepthSearch - depth != 0)
						foreach (int validItem in ingredientList)
						{
							if (!craftedItemsOnceUsed.Contains(validItem) &&
							    RecipeByResult.TryGetValue(validItem, out List<Recipe> usableRecipes))
								foreach (Recipe ingredientRecipe in usableRecipes)
								{
									ingredientsNeeded -= AmountOfDoableRecipe(ref inventoryOnceUsed, craftingSource,
										ingredientsNeeded, ingredientRecipe, craftedItemsOnceUsed, depth + 1);
									if (ingredientsNeeded <= 0)
										break;
								}

							if (ingredientsNeeded <= 0)
								break;
						}

					#endregion

					if (ingredientsNeeded > 0)
					{
						timeCraft -= (ingredientsNeeded + ingredient.stack - 1) / ingredient.stack;
						break;
					}
				}
			}

			if (timeCraft <= 0)
			{
				return 0;
			}
			else if (amount > timeCraft * recipe.createItem.stack)
			{
				return AmountOfDoableRecipe(ref inventoryToUse, craftingSource, timeCraft * recipe.createItem.stack,
					recipe, craftedItems, depth);
			}
			else
			{
				if (amount < timeCraft * recipe.createItem.stack)
				{
					if (inventoryOnceUsed.ContainsKey(recipe.createItem.type))
						inventoryOnceUsed[recipe.createItem.type] += timeCraft * recipe.createItem.stack - amount;
					else
						inventoryOnceUsed.Add(recipe.createItem.type, timeCraft * recipe.createItem.stack - amount);
				}

				inventoryToUse = inventoryOnceUsed;
				return amount;
			}
		}

		private static bool IsAvailable(Recipe recipe, CraftingSource craftingSource)
		{
			if (!RecipeHooks.RecipeAvailable(recipe))
				return false;
			for (int craftingStation = 0;
				craftingStation < Recipe.maxRequirements && recipe.requiredTile[craftingStation] != -1;
				craftingStation++)
				if (!craftingSource.AdjTile[recipe.requiredTile[craftingStation]])
					return false;

			if (recipe.needWater && !craftingSource.AdjWater &&
			    !craftingSource.AdjTile[172])
				return false;
			if (recipe.needHoney && !craftingSource.AdjHoney)
				return false;
			if (recipe.needLava && !craftingSource.AdjLava)
				return false;
			if (recipe.needSnowBiome && !craftingSource.ZoneSnow)
				return false;

			return true;
		}
	}
}