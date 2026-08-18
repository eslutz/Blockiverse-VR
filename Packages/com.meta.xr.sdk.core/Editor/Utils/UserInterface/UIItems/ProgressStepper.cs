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
    /// A progress stepper component following the RLDS design system.
    /// Shows discrete steps as segmented bars, with completed, current, and upcoming states.
    /// Optionally clickable to jump to a specific step.
    /// </summary>
    internal class ProgressStepper : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly int _stepCount;
        private int _currentStep;
        private readonly Action<int> _onStepClick;
        private VisualElement _container;
        private VisualElement[] _steps;

        /// <param name="stepCount">Total number of steps (minimum 2)</param>
        /// <param name="currentStep">Zero-based index of the current step</param>
        /// <param name="onStepClick">Callback when a step is clicked (null for non-clickable). Receives the step index.</param>
        public ProgressStepper(int stepCount, int currentStep, Action<int> onStepClick = null)
        {
            _stepCount = Math.Max(2, stepCount);
            _currentStep = Math.Clamp(currentStep, 0, _stepCount - 1);
            _onStepClick = onStepClick;
        }

        public void Draw()
        {
            // Not implemented - ProgressStepper is UIToolkit only
        }

        /// <summary>
        /// Updates the current step and refreshes visual states.
        /// </summary>
        public void SetCurrentStep(int step)
        {
            _currentStep = Math.Clamp(step, 0, _stepCount - 1);
            if (_steps == null) return;
            for (var i = 0; i < _steps.Length; i++)
            {
                ApplyStepState(_steps[i], i);
            }
        }

        public int GetCurrentStep() => _currentStep;

        public VisualElement Build()
        {
            _container = new VisualElement();
            _container.AddToClassList(RLDSConstants.ProgressStepper.Container);

            _steps = new VisualElement[_stepCount];

            for (var i = 0; i < _stepCount; i++)
            {
                var step = new VisualElement();
                step.AddToClassList(RLDSConstants.ProgressStepper.Step);
                ApplyStepState(step, i);

                if (i < _stepCount - 1)
                {
                    step.style.marginRight = RLDSConstants.Spacing.SizeXS;
                }

                if (_onStepClick != null)
                {
                    step.AddToClassList(RLDSConstants.ProgressStepper.StepClickable);
                    var stepIndex = i;
                    step.RegisterCallback<ClickEvent>(_ =>
                    {
                        SetCurrentStep(stepIndex);
                        _onStepClick.Invoke(stepIndex);
                    });
                }

                _steps[i] = step;
                _container.Add(step);
            }

            return _container;
        }

        private void ApplyStepState(VisualElement step, int index)
        {
            step.RemoveFromClassList(RLDSConstants.ProgressStepper.StepCompleted);
            step.RemoveFromClassList(RLDSConstants.ProgressStepper.StepCurrent);

            if (index < _currentStep)
            {
                step.AddToClassList(RLDSConstants.ProgressStepper.StepCompleted);
            }
            else if (index == _currentStep)
            {
                step.AddToClassList(RLDSConstants.ProgressStepper.StepCurrent);
            }
        }
    }
}
