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
using Meta.XR.Editor.Id;
using UnityEngine;

namespace Meta.XR.Editor.Tags
{
    /// <summary>
    /// Represents a named tag that can be associated with editor items for categorization and filtering.
    /// </summary>
    [Serializable]
    public struct Tag : IEquatable<Tag>, IIdentified
    {
        internal enum TagListType
        {
            Overlays,
            Filters,
            Description,
        }

        internal static readonly TagArray Registry = new TagArray();

        [SerializeField] private string name;
        private TagBehavior _behavior;

        /// <summary>
        /// Initializes a new tag with the specified name and registers it in the global tag registry.
        /// </summary>
        /// <param name="name">The name of the tag.</param>
        public Tag(string name)
        {
            this.name = name;
            _behavior = null;
            OnValidate();
        }

        /// <summary>
        /// Validates the tag by resolving its behavior and registering or updating it in the global registry.
        /// </summary>
        /// <param name="update">If true, updates the existing registry entry instead of adding a new one.</param>
        public void OnValidate(bool update = false)
        {
            if (!Valid) return;
            _behavior = TagBehavior.GetBehavior(this);

            if (update)
            {
                Registry.Update(this);
                return;
            }

            Registry.Add(this);
        }

        /// <summary>
        /// Gets the name of this tag.
        /// </summary>
        public string Name => name;
        /// <summary>
        /// Gets the unique identifier for this tag, which is the same as its name.
        /// </summary>
        public string Id => Name;
        internal bool Valid => Name != null;

        internal TagBehavior Behavior => _behavior ??= TagBehavior.GetBehavior(this);


        /// <summary>
        /// Determines whether this tag is equal to another tag by comparing their names.
        /// </summary>
        /// <param name="other">The other tag to compare with.</param>
        /// <returns>True if the tags have the same name; otherwise, false.</returns>
        public bool Equals(Tag other) => Name == other.Name;
        /// <summary>
        /// Determines whether this tag is equal to the specified object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True if the object is a Tag with the same name; otherwise, false.</returns>
        public override bool Equals(object obj) => obj is Tag other && Equals(other);
        /// <summary>
        /// Returns a hash code based on the tag's name.
        /// </summary>
        /// <returns>A hash code for the current tag.</returns>
        public override int GetHashCode() => (Name != null ? Name.GetHashCode() : 0);
        /// <summary>
        /// Implicitly converts a string to a Tag.
        /// </summary>
        /// <param name="s">The string to convert.</param>
        public static implicit operator Tag(string s) => new Tag(s);
        /// <summary>
        /// Implicitly converts a Tag to its name string.
        /// </summary>
        /// <param name="tag">The tag to convert.</param>
        public static implicit operator string(Tag tag) => tag.Name;

        internal static Comparison<Tag> Sorter => (lhs, rhs) =>
        {
            var orderComparison = lhs.Behavior.Order.CompareTo(rhs.Behavior.Order);
            return orderComparison != 0 ? orderComparison : string.Compare(lhs.Name, rhs.Name, StringComparison.Ordinal);
        };

        /// <summary>
        /// Draws the tag in the editor using its associated behavior style.
        /// </summary>
        /// <param name="inline">If true, draws the tag inline rather than on its own line.</param>
        public void Draw(bool inline = false) => Behavior.DrawSimple(inline);
    }
}
