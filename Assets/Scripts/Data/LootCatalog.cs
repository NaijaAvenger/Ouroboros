using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Data
{
    public enum LootTier
    {
        Common,
        Rare,
        Legendary
    }

    /// <summary>(v0.12) Which stand-in model <c>Art.ArtefactBuilder</c> makes for an item.</summary>
    public enum LootShape
    {
        Purse, Weights, Cloth, Headrest, Beads, Fragment, Blade, Reels, Ledger,
        Plaque, Pendant, Collar, Manuscript, Harp, DataCore, Leopard, Chest, Sword, Astrolabe
    }

    [Serializable]
    public class LootItemDef
    {
        public string name;
        public LootShape shape = LootShape.Purse;
        [TextArea(2, 4)] public string story;
        public int value = 200;
        public LootTier tier = LootTier.Common;
    }

    /// <summary>
    /// The things worth stealing back (v0.10, milestone M2). Loot is no longer a number: every vault holds
    /// named items from this catalog, each with a value and a short story card, and they exist in the world as
    /// pickups (<c>GameMode.LootDrop</c>) that are carried, dropped on death and taken by whoever is left standing.
    ///
    /// Items are referenced across the network by id (index + 1; 0 = loose, unnamed loot), so every peer must
    /// use the same list. With no asset assigned, <see cref="CreateDefault"/> supplies the same built-in list
    /// everywhere. To author your own: Create > Ouroboros > Loot Catalog, then assign it on GameSessionManager.
    ///
    /// The built-in entries are fictional objects inspired by broad categories of African material culture and
    /// by the "heritage and data of a continent" premise. They are placeholder writing: names and stories should
    /// be reviewed with people from the cultures they draw on before anything ships.
    /// </summary>
    [CreateAssetMenu(fileName = "Loot Catalog", menuName = "Ouroboros/Loot Catalog")]
    public class LootCatalog : ScriptableObject
    {
        public LootItemDef[] items = new LootItemDef[0];

        private static LootCatalog active;

        /// <summary>The catalog in use on this peer. Falls back to the built-in list when none was assigned.</summary>
        public static LootCatalog Active
        {
            get { return active != null ? active : (active = CreateDefault()); }
            set { active = value; }
        }

        public int Count => items != null ? items.Length : 0;

        /// <summary>Definition for a networked item id (index + 1). Null for 0 or an unknown id.</summary>
        public LootItemDef Get(int id)
        {
            int index = id - 1;
            return items != null && index >= 0 && index < items.Length ? items[index] : null;
        }

        public int ValueOf(int id)
        {
            var def = Get(id);
            return def != null ? Mathf.Max(0, def.value) : 0;
        }

        /// <summary>Fills <paramref name="buffer"/> with the ids of every item of <paramref name="tier"/>.</summary>
        public void GetIdsOfTier(LootTier tier, List<int> buffer)
        {
            buffer.Clear();
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null && items[i].tier == tier) buffer.Add(i + 1);
            }
        }

        public static Color TierColor(LootTier tier)
        {
            switch (tier)
            {
                case LootTier.Rare:      return new Color(0.35f, 0.75f, 1f);
                case LootTier.Legendary: return new Color(1f, 0.55f, 0.1f);
                default:                 return new Color(1f, 0.82f, 0.2f);
            }
        }

        /// <summary>Built-in list, identical on every peer. Append only: ids are positions in this list.</summary>
        public static LootCatalog CreateDefault()
        {
            var catalog = CreateInstance<LootCatalog>();
            catalog.name = "Built-in Loot Catalog";
            catalog.hideFlags = HideFlags.DontSave;
            var list = new List<LootItemDef>();

            // [v0.11] void Add(LootTier tier, int value, string name, string story)
            LootItemDef Add(LootTier tier, int value, string name, string story)
            {
                var def = new LootItemDef { name = name, story = story, value = value, tier = tier };
                list.Add(def);
                return def;
            }

            // ---- Common: small, numerous, easy to carry out ----
            Add(LootTier.Common, 150, "Brass Merchant Weights",
                "A set of cast-brass weights once used to measure gold dust. Catalogued here as 'assorted metalwork, lot 114'.").shape = LootShape.Weights;
            Add(LootTier.Common, 180, "Indigo Strip-Cloth",
                "Narrow hand-woven bands sewn edge to edge and dyed deep blue. The freeport lists the weaver as 'unknown'.").shape = LootShape.Cloth;
            Add(LootTier.Common, 160, "Carved Headrest",
                "A herder's wooden headrest, worn smooth by one owner over a lifetime. Sold on as 'tribal furniture'.").shape = LootShape.Headrest;
            Add(LootTier.Common, 200, "Trade Bead Strand",
                "Glass beads that crossed a continent as currency. Each colour once meant something to somebody.").shape = LootShape.Beads;
            Add(LootTier.Common, 220, "Terracotta Fragment",
                "Part of a fired-clay figure two thousand years old, dug at night and shipped in a crate of ceramics.").shape = LootShape.Fragment;
            Add(LootTier.Common, 170, "Iron Currency Blade",
                "Forged to be traded, not swung. A blacksmith's signature is still visible near the tang.").shape = LootShape.Blade;
            Add(LootTier.Common, 240, "Field Recorder Reels",
                "Songs taped by a visiting researcher in the 1950s. The families of the singers were never given copies.").shape = LootShape.Reels;
            Add(LootTier.Common, 260, "Shipping Ledger",
                "Pages of 'curios, 40 cases' with ports and dates. On its own it is paper; in court it is provenance.").shape = LootShape.Ledger;

            // ---- Rare: one or two per vault, worth a fight ----
            Add(LootTier.Rare, 450, "Bronze Court Plaque",
                "Cast to hang on a palace pillar and record who stood where. It has not been home in over a century.").shape = LootShape.Plaque;
            Add(LootTier.Rare, 500, "Gold Filigree Pendant",
                "Wire-fine goldwork made for a wedding. Auctioned twice since, each time with a shorter history.").shape = LootShape.Pendant;
            Add(LootTier.Rare, 420, "Beadwork Collar",
                "A broad collar whose colours spell out age, standing and family. The pattern can still be read.").shape = LootShape.Collar;
            Add(LootTier.Rare, 550, "Astronomer's Manuscript",
                "Star tables and marginal notes in a scholar's hand, from a desert library that hid its books to save them.").shape = LootShape.Manuscript;
            Add(LootTier.Rare, 480, "Twenty-One String Harp",
                "A griot's instrument, its bridge replaced many times and its songs never written down.").shape = LootShape.Harp;
            Add(LootTier.Rare, 600, "Land Registry Core",
                "A cold-storage drive: every title deed of a coastal city, held offshore as collateral.").shape = LootShape.DataCore;

            // ---- Legendary: the reason a crew came ----
            Add(LootTier.Legendary, 1100, "Bronze Leopard",
                "One of a pair that flanked a throne. Its twin is in a museum that says the question of return is complicated.").shape = LootShape.Leopard;
            Add(LootTier.Legendary, 1200, "Library Chest",
                "Forty manuscripts on law, medicine and the stars, carried out of a burning city in one iron-bound box.").shape = LootShape.Chest;
            Add(LootTier.Legendary, 1300, "Seed Archive Core",
                "Genomes of ten thousand food crops, bred over centuries by farmers and patented by someone else.").shape = LootShape.DataCore;
            Add(LootTier.Legendary, 1000, "Court Sword",
                "A blade of state with a gold-covered grip, carried before a ruler and never drawn in anger.").shape = LootShape.Sword;

            // ---- (v0.12) Appended so existing ids keep their meaning: twenty items in all ----
            Add(LootTier.Common, 190, "Cowrie Purse",
                "A leather purse of cowrie shells, once money from the coast to the desert's edge. Listed here as 'ornamental'.").shape = LootShape.Purse;
            Add(LootTier.Rare, 520, "Navigator's Astrolabe",
                "A brass instrument for reading latitude from the stars, made for the monsoon routes of the eastern coast.").shape = LootShape.Astrolabe;

            catalog.items = list.ToArray();
            return catalog;
        }
    }
}
