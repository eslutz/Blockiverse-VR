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

using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// A small, uppercase category label used to group menu items in the SDK menu.
    /// The provided label is always rendered in upper case per the design spec.
    /// </summary>
    internal class CategoryLabel : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly string _label;
        private VisualElement _root;

        public CategoryLabel(string label)
        {
            _label = label?.ToUpperInvariant() ?? string.Empty;
        }

        public void Draw()
        {
            // UIToolkit-only component; IMGUI rendering is not supported.
        }

        public VisualElement Build()
        {
            if (_root != null)
            {
                return _root;
            }

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.CategoryLabel.Root);

            var labelElement = new UnityEngine.UIElements.Label(_label);
            labelElement.AddToClassList(RLDSConstants.CategoryLabel.Label);
            _root.Add(labelElement);

            return _root;
        }
    }
}
