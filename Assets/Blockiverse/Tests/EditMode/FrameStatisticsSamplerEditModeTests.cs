using System;
using Blockiverse.Core;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class FrameStatisticsSamplerEditModeTests
    {
        [Test]
        public void RejectsNonPositiveWindowSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStatisticsSampler(0));
        }

        [Test]
        public void ReportsNoSamplesUntilFramesAreAdded()
        {
            var sampler = new FrameStatisticsSampler(8);

            Assert.That(sampler.HasSamples, Is.False);
            Assert.That(sampler.AverageFps, Is.EqualTo(0f));
            Assert.That(sampler.MinFps, Is.EqualTo(0f));
            Assert.That(sampler.MaxFps, Is.EqualTo(0f));
        }

        [Test]
        public void IgnoresInvalidFrameDurations()
        {
            var sampler = new FrameStatisticsSampler(8);

            sampler.AddFrame(0f);
            sampler.AddFrame(-1f);
            sampler.AddFrame(float.NaN);
            sampler.AddFrame(float.PositiveInfinity);

            Assert.That(sampler.HasSamples, Is.False);
        }

        [Test]
        public void ComputesAverageMinAndMaxAcrossWindow()
        {
            var sampler = new FrameStatisticsSampler(8);

            sampler.AddFrame(1f / 90f);
            sampler.AddFrame(1f / 72f);
            sampler.AddFrame(1f / 60f);

            Assert.That(sampler.SampleCount, Is.EqualTo(3));
            Assert.That(sampler.MaxFps, Is.EqualTo(90f).Within(0.5f));
            Assert.That(sampler.MinFps, Is.EqualTo(60f).Within(0.5f));
            Assert.That(sampler.AverageFps, Is.EqualTo(72f).Within(2f));
            Assert.That(sampler.AverageFrameMilliseconds, Is.GreaterThan(0f));
        }

        [Test]
        public void EvictsOldestSampleOnceWindowIsFull()
        {
            var sampler = new FrameStatisticsSampler(2);

            sampler.AddFrame(1f / 30f);
            sampler.AddFrame(1f / 90f);
            sampler.AddFrame(1f / 90f);

            Assert.That(sampler.SampleCount, Is.EqualTo(2));
            Assert.That(sampler.MinFps, Is.EqualTo(90f).Within(0.5f));
            Assert.That(sampler.AverageFps, Is.EqualTo(90f).Within(0.5f));
        }

        [Test]
        public void ResetClearsAllSamples()
        {
            var sampler = new FrameStatisticsSampler(4);
            sampler.AddFrame(1f / 72f);

            sampler.Reset();

            Assert.That(sampler.HasSamples, Is.False);
            Assert.That(sampler.SampleCount, Is.EqualTo(0));
            Assert.That(sampler.AverageFps, Is.EqualTo(0f));
        }
    }
}
