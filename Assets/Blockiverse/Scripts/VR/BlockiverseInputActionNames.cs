namespace Blockiverse.VR
{
    public static class BlockiverseInputActionNames
    {
        public const string HeadMap = "XRI Head";
        public const string LeftHandMap = "XRI LeftHand";
        public const string RightHandMap = "XRI RightHand";
        public const string GameplayMap = "Blockiverse Gameplay";

        public const string Position = "Position";
        public const string Rotation = "Rotation";
        public const string LeftEyePosition = "Left Eye Position";
        public const string LeftEyeRotation = "Left Eye Rotation";
        public const string RightEyePosition = "Right Eye Position";
        public const string RightEyeRotation = "Right Eye Rotation";
        public const string AimPosition = "Aim Position";
        public const string AimRotation = "Aim Rotation";
        public const string IsTracked = "Is Tracked";
        public const string TrackingState = "Tracking State";
        public const string Select = "Select";
        public const string Activate = "Activate";
        public const string PrimaryButton = "Primary Button";
        public const string SecondaryButton = "Secondary Button";
        public const string UiPress = "UI Press";
        public const string UiScroll = "UI Scroll";
        public const string HapticDevice = "Haptic Device";
        public const string Move = "Move";
        public const string Turn = "Turn";
        public const string TeleportMode = "Teleport Mode";
        public const string TeleportSelect = "Teleport Select";
        public const string Menu = "Menu";
        public const string Jump = "Jump";
        public const string BlockEditingToggle = "Block Editing Toggle";
        public const string Sprint = "Sprint";
        public const string Crouch = "Crouch";
        public const string HeightReset = "Height Reset";
        public const string Undo = "Undo";
    }

    public static class BlockiverseDeterministicInputIds
    {
        public static System.Guid ForMap(string mapName)
        {
            return CreateGuid("map", mapName);
        }

        public static System.Guid ForAction(string mapName, string actionName)
        {
            return CreateGuid("action", mapName, actionName);
        }

        public static System.Guid ForBinding(string mapName, string actionName, string bindingKey)
        {
            return CreateGuid("binding", mapName, actionName, bindingKey);
        }

        static System.Guid CreateGuid(params string[] parts)
        {
            string input = "blockiverse-input:" + string.Join(":", parts);

            using System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));

            hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

            return new System.Guid(hash);
        }
    }

    public static class BlockiverseInputActionReferencePaths
    {
        public static string GetReferencePath(string mapName, string actionName)
        {
            return Blockiverse.Core.BlockiverseProject.InputActionReferencesFolderPath +
                   "/" +
                   Sanitize(mapName) +
                   "__" +
                   Sanitize(actionName) +
                   ".asset";
        }

        static string Sanitize(string value)
        {
            return value
                .ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("/", "_")
                .Replace("{", string.Empty)
                .Replace("}", string.Empty);
        }
    }
}
