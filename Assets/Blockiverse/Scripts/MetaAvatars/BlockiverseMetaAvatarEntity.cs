using Oculus.Avatar2;
using UnityEngine;

namespace Blockiverse.MetaAvatars
{
    public sealed class BlockiverseMetaAvatarEntity : OvrAvatarEntity
    {
        MetaAvatarTrackingSources trackingSources = MetaAvatarTrackingSources.Empty;
        bool hideFirstPersonHead;

        public void ConfigurePresentation(MetaAvatarPresentationMode mode, bool hideHeadForFirstPerson)
        {
            hideFirstPersonHead = mode == MetaAvatarPresentationMode.LocalFirstPerson && hideHeadForFirstPerson;
            SetIsLocal(mode == MetaAvatarPresentationMode.LocalFirstPerson);
            ApplyFirstPersonVisibility();
        }

        public void SetTrackingSources(MetaAvatarTrackingSources sources)
        {
            trackingSources = sources;
            SetTrackingSourcesFromTransforms();
        }

        public bool TryLoadUserAvatar(ulong userId)
        {
            if (!IsCreated || userId == 0)
                return false;

            _userId = userId;
            LoadUser();
            return true;
        }

        public bool TryLoadPresetAvatar(string presetPath)
        {
            if (!IsCreated || string.IsNullOrWhiteSpace(presetPath))
                return false;

            return LoadAssets(new[] { presetPath }, AssetSource.Zip);
        }

        public void SetTrackingSourcesFromTransforms()
        {
            if (trackingSources.Head != null)
                transform.SetPositionAndRotation(trackingSources.Head.position, trackingSources.Head.rotation);
        }

        void ApplyFirstPersonVisibility()
        {
            Transform head = transform.Find("Head");
            if (head != null)
                head.gameObject.SetActive(!hideFirstPersonHead);
        }
    }
}
