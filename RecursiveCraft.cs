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
				Recipe recipe = Main.recipe[n];
				if (recipe is CompoundRecipe compoundRecipe)
					recipe = compoundRecipe.OverridenRecipe;
				RecipeInfo recipeInfo = FindIngredientsForRecipe(inventory, craftingSource, recipe);
				if (recipeInfo != null)
				{
					if (recipeInfo.RecipeUsed.Count > 1)
						RecipeCache.Add(recipe, recipeInfo);
					Main.availableRecipe[Main.numAvailableRecipes++] = n;
				}
			}
		}

		public static RecipeInfo FindIngredientsForRecipe(Dictionary<int, int> dictionary,
			CraftingSource craftingSource, Recipe recipe)
		{
			Dictionary<int, int> inventoryToUse = new Dictionary<int, int>(dictionary);
			Dictionary<int, int> inventoryOnceUsed = inventoryToUse;
			Dictionary<int, int> trueInventoryOnceUsed = inventoryToUse;
			Dictionary<Recipe, int> recipeUsed = new Dictionary<Recipe, int>();
			List<int> craftedItems = new List<int>();

			if (AmountOfDoableRecipe(ref inventoryOnceUsed, ref trueInventoryOnceUsed, ref recipeUsed, craftingSource,
				recipe.createItem.stack, recipe.createItem.stack, recipe, craftedItems, 0) == 0) return null;

			Dictionary<int, int> usedItems = new Dictionary<int, int>();
			foreach (KeyValuePair<int, int> keyValuePair in inventoryOnceUsed)
			{
				if (!inventoryToUse.TryGetValue(keyValuePair.Key, out int amount))
					amount = 0;
				amount -= keyValuePair.Value;
				if (amount != 0)
					usedItems.Add(keyValuePair.Key, amount);
			}

			Dictionary<int, int> trueUsedItems = new Dictionary<int, int>();
			foreach (KeyValuePair<int, int> keyValuePair in trueInventoryOnceUsed)
			{
				if (!inventoryToUse.TryGetValue(keyValuePair.Key, out int amount))
					amount = 0;
				amount -= keyValuePair.Value;
				if (amount != 0)
					trueUsedItems.Add(keyValuePair.Key, amount);
			}

			return new RecipeInfo(usedItems, trueUsedItems, recipeUsed);
		}

		//Every "true" variable is the same as the normal one except when alchemy table is used,
		//saving ingredients while not showing it to the player.
		public static int AmountOfDoableRecipe(ref Dictionary<int, int> inventoryToUse,
			ref Dictionary<int, int> trueInventoryToUse, ref Dictionary<Recipe, int> recipeUsed,
			CraftingSource craftingSource, int amount, int trueAmount, Recipe recipe, List<int> craftedItems, int depth)
		{
			if (!IsAvailable(recipe, craftingSource)) return 0;

			Dictionary<int, int> inventoryOnceUsed =
				inventoryToUse.ToDictionary(keyValuePair => keyValuePair.Key, keyValuePair => keyValuePair.Value);
			Dictionary<int, int> trueInventoryOnceUsed =
				trueInventoryToUse.ToDictionary(keyValuePair => keyValuePair.Key, keyValuePair => keyValuePair.Value);
			Dictionary<Recipe, int> currentRecipeUsed =
				recipeUsed.ToDictionary(keyValuePair => keyValuePair.Key, keyValuePair => keyValuePair.Value);
			List<int> craftedItemsOnceUsed = craftedItems.ToList();
			if (!craftedItemsOnceUsed.Contains(recipe.createItem.type))
				craftedItemsOnceUsed.Add(recipe.createItem.type);

			MethodInfo getAcceptedGroups =
				typeof(RecipeFinder).GetMethod("GetAcceptedGroups", BindingFlags.NonPublic | BindingFlags.Static);
			List<int> recipeAcceptedGroups = (List<int>) getAcceptedGroups.Invoke(null, new object[] {recipe});

			int timeCraft = (amount + recipe.createItem.stack - 1) / recipe.createItem.stack;
			int trueTimeCraft = (trueAmount + recipe.createItem.stack - 1) / recipe.createItem.stack;
			for (int numIngredient = 0; numIngredient < Recipe.maxRequirements; numIngredient++)
			{
				Item ingredient = recipe.requiredItem[numIngredient];
				if (ingredient.type == ItemID.None) break;

				int ingredientsNeeded = timeCraft * ingredient.stack;
				int trueIngredientsNeeded = trueTimeCraft * ingredient.stack;
				if (recipe.alchemy && craftingSource.AlchemyTable)
					for (int i = 0; i < trueTimeCraft; i++)
						if (Main.rand.Next(3) == 0)
							trueIngredientsNeeded -= ingredient.stack;

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

						usedAmount = Math.Min(trueIngredientsNeeded, availableAmount);
						trueInventoryOnceUsed[validItem] -= usedAmount;
						trueIngredientsNeeded -= usedAmount;

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
									ingredientsNeeded -= AmountOfDoableRecipe(ref inventoryOnceUsed,
										ref trueInventoryOnceUsed, ref currentRecipeUsed, craftingSource,
										ingredientsNeeded, trueIngredientsNeeded, ingredientRecipe,
										craftedItemsOnceUsed, depth + 1);
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
				trueTimeCraft = Math.Min(timeCraft, trueTimeCraft);
				return AmountOfDoableRecipe(ref inventoryToUse, ref trueInventoryToUse, ref currentRecipeUsed,
					craftingSource, timeCraft * recipe.createItem.stack, trueTimeCraft * recipe.createItem.stack,
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

				if (trueAmount < trueTimeCraft * recipe.createItem.stack)
				{
					if (trueInventoryOnceUsed.ContainsKey(recipe.createItem.type))
						trueInventoryOnceUsed[recipe.createItem.type] +=
							trueTimeCraft * recipe.createItem.stack - trueAmount;
					else
						trueInventoryOnceUsed.Add(recipe.createItem.type,
							trueTimeCraft * recipe.createItem.stack - trueAmount);
				}

				if (currentRecipeUsed.ContainsKey(recipe))
					currentRecipeUsed[recipe] += trueTimeCraft;
				else
					currentRecipeUsed.Add(recipe, trueTimeCraft);

				inventoryToUse = inventoryOnceUsed;
				trueInventoryToUse = trueInventoryOnceUsed;
				recipeUsed = currentRecipeUsed;
				return amount;
			}
		}

		public static bool IsAvailable(Recipe recipe, CraftingSource craftingSource)
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