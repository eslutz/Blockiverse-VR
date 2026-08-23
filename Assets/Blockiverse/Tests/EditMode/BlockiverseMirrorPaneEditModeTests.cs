using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    /// <summary>
    /// Pins the mirror_pane block's registration surface (issue #340) and the mirror
    /// reflection math. The satellite tables fail in distant, hard-to-attribute ways when
    /// a row is missed (invisible block, unobtainable item, silent break audio), so each
    /// gets an explicit assertion here.
    /// </summary>
    public sealed class BlockiverseMirrorPaneEditModeTests
    {
        [Test]
        public void MirrorPaneIsFullyRegistered()
        {
            BlockDefinition definition = BlockRegistry.Default.Get(BlockRegistry.MirrorPane);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.CanonicalId, Is.EqualTo("mirror_pane"));
            Assert.That(definition.Category, Is.EqualTo(BlockCategory.Crafted));
            // Glass-like: neighbours must keep their faces so the pane does not punch
            // holes into adjacent chunk geometry.
            Assert.That(definition.IsSolid, Is.False);
            Assert.That(definition.IsRenderable, Is.True);

            ItemDefinition item = ItemRegistry.Default.Get(ItemId.MirrorPane);
            Assert.That(item, Is.Not.Null);
            Assert.That(item.HasBlockMapping, Is.True);
            Assert.That(item.BlockId, Is.EqualTo(BlockRegistry.MirrorPane));

            CraftingRecipe recipe = CraftingRecipeBook.Default.All
                .FirstOrDefault(candidate => candidate.Output.ItemId == ItemId.MirrorPane);
            Assert.That(recipe, Is.Not.Null, "mirror_pane needs a crafting recipe.");
            Assert.That(recipe.RequiredStation, Is.EqualTo(CraftingStation.ClayKiln));
            Assert.That(
                recipe.Ingredients.Select(stack => stack.ItemId),
                Is.EquivalentTo(new[] { ItemId.ClearpaneGlass, ItemId.PaletinBar }));

            Assert.That(
                BlockHarvestRuleSet.CreateDefault().TryGet(BlockRegistry.MirrorPane, out _), Is.True,
                "mirror_pane needs a harvest rule or it cannot be broken in survival.");
        }

        [Test]
        public void ReflectionMathMirrorsAcrossThePane()
        {
            // Pane on a wall facing +X; viewer stands 2 m in front, 1 m to the pane's left.
            Vector3 paneCenter = new(10.0f, 65.0f, 20.0f);
            Quaternion paneBasis = Quaternion.LookRotation(Vector3.right, Vector3.up);
            Vector3 viewer = paneCenter + new Vector3(2.0f, 0.0f, 1.0f);

            MirrorPoseMath.ReflectIntoPaneFrame(
                paneCenter, paneBasis, viewer, Vector3.left,
                out Vector3 localPosition, out Vector3 localForward);

            // Pane frame: forward = +X world. Viewer is 2 m in front (local z = 2) and
            // 1 m to local left (world +Z maps to local -x for this basis). Reflection
            // negates z only.
            Assert.That(localPosition.z, Is.EqualTo(-2.0f).Within(1e-4f));
            Assert.That(localPosition.y, Is.EqualTo(0.0f).Within(1e-4f));
            // Facing the pane (world -X) reflects to facing out of it.
            Assert.That(localForward.z, Is.EqualTo(1.0f).Within(1e-4f));

            MirrorPoseMath.ComposeStudioPose(
                new Vector3(100.0f, 200.0f, 300.0f), Quaternion.identity,
                localPosition, localForward,
                out Vector3 studioPosition, out Quaternion studioRotation);

            // The reflected avatar lands behind the studio origin (negative z), which is
            // where the studio camera looks, and faces back toward it.
            Assert.That(studioPosition.z, Is.EqualTo(298.0f).Within(1e-4f));
            Vector3 studioForward = studioRotation * Vector3.forward;
            Assert.That(studioForward.z, Is.EqualTo(1.0f).Within(1e-4f));
        }

        [Test]
        public void VisibleFaceSelectionPrefersTheViewerSideAndSkipsBlockedFaces()
        {
            Vector3 blockCenter = new(0.5f, 0.5f, 0.5f);

            bool found = MirrorPoseMath.TryChooseVisibleFace(
                blockCenter, blockCenter + new Vector3(3.0f, 0.0f, 0.2f),
                _ => true, out Vector3Int normal);
            Assert.That(found, Is.True);
            Assert.That(normal, Is.EqualTo(new Vector3Int(1, 0, 0)));

            // The viewer-side face is blocked: fall back to the next-best open face.
            found = MirrorPoseMath.TryChooseVisibleFace(
                blockCenter, blockCenter + new Vector3(3.0f, 0.0f, 0.2f),
                candidate => candidate.x == 0, out normal);
            Assert.That(found, Is.True);
            Assert.That(normal, Is.EqualTo(new Vector3Int(0, 0, 1)));

            // Viewer directly above: no horizontal face points at them meaningfully more
            // than another, but any positive-dot open face is acceptable; straight above
            // yields zero dot everywhere, so no face is chosen and the mirror stays dark.
            found = MirrorPoseMath.TryChooseVisibleFace(
                blockCenter, blockCenter + new Vector3(0.0f, 3.0f, 0.0f),
                _ => true, out _);
            Assert.That(found, Is.False);
        }
    }
}
