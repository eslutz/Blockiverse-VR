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

using System;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// A slider component following the RLDS design system.
    /// Similar to Unity's Slider/Range but with custom RLDS styling.
    /// </summary>
    internal class Slider : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly string _label;
        private readonly float _minValue;
        private readonly float _maxValue;
        private float _value;
        private readonly Action<float> _onValueChanged;
        private VisualElement _container;
        private UnityEngine.UIElements.Slider _slider;

        /// <param name="label">Label text for the slider</param>
        /// <param name="minValue">Minimum value</param>
        /// <param name="maxValue">Maximum value</param>
        /// <param name="value">Initial value</param>
        /// <param name="onValueChanged">Callback when value changes</param>
        public Slider(string label = "", float minValue = 0f, float maxValue = 1f, float value = 0f,
            Action<float> onValueChanged = null)
        {
            _label = label;
            _minValue = minValue;
            _maxValue = maxValue;
            _value = Math.Clamp(value, minValue, maxValue);
            _onValueChanged = onValueChanged;
        }

        public float Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, _minValue, _maxValue);
                if (_slider != null)
                {
                    _slider.SetValueWithoutNotify(_value);
                }
            }
        }

        public void Draw()
        {
        }

        public VisualElement Build()
        {
            _container = new VisualElement();
            _container.AddToClassList(RLDSConstants.Slider.Base);

            _slider = new UnityEngine.UIElements.Slider(_label, _minValue, _maxValue)
            {
                value = _value,
#if UNITY_6000_0_OR_NEWER
                fill = true
#endif
            };

            _slider.RegisterValueChangedCallback(evt =>
            {
                _value = evt.newValue;
                _onValueChanged?.Invoke(evt.newValue);
            });

            _container.Add(_slider);

            return _container;
        }
    }
}
