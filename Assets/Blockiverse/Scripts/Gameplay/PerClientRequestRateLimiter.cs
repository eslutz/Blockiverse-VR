using System;
using System.Collections.Generic;

namespace Blockiverse.Gameplay
{
    public sealed class PerClientRequestRateLimiter
    {
        readonly Dictionary<ulong, Queue<double>> requestTimesByClientId = new();
        readonly int maxRequests;
        readonly double windowSeconds;

        public PerClientRequestRateLimiter(int maxRequests, double windowSeconds)
        {
            if (maxRequests <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRequests));
            if (windowSeconds <= 0.0d)
                throw new ArgumentOutOfRangeException(nameof(windowSeconds));

            this.maxRequests = maxRequests;
            this.windowSeconds = windowSeconds;
        }

        public bool TryConsume(ulong clientId, double nowSeconds)
        {
            if (!requestTimesByClientId.TryGetValue(clientId, out Queue<double> requestTimes))
            {
                requestTimes = new Queue<double>();
                requestTimesByClientId.Add(clientId, requestTimes);
            }

            double cutoff = nowSeconds - windowSeconds;
            while (requestTimes.Count > 0 && requestTimes.Peek() <= cutoff)
                requestTimes.Dequeue();

            if (requestTimes.Count >= maxRequests)
                return false;

            requestTimes.Enqueue(nowSeconds);
            return true;
        }

        public void RemoveClient(ulong clientId)
        {
            requestTimesByClientId.Remove(clientId);
        }

        public void Clear()
        {
            requestTimesByClientId.Clear();
        }
    }
}
