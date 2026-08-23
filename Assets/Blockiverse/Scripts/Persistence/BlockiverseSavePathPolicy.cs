using System;
using System.Collections.Generic;
using System.IO;

namespace Blockiverse.Persistence
{
    // Decides which directories world saves may be written to.
    //
    // The property being protected is that a bad configuration arriving at runtime cannot redirect
    // saves somewhere unexpected. Until now that was enforced by hardcoding the allowed roots, which
    // also made a dedicated server's `--world-dir /data` impossible.
    //
    // So the roots stay closed to runtime changes, but their PROVENANCE widens: an operator may
    // declare one additional root at process startup, before any session is listening. After that
    // the set is sealed for the lifetime of the process.
    public static class BlockiverseSavePathPolicy
    {
        static readonly List<string> AdditionalRoots = new();
        static bool sealedForSession;

        // Set by the host application. Kept as a delegate so this type stays free of UnityEngine
        // and can be unit-tested without a player loop.
        public static Func<IEnumerable<string>> DefaultRootProvider { get; set; }

        // Called once the process starts listening. After this, registration throws rather than
        // silently succeeding, so a late registration is a loud bug and not a quiet redirect.
        public static void SealForSession() => sealedForSession = true;

        public static void ResetForTesting()
        {
            AdditionalRoots.Clear();
            sealedForSession = false;
        }

        public static IReadOnlyList<string> RegisteredAdditionalRoots => AdditionalRoots;

        // Declares an operator-chosen save root. Rooted, existing directories only: a relative or
        // absent path is a configuration mistake, and creating it here would let a typo silently
        // become a new save location.
        public static bool TryRegisterAdditionalRoot(string root, out string failureReason)
        {
            failureReason = null;

            if (sealedForSession)
                throw new InvalidOperationException(
                    "Save roots are sealed once a session is listening; register them during startup.");

            if (string.IsNullOrWhiteSpace(root))
            {
                failureReason = "path is empty";
                return false;
            }

            string resolved;
            try
            {
                if (!Path.IsPathRooted(root))
                {
                    failureReason = "path is not absolute";
                    return false;
                }

                resolved = Path.GetFullPath(root);
            }
            catch (Exception exception)
            {
                failureReason = exception.Message;
                return false;
            }

            if (!Directory.Exists(resolved))
            {
                failureReason = "directory does not exist";
                return false;
            }

            if (!AdditionalRoots.Contains(resolved))
                AdditionalRoots.Add(resolved);

            return true;
        }

        // True when the candidate resolves inside a permitted root.
        public static bool IsTrusted(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            foreach (string root in EnumerateRoots())
            {
                if (IsUnderRoot(fullPath, root))
                    return true;
            }

            return false;
        }

        static IEnumerable<string> EnumerateRoots()
        {
            IEnumerable<string> defaults = DefaultRootProvider?.Invoke();
            if (defaults != null)
            {
                foreach (string root in defaults)
                    yield return root;
            }

            foreach (string root in AdditionalRoots)
                yield return root;
        }

        // A path is under a root only at a directory boundary: "/data-other" must not count as
        // being under "/data".
        public static bool IsUnderRoot(string fullPath, string root)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fullPath))
                return false;

            string normalizedRoot;
            try
            {
                normalizedRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return false;
            }

            return fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                   string.Equals(fullPath, normalizedRoot, StringComparison.Ordinal);
        }
    }
}
