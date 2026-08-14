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
using UnityEngine.UI;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    /// <summary>
    /// This is a <see cref="MonoBehaviour"/> for the UI container that can be vertically scrolled. It manages the child <see cref="ScrollViewport"/>
    /// Used by inspector debug data and console logs of Immersive Debugger.
    /// For more info about Immersive Debugger, check out the [official doc](https://developer.oculus.com/documentation/unity/immersivedebugger-overview)
    /// </summary>
    public class ScrollView : InteractableController
    {
        private ScrollRect _scrollRect;
        private ScrollViewport _viewport;
        private Mask _mask;

        internal ScrollRect ScrollRect => _scrollRect;
        internal Flex Flex => _viewport.Flex;

        private float _previousProgress;
        private float _previousContentOffset;
        private bool _preventScrollPreservation = false;

        internal event Action OnUserScrolled;

        /// <summary>
        /// When true, a layout refresh preserves the absolute content offset instead of the normalized
        /// scroll position. Use this when the view is not pinned to the bottom and content grows at the
        /// bottom: preserving the normalized position would creep the view toward the top as the total
        /// height increases, whereas the absolute offset keeps the same content in view.
        /// </summary>
        public bool PreserveAbsolutePosition { get; set; }

        /// <summary>
        /// The progress of the scroll view, specifically representing the normalized vertical position of the rectangle.
        /// </summary>
        public float Progress
        {
            get => _scrollRect.verticalNormalizedPosition;
            set => _scrollRect.verticalNormalizedPosition = value;
        }

        /// <summary>
        /// The absolute vertical scroll offset of the content, in pixels and independent of content height.
        /// </summary>
        public float ContentVerticalOffset
        {
            get => _scrollRect.content != null ? _scrollRect.content.anchoredPosition.y : 0f;
            set
            {
                if (_scrollRect.content == null)
                {
                    return;
                }

                var position = _scrollRect.content.anchoredPosition;
                position.y = value;
                _scrollRect.content.anchoredPosition = position;
            }
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            _scrollRect = GameObject.AddComponent<PanelScrollRect>();

            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.inertia = true;

            ((PanelScrollRect)_scrollRect).UserInteracted += HandleUserInteracted;

            _viewport = Append<ScrollViewport>("viewport");
            _viewport.LayoutStyle = Style.Load<LayoutStyle>("Fill");

            _scrollRect.content = _viewport.Flex.RectTransform;
        }

        private void HandleUserInteracted()
        {
            OnUserScrolled?.Invoke();
        }

        protected override void RefreshLayoutPreChildren()
        {
            if (!_preventScrollPreservation)
            {
                _previousProgress = Progress;
                _previousContentOffset = ContentVerticalOffset;
            }

            base.RefreshLayoutPreChildren();
        }

        protected override void RefreshLayoutPostChildren()
        {
            if (!_preventScrollPreservation)
            {
                if (PreserveAbsolutePosition)
                {
                    ContentVerticalOffset = _previousContentOffset;
                }
                else
                {
                    Progress = _previousProgress;
                }
            }
            else
            {
                _preventScrollPreservation = false; // Reset flag after use
            }

            base.RefreshLayoutPostChildren();
        }

        /// <summary>
        /// Sets the scroll position without preservation during layout updates
        /// </summary>
        /// <param name="progress">The scroll progress (0.0f = top, 1.0f = bottom)</param>
        public void SetProgressWithoutPreservation(float progress)
        {
            _preventScrollPreservation = true;
            _previousProgress = progress;
            Progress = progress;
        }
    }
}

