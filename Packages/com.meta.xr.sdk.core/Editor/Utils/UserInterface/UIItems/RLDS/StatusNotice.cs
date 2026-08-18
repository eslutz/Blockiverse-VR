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
    internal enum StatusNoticeVariant
    {
        Positive,
        Negative,
        Processing
    }

    /// <summary>
    /// A block-level status notice banner following the RLDS design system.
    /// Displays a colored background with border, status icon, label text, and optional action button.
    /// Used for connection status, validation results, and progress indicators.
    /// </summary>
    internal class StatusNotice
    {
        private readonly string _label;
        private readonly StatusNoticeVariant _variant;
        private readonly VisualElement _leadingElement;
        private readonly string _actionLabel;
        private readonly System.Action _onAction;

        public StatusNotice(
            string label,
            StatusNoticeVariant variant,
            VisualElement leadingElement = null,
            string actionLabel = null,
            System.Action onAction = null)
        {
            _label = label;
            _variant = variant;
            _leadingElement = leadingElement;
            _actionLabel = actionLabel;
            _onAction = onAction;
        }

        public VisualElement Build()
        {
            var container = new VisualElement();
            container.AddToClassList(RLDSConstants.StatusNotice.Base);

            string variantClass = _variant switch
            {
                StatusNoticeVariant.Positive => RLDSConstants.StatusNotice.Positive,
                StatusNoticeVariant.Negative => RLDSConstants.StatusNotice.Negative,
                StatusNoticeVariant.Processing => RLDSConstants.StatusNotice.Processing,
                _ => RLDSConstants.StatusNotice.Processing
            };
            container.AddToClassList(variantClass);

            var left = new VisualElement();
            left.AddToClassList(RLDSConstants.StatusNotice.Left);

            if (_leadingElement != null)
            {
                left.Add(_leadingElement);
            }

            var label = new UnityEngine.UIElements.Label(_label);
            label.AddToClassList(RLDSConstants.StatusNotice.Label);
            left.Add(label);

            container.Add(left);

            if (_onAction != null && _actionLabel != null)
            {
                UnityEngine.UIElements.Button actionBtn = null;
                actionBtn = new UnityEngine.UIElements.Button(() =>
                {
                    RLDSTelemetry.SendInteraction(actionBtn, GetType().Name, _actionLabel, _actionLabel);
                    _onAction.Invoke();
                })
                { text = _actionLabel };
                actionBtn.AddToClassList(RLDSConstants.Button.SecondarySmall);
                container.Add(actionBtn);
            }

            return container;
        }
    }
}
