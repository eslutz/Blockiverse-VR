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
using Meta.XR.Editor.ToolingSupport;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.StatusMenu
{
    internal class StatusMenu : EditorWindow
    {
        private static IReadOnlyList<ToolDescriptor> _registeredItems;

        public static bool Visible => StatusMenuDrawer.Visible;

        private static void PrepareItems()
        {
            var registeredItems = ToolRegistry.Registry.Where(item => item.AddToStatusMenu && item.IsRampedUp).ToList();
            registeredItems.Sort((x, y) => x.Order.CompareTo(y.Order));
            _registeredItems = registeredItems;
        }

        public static ToolDescriptor GetHighestItem()
        {
            if (_registeredItems == null)
            {
                PrepareItems();
            }

            foreach (var item in _registeredItems)
            {
                var (_, color, showNotification) = item.PillIcon?.Invoke() ?? default;

                if (!showNotification)
                {
                    continue;
                }

                if (color.HasValue)
                {
                    return item;
                }
            }

            return default;
        }

        public static void ShowDropdown(Rect source)
        {
            PrepareItems();

            if (_registeredItems.Count == 0)
            {
                return;
            }

            var (isUpdate, latestVersion) = GetUpdateState();
            StatusMenuDrawer.ShowDropdown(source, _registeredItems, isUpdate, latestVersion);
        }

        private static (bool isUpdateAvailable, string latestVersion) GetUpdateState()
        {
            foreach (var item in ToolRegistry.Registry)
            {
                if (item.AvailableVersionDelegate == null || item.PillIcon == null) continue;
                var (_, color, showNotification) = item.PillIcon();
                if (!showNotification || !color.HasValue) continue;

                var version = item.AvailableVersionDelegate();
                if (version.HasValue)
                {
                    return (true, version.Value.ToString());
                }
            }
            return (false, null);
        }
    }
}
