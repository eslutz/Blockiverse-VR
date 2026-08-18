using System;

namespace Blockiverse.WorldGen
{
    public readonly struct ProfilerMarker
    {
        private readonly string name;

        public static Func<string, IDisposable> BeginMarkerCallback;

        public ProfilerMarker(string name)
        {
            this.name = name;
        }

        public AutoScope Auto()
        {
            var disposable = BeginMarkerCallback?.Invoke(name);
            return new AutoScope(disposable);
        }

        public readonly struct AutoScope : IDisposable
        {
            private readonly IDisposable disposable;

            public AutoScope(IDisposable disposable)
            {
                this.disposable = disposable;
            }

            public void Dispose()
            {
                disposable?.Dispose();
            }
        }
    }
}
