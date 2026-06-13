using Blockiverse.Gameplay;
using NUnit.Framework;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class PerClientRequestRateLimiterEditModeTests
    {
        [Test]
        public void LimitIsAppliedPerClientWithinSlidingWindow()
        {
            var limiter = new PerClientRequestRateLimiter(maxRequests: 2, windowSeconds: 1.0d);

            Assert.That(limiter.TryConsume(1, 10.0d), Is.True);
            Assert.That(limiter.TryConsume(1, 10.1d), Is.True);
            Assert.That(limiter.TryConsume(1, 10.2d), Is.False);
            Assert.That(limiter.TryConsume(2, 10.2d), Is.True, "Another client should have an independent bucket.");
            Assert.That(limiter.TryConsume(1, 11.1d), Is.True, "Old entries should expire from the sliding window.");
        }

        [Test]
        public void RemoveAndClearDropTrackedClientWindows()
        {
            var limiter = new PerClientRequestRateLimiter(maxRequests: 1, windowSeconds: 1.0d);

            Assert.That(limiter.TryConsume(1, 20.0d), Is.True);
            Assert.That(limiter.TryConsume(1, 20.1d), Is.False);

            limiter.RemoveClient(1);
            Assert.That(limiter.TryConsume(1, 20.2d), Is.True);

            Assert.That(limiter.TryConsume(2, 20.2d), Is.True);
            Assert.That(limiter.TryConsume(2, 20.3d), Is.False);

            limiter.Clear();
            Assert.That(limiter.TryConsume(2, 20.4d), Is.True);
        }
    }
}
