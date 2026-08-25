using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;

namespace Blockiverse.Tests.EditMode.Toolkit
{
    // No two co-visible HUD panels may overlap in space.
    //
    // ── Why this needs a test ────────────────────────────────────────────────
    //
    // The HUD family shares one screen id and one rig anchor, so every panel is on screen at the
    // same time, positioned only by HudLocalX/Y and sized only by WidthPixels/HeightPixels. Nothing
    // lays them out relative to each other. Two panels whose rectangles intersect render on top of
    // one another in the headset and produce no editor warning, no exception and no failing
    // assertion — HudFamilyEditModeTests already carries a comment saying exactly this, but it
    // checks each panel's numbers in isolation, which cannot see a collision.
    //
    // It caught two on the run that introduced it: the hotbar strip was 10 mm taller than the gap
    // between the action bar and the mining bar, and the debug readout's top edge clipped the
    // status toast by 20 mm.
    //
    // ── The quick block menu is excluded ─────────────────────────────────────
    //
    // CreativeHotbarController carries IUiToolkitQuickBlockMenu, which the host uses to exclude it
    // from routed visibility: it is opened on demand and is not co-visible with the rest. It
    // deliberately overlaps the action bar, because it replaces it while open.
    public sealed class HudPanelOverlapEditModeTests
    {
        // The project convention: 1000 document pixels is 1.00 m at 100 pixels-per-unit and 0.1
        // panel scale. Everything below is metres.
        const float PixelsPerMetre = 1000f;

        // Panels closer than this are treated as touching. Not zero: these are hand-authored
        // numbers and two panels sharing an exact edge is a coincidence waiting to become an
        // overlap, not a design.
        const float MinimumClearanceMetres = 0.002f;

        readonly struct Panel
        {
            public Panel(string name, UiToolkitScreenAttribute a)
            {
                Name = name;
                MinX = a.HudLocalX - a.WidthPixels / (2f * PixelsPerMetre);
                MaxX = a.HudLocalX + a.WidthPixels / (2f * PixelsPerMetre);
                MinY = a.HudLocalY - a.HeightPixels / (2f * PixelsPerMetre);
                MaxY = a.HudLocalY + a.HeightPixels / (2f * PixelsPerMetre);
            }

            public string Name { get; }
            public float MinX { get; }
            public float MaxX { get; }
            public float MinY { get; }
            public float MaxY { get; }
        }

        static IEnumerable<Panel> CoVisibleHudPanels()
        {
            foreach (Type type in TypeCache.GetTypesWithAttribute<UiToolkitScreenAttribute>())
            {
                var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                    type, typeof(UiToolkitScreenAttribute));

                if (attribute == null || attribute.PlacementProfile != UiToolkitPlacementProfile.Hud)
                    continue;

                // Opened on demand, not co-visible; it replaces the action bar while showing.
                if (typeof(IUiToolkitQuickBlockMenu).IsAssignableFrom(type))
                    continue;

                yield return new Panel(type.Name, attribute);
            }
        }

        [Test]
        public void NoTwoCoVisibleHudPanelsOverlap()
        {
            Panel[] panels = CoVisibleHudPanels().ToArray();

            Assert.That(panels.Length, Is.GreaterThan(1),
                "Fewer than two co-visible HUD panels found — positive control failed.");

            var collisions = new List<string>();

            for (int i = 0; i < panels.Length; i++)
            {
                for (int j = i + 1; j < panels.Length; j++)
                {
                    Panel a = panels[i];
                    Panel b = panels[j];

                    // Positive = overlapping by that much; negative = a gap of that much.
                    float overlapX = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
                    float overlapY = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);

                    // Compared against NEGATIVE clearance, so the constant is a required GAP rather
                    // than a tolerated overlap. The earlier `> MinimumClearanceMetres` form let a
                    // 1 mm overlap through and passed two panels sharing an exact edge — the very
                    // case the constant's comment says must fail.
                    //
                    // Rectangles are clear of each other if they are separated on EITHER axis, so
                    // this fires only when both axes are within the clearance.
                    if (overlapX > -MinimumClearanceMetres && overlapY > -MinimumClearanceMetres)
                    {
                        collisions.Add(
                            $"{a.Name} vs {b.Name}: " +
                            $"{overlapX * 1000f:0} x {overlapY * 1000f:0} mm " +
                            "(positive = overlap, negative = gap; both axes must clear " +
                            $"{MinimumClearanceMetres * 1000f:0} mm)");
                    }
                }
            }

            Assert.That(collisions, Is.Empty,
                "These panels render on top of each other in the headset, with nothing failing in " +
                "the editor:\n" + string.Join("\n", collisions));
        }

        // A panel wider or taller than the player's comfortable view is its own problem, and it is
        // the shape most likely to start colliding with everything once someone nudges it.
        [Test]
        public void NoHudPanelIsAbsurdlyLarge()
        {
            var oversized = new List<string>();

            foreach (Panel panel in CoVisibleHudPanels())
            {
                float width = panel.MaxX - panel.MinX;
                float height = panel.MaxY - panel.MinY;

                // ~53° wide and ~33° tall at the 1.10-1.15 m HUD distance. The hotbar strip is the
                // widest thing here at 1.00 m and is deliberately so.
                if (width > 1.2f || height > 0.7f)
                    oversized.Add($"{panel.Name} is {width:0.00} x {height:0.00} m");
            }

            Assert.That(oversized, Is.Empty, string.Join("\n", oversized));
        }
    }
}
