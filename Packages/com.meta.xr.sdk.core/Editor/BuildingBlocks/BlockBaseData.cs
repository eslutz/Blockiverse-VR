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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Tags;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.Settings;

namespace Meta.XR.BuildingBlocks.Editor
{
    /// <summary>
    /// Serves as the abstract base class for all building block data assets.
    /// </summary>
    public abstract class BlockBaseData : ScriptableObject, ITaggable, IIdentified, IComparable<BlockBaseData>
    {
        internal static readonly CachedIdDictionary<BlockBaseData> Registry = new();

        /// <summary>
        /// Gets the unique identifier of this building block.
        /// </summary>
        [SerializeField, OVRReadOnly] internal string id = Guid.NewGuid().ToString();
        public string Id => id;

        /// <summary>
        /// Gets the version number of this building block.
        /// </summary>
        [SerializeField, OVRReadOnly] internal int version = 1;
        public int Version => version;

        private static TextureContent _defaultThumbnailTexture;
        private static TextureContent DefaultThumbnailTexture => _defaultThumbnailTexture ??=
            TextureContent.CreateContent("bb_thumb_default.jpg", Utils.BuildingBlocksThumbnails);

        private static TextureContent _defaultInternalThumbnailTexture;
        internal static TextureContent DefaultInternalThumbnailTexture => _defaultInternalThumbnailTexture ??=
            TextureContent.CreateContent("bb_thumb_internal.jpg", Utils.BuildingBlocksThumbnails);

        /// <summary>
        /// Gets the overridable display name of this building block.
        /// </summary>
        [SerializeField] internal string blockName;
        public Overridable<string> BlockName { get; private set; } = new("");

        /// <summary>
        /// Gets the overridable text description of this building block.
        /// </summary>
        [TextArea(5, 40)]
        [SerializeField] internal string description;
        public Overridable<string> Description { get; private set; } = new("");

        #region Tags

        [SerializeField] internal TagArray tags;

        private TagArray SerializedTags => tags ??= new TagArray();
        private Overridable<TagArray> _overridableTags;

        /// <summary>
        /// Gets the overridable collection of tags assigned to this building block.
        /// </summary>
        public Overridable<TagArray> OverridableTags => _overridableTags ??= new Overridable<TagArray>(SerializedTags);

        /// <summary>
        /// Gets the current collection of tags assigned to this building block.
        /// </summary>
        public TagArray Tags => OverridableTags.Value;

        internal virtual void OnEnable()
        {
            Description = new Overridable<string>(description);
            BlockName = new Overridable<string>(blockName);
            RemoteThumbnailContentId = new Overridable<ulong>(remoteThumbnailContentId);
        }

        /// <summary>
        /// Initializes the block base data when it awakens.
        /// </summary>
        public void OnAwake()
        {
            ValidateTags();
        }

        /// <summary>
        /// Validates the block base data when a value changes in the inspector.
        /// </summary>
        public void OnValidate()
        {
            ValidateTags();
        }

        private void ValidateTags()
        {
            {
                Tags.Remove(Utils.InternalTag);
            }

            if (IsNew())
            {
                Tags.Add(Utils.NewTag);
            }
            else
            {
                Tags.Remove(Utils.NewTag);
            }

            if (NewVersionAvailable())
            {
                Tags.Add(Utils.NewVersionTag);
            }
            else
            {
                Tags.Remove(Utils.NewVersionTag);
            }

            Tags.OnValidate();
        }

        private CustomBool _hasSeenBefore;
        private CustomInt _hasSeenVersionBefore;

        private bool IsNew()
        {
            _hasSeenBefore ??= new UserBool()
            {
                Owner = this,
                Uid = "HasSeenBefore",
                OldKey = $"OVRProjectSetup.HasSeenBeforeKey_{Id}",
                Default = false,
                SendTelemetry = false
            };
            return !_hasSeenBefore.Value;
        }

        private bool NewVersionAvailable()
        {
            if (_hasSeenBefore is not { Value: true })
            {
                // haven't seen, not showing as new version, just new.
                return false;
            }
            _hasSeenVersionBefore ??= new UserInt()
            {
                Owner = this,
                Uid = "HasSeenVersionBefore",
                Default = 1,
                SendTelemetry = false
            };
            return version > _hasSeenVersionBefore.Value;
        }

        internal void MarkAsSeen()
        {
            if (_hasSeenBefore == null || _hasSeenBefore.Value)
            {
                _hasSeenVersionBefore ??= new UserInt()
                {
                    Owner = this,
                    Uid = "HasSeenVersionBefore",
                    Default = 1,
                    SendTelemetry = false
                };
                if (_hasSeenVersionBefore.Value != version)
                {
                    _hasSeenVersionBefore.SetValue(version);
                    ValidateTags();
                }
                return;
            }
            _hasSeenBefore.SetValue(true);
            _hasSeenVersionBefore?.SetValue(version);
            ValidateTags();
        }

        internal void ResetSeen()
        {
            _hasSeenBefore.SetValue(false);
            ValidateTags();
        }

        internal void ResetVersionSeen()
        {
            _hasSeenVersionBefore?.SetValue(1);
            ValidateTags();
        }
        #endregion

        [SerializeField] internal Texture2D thumbnail;
        [SerializeField] internal ulong remoteThumbnailContentId;
        /// <summary>
        /// Gets the overridable content ID for the remotely hosted thumbnail image.
        /// </summary>
        public Overridable<ulong> RemoteThumbnailContentId { get; private set; } = new(0ul);

        private RemoteTextureContent _remoteThumbnail;
        private RemoteTextureContent RemoteThumbnail
        {
            get
            {
                if (RemoteThumbnailContentId == 0ul) return null;
                if (_remoteThumbnail?.ContentId != RemoteThumbnailContentId)
                {
                    _remoteThumbnail = RemoteTextureContent.CreateWithAutoDownload(
                        RemoteThumbnailContentId,
                        Utils.BuildingBlocksThumbnails);
                }

                return _remoteThumbnail;
            }
        }


        public Texture2D Thumbnail
        {
            get
            {
                if (RemoteThumbnail?.Valid ?? false)
                {
                    return RemoteThumbnail.Image as Texture2D;
                }

                if (thumbnail != null)
                {
                    return thumbnail;
                }

                if (!Hidden)
                {
                    return DefaultThumbnailTexture.Image as Texture2D;
                }

                return DefaultInternalThumbnailTexture.Image as Texture2D;
            }
        }

        /// <summary>
        /// Gets whether this block is hidden based on its tag visibility settings.
        /// </summary>
        public virtual bool Hidden => Tags.Any(tag => tag.Behavior.Visibility == false);

        /// <summary>
        /// Gets whether this block is tagged as experimental.
        /// </summary>
        public bool Experimental => Tags.Contains(Utils.ExperimentalTag);



        /// <summary>
        /// Block can be installed into the scene
        /// </summary>
        internal virtual bool IsInstallable => !Utils.IsApplicationPlaying.Invoke();

        /// <summary>
        /// User can try to install the block into the scene
        /// </summary>
        /// <remarks>
        /// Note the difference with <see cref="IsInstallable"/>: it is possible for the user to try
        /// even though <see cref="IsInstallable"/> is <code>false</code>, the UI may react and propose
        /// to fix the missing links via a popup.
        /// We recommend using this property in the front-end instead of <see cref="IsInstallable"/>
        /// </remarks>
        internal virtual bool IsInteractable => IsInstallable;

        internal abstract Task AddToProject(GameObject selectedGameObject = null, Action onInstall = null);

        internal virtual async Task AddToObjects(List<GameObject> selectedGameObjects)
        {
            foreach (var obj in selectedGameObjects.DefaultIfEmpty())
            {
                await AddToProject(obj);
            }
        }

        internal virtual bool RequireListRefreshAfterInstall => false;

        internal virtual bool OverridesInstallRoutine => false;

        /// <summary>
        /// Compares this block base data to another by block name for sorting purposes.
        /// </summary>
        /// <param name="other">The other block base data to compare against.</param>
        /// <returns>An integer indicating the relative sort order of the two instances.</returns>
        public int CompareTo(BlockBaseData other)
        {
            return other == null ? 0 : string.Compare(other.BlockName.Value, BlockName.Value, StringComparison.CurrentCultureIgnoreCase);
        }

    }
}
