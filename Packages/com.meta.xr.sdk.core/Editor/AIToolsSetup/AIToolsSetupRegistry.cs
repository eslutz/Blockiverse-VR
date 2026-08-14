/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Collections.Generic;
using System.Linq;

namespace Meta.XR.Editor
{
    internal static class AIToolsSetupRegistry
    {
        private static readonly List<IAIToolsProvider> Providers = new();

        internal static void Register(IAIToolsProvider provider)
        {
            if (Providers.Any(p => p.Id == provider.Id))
                return;
            Providers.Add(provider);
        }

        internal static void Unregister(string id)
        {
            Providers.RemoveAll(p => p.Id == id);
        }

        internal static IReadOnlyList<IAIToolsProvider> GetProviders()
        {
            return Providers;
        }

        internal static IAIToolsProvider GetProvider(string id)
        {
            return Providers.FirstOrDefault(p => p.Id == id);
        }

        internal static IReadOnlyList<IAIToolsProvider> GetProvidersSorted()
        {
            return Providers.OrderBy(p => p.ToolCard.Order).ToList();
        }

        internal static void Clear()
        {
            Providers.Clear();
        }
    }
}
