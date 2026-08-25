using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Blockiverse.Survival;
using Blockiverse.Voxel;

namespace Blockiverse.Networking
{
    /// <summary>
    /// Compatibility hashes for the LAN join handshake (ruleset §5).
    ///
    /// These are deliberately **not** the save-manifest hashes in
    /// <c>WorldSaveService</c>, because the two answer different questions. A save stores
    /// canonical string ids, so string-set identity is all it needs. The multiplayer wire
    /// sends raw integer <see cref="BlockId"/> values in mutation deltas and snapshots, so a
    /// peer that agrees on every canonical id but assigns a different integer to one of them
    /// would pass the save-style check and then decode every delta wrongly — silently, with no
    /// error anywhere. These hashes therefore cover the id→integer mapping and the definition
    /// fields a peer simulates or renders from.
    /// </summary>
    public static class BlockiverseRegistryCompatibility
    {
        /// <summary>
        /// Blocks travel as integers, so the integer is part of the contract. Ordered by that
        /// integer rather than by name: two builds that assign the same ids in a different
        /// registration order describe the same world and must hash the same.
        ///
        /// Display <c>Name</c> is deliberately excluded — it drives no simulation or geometry,
        /// so including it would refuse a join over a typo fix.
        /// </summary>
        public static string ComputeBlockHash(BlockRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            IEnumerable<string> entries = registry.All
                .Select(definition => string.Join(
                    ":",
                    definition.Id.Value.ToString(CultureInfo.InvariantCulture),
                    definition.CanonicalId,
                    // Category is not a label: it selects fluid mesh generation in
                    // ChunkMeshBuilder and drives mining cost and harvest eligibility, so two
                    // peers disagreeing on it build different geometry from the same delta.
                    ((int)definition.Category).ToString(CultureInfo.InvariantCulture),
                    definition.IsSolid ? "1" : "0",
                    definition.OccludesFaces ? "1" : "0",
                    definition.BlocksLight ? "1" : "0",
                    // Skylight is recomputed on every peer rather than replicated, so a peer that
                    // disagreed about how much light a leaf passes would render a different world
                    // from the same blocks.
                    definition.LightTransmission.ToString("0.###", CultureInfo.InvariantCulture),
                    definition.IsRenderable ? "1" : "0",
                    // Render shape selects which geometry both peers build from the same delta —
                    // a cube versus a cross quad is the same class of divergence as Category
                    // above. Passability is a physics divergence: one peer walks through a plant
                    // the other collides with.
                    ((int)definition.RenderShape).ToString(CultureInfo.InvariantCulture),
                    definition.IsPassable ? "1" : "0",
                    definition.EmissiveLight.ToString(CultureInfo.InvariantCulture),
                    ((int)definition.HardnessClass).ToString(CultureInfo.InvariantCulture),
                    definition.HarvestTierMin.ToString(CultureInfo.InvariantCulture),
                    definition.Hardness.ToString("R", CultureInfo.InvariantCulture)))
                .OrderBy(entry => entry, StringComparer.Ordinal);

            return ComputeMd5Hex(string.Join("|", entries));
        }

        /// <summary>
        /// Items travel as canonical strings, so the id set matters less here than the
        /// definitions behind it: stack size, tool class/tier, durability and the block an item
        /// places all drive host resolution that clients mirror into their own UI.
        /// </summary>
        public static string ComputeItemHash(ItemRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            IEnumerable<string> entries = registry.All
                .Select(definition => string.Join(
                    ":",
                    definition.Id.Value,
                    ((int)definition.Kind).ToString(CultureInfo.InvariantCulture),
                    definition.MaxStackSize.ToString(CultureInfo.InvariantCulture),
                    definition.BlockId.HasValue
                        ? definition.BlockId.Value.Value.ToString(CultureInfo.InvariantCulture)
                        : "-",
                    ((int)definition.ToolClass).ToString(CultureInfo.InvariantCulture),
                    definition.ToolTier.ToString(CultureInfo.InvariantCulture),
                    definition.MaxDurability.ToString(CultureInfo.InvariantCulture)))
                .OrderBy(entry => entry, StringComparer.Ordinal);

            return ComputeMd5Hex(string.Join("|", entries));
        }

        /// <summary>
        /// Everything a peer must agree on to resolve a craft identically: output, station,
        /// craft time and the ingredient set. Ingredients keep their declared order because it
        /// is part of the recipe's identity; the recipes themselves are ordered so the hash does
        /// not depend on registration order.
        /// </summary>
        public static string ComputeRecipeHash(CraftingRecipeBook recipeBook)
        {
            if (recipeBook == null)
                throw new ArgumentNullException(nameof(recipeBook));

            IEnumerable<string> entries = recipeBook.All
                .Select(recipe => string.Concat(
                    recipe.Output.ItemId.Value, ":", recipe.Output.Count.ToString(CultureInfo.InvariantCulture),
                    ">", ((int)recipe.RequiredStation).ToString(CultureInfo.InvariantCulture),
                    "@", recipe.TimeTicks.ToString(CultureInfo.InvariantCulture),
                    "<", string.Join(
                        ",",
                        recipe.Ingredients.Select(ingredient => string.Concat(
                            ingredient.ItemId.Value, ":", ingredient.Count.ToString(CultureInfo.InvariantCulture))))))
                .OrderBy(entry => entry, StringComparer.Ordinal);

            return ComputeMd5Hex(string.Join("|", entries));
        }

        static string ComputeMd5Hex(string content)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
