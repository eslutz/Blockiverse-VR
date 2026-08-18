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

using Object = UnityEngine.Object;

namespace MCPServices.Tools
{
    internal static class UnityObjectId
    {
#if UNITY_6000_5_OR_NEWER
        internal static ulong From(Object obj) => obj.GetEntityId().GetRawData();
        internal static bool Matches(Object obj, ulong id) => From(obj) == id;
#else
        internal static ulong From(Object obj) => unchecked((ulong)(uint)obj.GetInstanceID());
        internal static bool Matches(Object obj, ulong id) => From(obj) == id;
#endif
    }
}
