using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.Networking.EditMode
{
    // Ruleset §16: every server-side reach gate uses ONE formula (distance to the block's box) and
    // ONE limit (MaxHostValidatedReachMeters). These tests exist because sharing a helper is not the
    // same as being required to use it -- the survival gate carried its own Vector3.Distance to the
    // block CENTRE at a hardcoded 6.0 m and nothing failed when it drifted.
    //
    // Every "accepted" case below is REJECTED by that old formula, so this fixture fails if anyone
    // reintroduces a centre-distance or drops the host tolerance.
    public sealed class ServerReachGateEditModeTests
    {
        static readonly Vector3 Head = Vector3.zero;

        // centre 6.538 m (old gate rejects at 6.0), box 6.000 m (within the 7.5 m server limit)
        static readonly BlockPosition JustPastCentreLimit = new BlockPosition(6, 0, 0);

        // The corner case, and the one a single-axis test misses: centre 7.794 m, box 6.928 m.
        static readonly BlockPosition DiagonalCorner = new BlockPosition(4, 4, 4);

        // box 8.000 m -- genuinely beyond the server limit, so it must still be refused.
        static readonly BlockPosition BeyondServerLimit = new BlockPosition(8, 0, 0);

        [Test]
        public void SurvivalGateAcceptsABlockTheOldCentreFormulaWouldHaveRejected()
        {
            Assert.That(
                MultiplayerSurvivalSync.IsBlockWithinInteractionReach(Head, JustPastCentreLimit),
                Is.True,
                "A block 6.0 m from its box is inside the 7.5 m server limit. Measuring to the " +
                "centre instead put it at 6.538 m and rejected a legitimate survival edit.");
        }

        [Test]
        public void SurvivalGateAcceptsADiagonalCornerWithinTheServerLimit()
        {
            Assert.That(
                MultiplayerSurvivalSync.IsBlockWithinInteractionReach(Head, DiagonalCorner),
                Is.True,
                "Box distance 6.928 m is within 7.5 m; the centre formula measured 7.794 m.");
        }

        [Test]
        public void SurvivalGateStillRejectsBeyondTheServerLimit()
        {
            Assert.That(
                MultiplayerSurvivalSync.IsBlockWithinInteractionReach(Head, BeyondServerLimit),
                Is.False,
                "Widening the formula must not turn the gate off.");
        }

        // The actual contract: the survival gate and the client's local gate must not disagree
        // about a block the client already allowed. This is the invariant the drift violated.
        [Test]
        public void SurvivalGateAcceptsEverythingTheClientSideGateAllows()
        {
            for (int x = 0; x <= 9; x++)
            {
                for (int y = 0; y <= 9; y++)
                {
                    var block = new BlockPosition(x, y, 0);

                    if (!CreativeInteractionController.IsBlockWithinInteractionReach(Head, block))
                        continue;

                    Assert.That(
                        MultiplayerSurvivalSync.IsBlockWithinInteractionReach(Head, block),
                        Is.True,
                        $"Client allows an edit at {x},{y},0 that the server-side survival gate " +
                        "rejects. A locally legal edit must never be refused as out of reach.");
                }
            }
        }

        // Both server-side gates must agree with each other, not merely each with the client.
        [Test]
        public void SurvivalGateMatchesTheEnforcedServerLimitExactly()
        {
            for (int x = 0; x <= 12; x++)
            {
                var block = new BlockPosition(x, 0, 0);

                bool sharedFormula = BlockiverseInteractionLimits.IsWithinReach(
                    Head.x, Head.y, Head.z,
                    block.X, block.Y, block.Z,
                    BlockiverseInteractionLimits.MaxHostValidatedReachMeters);

                Assert.That(
                    MultiplayerSurvivalSync.IsBlockWithinInteractionReach(Head, block),
                    Is.EqualTo(sharedFormula),
                    $"Survival gate disagrees with BlockiverseInteractionLimits at {x},0,0.");
            }
        }
    }
}
