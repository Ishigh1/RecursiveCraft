using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using ILRecipe = IL.Terraria.Recipe;
using OnMain = On.Terraria.Main;

namespace RecursiveCraft
{
	public class RecursiveCraft : Mod
	{
		public static Dictionary<int, List<Recipe>> recipeByResult;
		public static Dictionary<Recipe, Dictionary<int, int>> recipeCache;
		public static CompoundRecipe currentCompound;

		public static int depthSearch;
		public static bool inventoryWasOpen;
		public static ModHotKey[] hotkeys;

		public override void Load()
		{
			ILRecipe.FindRecipes += ApplyRecursiveSearch;
			OnMain.DrawInventory += EditFocusRecipe;
			OnMain.Update += ApplyKey;
			On.Terraria.Recipe.Create += CraftCompoundRecipe;
			recipeByResult = new Dictionary<int, List<Recipe>>();
			recipeCache = new Dictionary<Recipe, Dictionary<int, int>>();

			hotkeys = new[]
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
			recipeByResult = null;
			recipeCache = null;
			hotkeys = null;

			if (currentCompound != null)
				Main.recipe[currentCompound.recipeId] = currentCompound.overridenRecipe;
			currentCompound = null;
		}

		public override void PostAddRecipes()
		{
			foreach (Recipe recipe in Main.recipe)
			{
				int type = recipe.createItem.type;
				if (!recipeByResult.TryGetValue(type, out List<Recipe> list))
				{
					list = new List<Recipe>();
					recipeByResult.Add(type, list);
				}

				list.Add(recipe);
			}
		}

		public static void CraftCompoundRecipe(On.Terraria.Recipe.orig_Create orig, Recipe self)
		{
			orig(self);
			if (currentCompound != null && self == currentCompound.currentRecipe)
			{
				currentCompound.OnCraft();
				Recipe.FindRecipes();
			}
		}

		public void ApplyKey(OnMain.orig_Update orig, Main self, GameTime gametime)
		{
			if (inventoryWasOpen != Main.playerInventory)
			{
				inventoryWasOpen = !inventoryWasOpen;
				depthSearch = ((RecursiveSettings) GetConfig("RecursiveSettings")).DefaultDepth;
			}

			if (inventoryWasOpen)
			{
				int oldDepth = depthSearch;
				if (hotkeys[0].JustPressed)
				{
					depthSearch = -1;
				}
				else if (hotkeys[1].JustPressed)
				{
					if (depthSearch == -1)
						depthSearch = 5;
					else
						depthSearch++;
				}
				else if (hotkeys[2].JustPressed)
				{
					if (depthSearch == 0)
						depthSearch = 0;
					else if (depthSearch == 5)
						depthSearch = -1;
					else
						depthSearch++;
				}
				else if (hotkeys[3].JustPressed)
				{
					depthSearch = 0;
				}

				if (oldDepth != depthSearch)
					Recipe.FindRecipes();
			}

			orig(self, gametime);
		}

		public static void EditFocusRecipe(OnMain.orig_DrawInventory orig, Main self)
		{
			if (currentCompound != null) Main.recipe[currentCompound.recipeId] = currentCompound.overridenRecipe;
			int i = Main.availableRecipe[Main.focusRecipe];
			Recipe recipe = Main.recipe[i];
			if (recipeCache.TryGetValue(recipe, out Dictionary<int, int> dictionary))
			{
				currentCompound = new CompoundRecipe(i, dictionary);
				Main.recipe[i] = currentCompound.currentRecipe;
			}
			else
			{
				currentCompound = null;
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

		public static void RecursiveSearch(Dictionary<int, int> dictionary)
		{
			recipeCache.Clear();
			for (int n = 0; n < Recipe.maxRecipes && Main.recipe[n].createItem.type != ItemID.None; n++)
				SearchRecipe(dictionary, n);
		}

		public static void SearchRecipe(Dictionary<int, int> dictionary, int n)
		{
			Recipe recipe = currentCompound?.recipeId == n ? currentCompound.overridenRecipe : Main.recipe[n];
			Dictionary<int, int> inventoryToUse = new Dictionary<int, int>(dictionary);
			Dictionary<int, int> inventoryOnceUsed = inventoryToUse;
			List<int> craftedItems = new List<int>();
			if (AmountOfDoableRecipe(ref inventoryOnceUsed, recipe.createItem.stack, recipe, craftedItems, 0) ==
			    0) return;
			if (inventoryOnceUsed != inventoryToUse)
			{
				Dictionary<int, int> usedItems = new Dictionary<int, int>();
				foreach (KeyValuePair<int, int> keyValuePair in inventoryOnceUsed)
				{
					if (!inventoryToUse.TryGetValue(keyValuePair.Key, out int amount))
						amount = 0;
					amount -= keyValuePair.Value;
					if (amount != 0)
						usedItems.Add(keyValuePair.Key, amount);
				}

				recipeCache.Add(recipe, usedItems);
			}

			Main.availableRecipe[Main.numAvailableRecipes++] = n;
		}

		public static int AmountOfDoableRecipe(ref Dictionary<int, int> inventoryToUse, int amount, Recipe recipe,
			List<int> craftedItems, int depth)
		{
			#region IsAvailable

			if (!RecipeHooks.RecipeAvailable(recipe))
				return 0;
			for (int craftingStation = 0;
				craftingStation < Recipe.maxRequirements && recipe.requiredTile[craftingStation] != -1;
				craftingStation++)
				if (!Main.player[Main.myPlayer].adjTile[recipe.requiredTile[craftingStation]])
					return 0;

			if (recipe.needWater && !Main.player[Main.myPlayer].adjWater &&
			    !Main.player[Main.myPlayer].adjTile[172])
				return 0;
			if (recipe.needHoney && !Main.player[Main.myPlayer].adjHoney)
				return 0;
			if (recipe.needLava && !Main.player[Main.myPlayer].adjLava)
				return 0;
			if (recipe.needSnowBiome && !Main.player[Main.myPlayer].ZoneSnow)
				return 0;

			#endregion

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

				foreach (int validItem in recipeAcceptedGroups
					.Select(recipeAcceptedGroup => RecipeGroup.recipeGroups[recipeAcceptedGroup])
					.Where(recipeGroup => recipeGroup.ContainsItem(ingredient.netID)).SelectMany(recipeGroup =>
						recipeGroup.ValidItems.Where(validItem =>
							!craftedItemsOnceUsed.Contains(ingredient.type) && !ingredientList.Contains(validItem))))
					ingredientList.Add(validItem);

				if (ingredientList.Count == 0)
					ingredientList.Add(ingredient.type);

				if (depth != 0)
					ingredientList.RemoveAll(i => craftedItemsOnceUsed.Contains(i));

				foreach (int validItem in ingredientList)
					if (inventoryOnceUsed.TryGetValue(validItem, out int availableAmount))
					{
						int usedAmount = Math.Min(ingredientsNeeded, availableAmount);
						inventoryOnceUsed[validItem] -= usedAmount;
						ingredientsNeeded -= usedAmount;

						if (ingredientsNeeded <= 0)
							break;
					}

				#endregion

				if (ingredientsNeeded > 0)
				{
					#region Recursive part

					if (depthSearch - depth != 0)
						foreach (int validItem in ingredientList)
						{
							if (!craftedItemsOnceUsed.Contains(validItem) &&
							    recipeByResult.TryGetValue(validItem, out List<Recipe> usableRecipes))
								foreach (Recipe ingredientRecipe in usableRecipes)
								{
									ingredientsNeeded -= AmountOfDoableRecipe(ref inventoryOnceUsed,
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
				return AmountOfDoableRecipe(ref inventoryToUse, timeCraft * recipe.createItem.stack, recipe,
					craftedItems, depth);
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
	}

	public class RecursiveSettings : ModConfig
	{
		[Label("Max recursive depth")]
		[Tooltip("Max number of different recipes that can be used to obtain an ingredient")]
		[DefaultValue(-1)]
		[Range(-1, 5)]
		public int DefaultDepth;

		public override ConfigScope Mode => ConfigScope.ClientSide;
	}

	public class CompoundRecipe
	{
		public Recipe currentRecipe;
		public Dictionary<int, int> dropItems;
		public Recipe overridenRecipe;
		public int recipeId;

		public CompoundRecipe(int recipeId, Dictionary<int, int> dictionary)
		{
			this.recipeId = recipeId;
			overridenRecipe = Main.recipe[recipeId];
			currentRecipe = new Recipe {createItem = overridenRecipe.createItem, alchemy = overridenRecipe.alchemy};
			dropItems = new Dictionary<int, int>();
			List<KeyValuePair<int, int>> keyValuePairs = dictionary.ToList();
			keyValuePairs.Reverse();
			int i = 0;
			foreach (KeyValuePair<int, int> keyValuePair in keyValuePairs)
				if (keyValuePair.Value < 0)
				{
					dropItems.Add(keyValuePair.Key, -keyValuePair.Value);
				}
				else
				{
					currentRecipe.requiredItem[i] = new Item();
					currentRecipe.requiredItem[i].SetDefaults(keyValuePair.Key);
					currentRecipe.requiredItem[i].stack = keyValuePair.Value;
					++i;
				}
		}

		public void OnCraft()
		{
			foreach (KeyValuePair<int, int> keyValuePair in dropItems)
				Main.player[Main.myPlayer].QuickSpawnItem(keyValuePair.Key, keyValuePair.Value);
		}
	}
}