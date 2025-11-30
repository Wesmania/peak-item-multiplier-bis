using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using pworld.Scripts.Extensions;
using UnityEngine;

namespace ItemMultiplierBis;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    internal static ConfigEntry<float> ItemMultiplier { get; private set; } = null!;
    internal static ConfigEntry<String> Luggages { get; private set; } = null!;
    internal static ConfigEntry<bool> GroundSpawns { get; private set; } = null!;
    internal static ConfigEntry<bool> BerryBushes { get; private set; } = null!;
    internal static ConfigEntry<bool> BerryVines { get; private set; } = null!;

    static String defaultLuggages = "Luggage,Big Luggage,Explorer's Luggage";
    static HashSet<String> luggageSet = [];
    private void Awake()
    {
        Log = Logger;
        Log.LogInfo($"Plugin {Name} is loaded!");
        ItemMultiplier = Config.Bind("Config", "ItemMultiplier", 1.0f, "Item multiplier. Defaults to 1.0");
        Luggages = Config.Bind("Config", "Luggages", defaultLuggages, "Luggages to apply multiplier to, comma separated. Hint: \"Ancient Luggage\" and \"Ancient Statue\" are luggages too!");
        GroundSpawns = Config.Bind("Config", "GroundSpawns", false, "Whether the multipler applies to items spawned on the ground.");
        BerryBushes = Config.Bind("Config", "BerryBushes", false, "Whether the multipler applies to berry bushes.");
        BerryVines = Config.Bind("Config", "BerryVines", false, "Whether the multipler applies to berry vines.");

        luggageSet = [.. Luggages.Value.Split(',')];

        Harmony harmony = new("com.github.Wesmania.ItemMultiplierBis");
        try
        {
            harmony.PatchAll();
        }
        catch (System.Exception ex)
        {
            Log.LogError($"Failed to patch classes: {ex}");
        }
    }
    public static List<Transform> AddMoreSlotsUntil(List<Transform> spawnSpots, int target, bool addRandom = false)
    {
        List<Transform> newSpots = [];
        while (target >= spawnSpots.Count)
        {
            newSpots.AddRange(spawnSpots);
            target -= spawnSpots.Count;
        }
        if (target > 0)
        {
            spawnSpots.Shuffle();
            newSpots.AddRange(spawnSpots.GetRange(0, target));
        }
        if (addRandom)
        {
            newSpots.Add(spawnSpots[target]);
        }
            
        return newSpots;
    }
    public static List<Transform> AddMoreSlots(List<Transform> spawnSpots, float m) {
        float toAdd = spawnSpots.Count * m;
        int guaranteed = Mathf.FloorToInt(toAdd);
        float lastChance = toAdd - guaranteed;

        bool addLast = UnityEngine.Random.value < lastChance;
        return AddMoreSlotsUntil(spawnSpots, guaranteed, addLast);
    }
    public static void multiplySpawnRange(ref Vector2 range, ref List<Transform> spawnSpots, out Vector2 original)
    {
        original = range;
        range *= ItemMultiplier.Value;

        float maxSpawns = Math.Max(range.x, range.y);
        if (maxSpawns > spawnSpots.Count)
        {
            List<Transform> newSpots = Plugin.AddMoreSlotsUntil(spawnSpots, Mathf.CeilToInt(maxSpawns));
            spawnSpots.Clear();
            spawnSpots.AddRange(newSpots);
        }
    }

    [HarmonyPatch(typeof(BerryBush))]
    public class BerryBushPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("SpawnItems", typeof(List<Transform>))]
        public static void SpawnItemsPrefix(BerryBush __instance, ref List<Transform> spawnSpots, out Vector2 __state)
        {
            __state = __instance.possibleBerries;
            if (!BerryBushes.Value)
            {
                return;
            }
            Plugin.multiplySpawnRange(ref __instance.possibleBerries, ref spawnSpots, out __state);
            var n = __instance.possibleBerries;
            Log.LogInfo($"Berry bush would spawn {__state.x}-{__state.y} berries, multiplied to {n.x}-{n.y}.");
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnItems", typeof(List<Transform>))]
        public static void SpawnItemsPostfix(BerryBush __instance, List<Transform> spawnSpots, Vector2 __state) {
            if (!BerryBushes.Value)
            {
                return;
            }
            __instance.possibleBerries = __state;
        }
    }

    [HarmonyPatch(typeof(BerryVine))]
    public class BerryVinePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("SpawnItems", typeof(List<Transform>))]
        public static void SpawnItemsPrefix(BerryVine __instance, ref List<Transform> spawnSpots, out Vector2 __state)
        {
            __state = __instance.possibleBerries;
            if (!BerryVines.Value)
            {
                return;
            }
            Plugin.multiplySpawnRange(ref __instance.possibleBerries, ref spawnSpots, out __state);
            var n = __instance.possibleBerries;
            Log.LogInfo($"Berry vine would spawn {__state.x}-{__state.y} berries, multiplied to {n.x}-{n.y}.");
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnItems", typeof(List<Transform>))]
        public static void SpawnItemsPostfix(BerryVine __instance, List<Transform> spawnSpots, Vector2 __state) {
            if (!BerryVines.Value)
            {
                return;
            }
            __instance.possibleBerries = __state;
        }
    }

    [HarmonyPatch(typeof(GroundPlaceSpawner))]
    public class GroundPlaceSpawnerPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("SpawnItems", typeof(List<Transform>))]
        public static void SpawnItemsPrefix(GroundPlaceSpawner __instance, ref List<Transform> spawnSpots, out Vector2 __state)
        {
            __state = __instance.possibleItems;

            if (!GroundSpawns.Value)
            {
                return;
            }
            // This is a bit different, as it randomizes item count between x floor and y floor, inclusive.
            // This gives us x-y items, and we want x*f-y*f items, weighted between integer values.
            __instance.possibleItems *= ItemMultiplier.Value;

            Vector2 v = __instance.possibleItems;
            float chanceHigherX = v.x - Mathf.Floor(v.x);
            float chanceHigherY = v.y - Mathf.Floor(v.y);
            v.x = UnityEngine.Random.value < chanceHigherX ? Mathf.Ceil(v.x) : Mathf.Floor(v.x);
            v.y = UnityEngine.Random.value < chanceHigherY ? Mathf.Ceil(v.y) : Mathf.Floor(v.y); 
            __instance.possibleItems = v;

            float maxSpawns = Math.Max(v.x, v.y);
            if (maxSpawns > spawnSpots.Count)
            {
                List<Transform> newSpots = Plugin.AddMoreSlotsUntil(spawnSpots, Mathf.CeilToInt(maxSpawns));
                spawnSpots.Clear();
                spawnSpots.AddRange(newSpots);
            }

            var n = __instance.possibleItems;
            Log.LogInfo($"Ground spawner would spawn {__state.x}-{__state.y} berries, multiplied to {n.x}-{n.y}.");
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnItems", typeof(List<Transform>))]
        public static void SpawnItemsPostfix(GroundPlaceSpawner __instance, List<Transform> spawnSpots, Vector2 __state) {
            if (!GroundSpawns.Value)
            {
                return;
            }
            __instance.possibleItems = __state;
        }
    }

    [HarmonyPatch(typeof(Spawner))]
    public class LuggagePatch
    {
        public static bool IsLuggage(Spawner spawner)
        {
            Luggage? l = spawner as Luggage;
            if (l == null)
            {
                return false;
            }
            return luggageSet.Contains(l.displayName);
        }

        [HarmonyPrefix]
        [HarmonyPatch("SpawnItems", typeof(List<Transform>))]
        public static void SetStatusPrefix(Spawner __instance, ref List<Transform> spawnSpots)
        {
            // Other spawners have separate logic since not all their spawn slots are used.
            if (!IsLuggage(__instance))
            {
                return;
            }

            List<Transform> newSpots = Plugin.AddMoreSlots(spawnSpots, ItemMultiplier.Value);
            spawnSpots.Clear();
            spawnSpots.AddRange(newSpots);
            Log.LogInfo($"We should spawn {spawnSpots.Count} items.");
        }
    }
}
