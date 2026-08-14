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

namespace Meta.XR.Guides.Editor.Nux
{
    internal static class NuxSettings
    {
        internal enum StepId
        {
            Intro = 0,
            SkillLevel = 1,
            Role = 2,
        }

        internal const int StepCount = 3;

        internal const int WindowWidth = 1024;
        internal const int WindowHeight = 768;

        internal static class SkillLevels
        {
            internal const string Beginner = "beginner";
            internal const string Intermediate = "intermediate";
            internal const string Advanced = "advanced";

            internal static readonly SkillLevelDefinition[] All =
            {
                new(Beginner, Content.BeginnerLabel, Content.BeginnerDescription),
                new(Intermediate, Content.IntermediateLabel, Content.IntermediateDescription),
                new(Advanced, Content.AdvancedLabel, Content.AdvancedDescription),
            };
        }

        internal readonly struct SkillLevelDefinition
        {
            internal readonly string Id;
            internal readonly string Label;
            internal readonly string Description;

            internal SkillLevelDefinition(string id, string label, string description)
            {
                Id = id;
                Label = label;
                Description = description;
            }
        }

        internal static class Roles
        {
            internal const string UnityDeveloper = "unity_developer";
            internal const string XRDeveloper = "xr_developer";
            internal const string Artist3D = "3d_artist";
            internal const string GameDesigner = "game_designer";
            internal const string TechnicalArtist = "technical_artist";
            internal const string ToolsEngineer = "tools_engineer";
            internal const string QALiveOps = "qa_live_ops";
            internal const string Animator = "animator";
            internal const string Other = "other";

            internal static readonly RoleDefinition[] All =
            {
                new(UnityDeveloper, "Unity Developer", "ibeam-cursor"),
                new(XRDeveloper, "XR Developer", "headset-alt"),
                new(Artist3D, "3D Artist", "vr-object"),
                new(GameDesigner, "Game Designer", "gamepad"),
                new(TechnicalArtist, "Technical Artist", "media-immersive-photo"),
                new(ToolsEngineer, "Tools Engineer", "editor"),
                new(QALiveOps, "QA/Live ops", "graphs"),
                new(Animator, "Animator", "avatar-emote"),
                new(Other, "Other", "category-basic"),
            };
        }

        internal readonly struct FeatureItem
        {
            internal readonly string Text;
            internal readonly string IconName;

            internal FeatureItem(string text, string iconName)
            {
                Text = text;
                IconName = iconName;
            }
        }

        internal readonly struct RoleDefinition
        {
            internal readonly string Id;
            internal readonly string Label;
            internal readonly string IconName;

            internal RoleDefinition(string id, string label, string iconName)
            {
                Id = id;
                Label = label;
                IconName = iconName;
            }
        }

        internal static class Content
        {
            // Intro step
            internal const string IntroTitle = "Welcome to Meta XR SDK";

            internal const string IntroSubtitle =
                "Build immersive experiences for Quest and Meta devices with Unity.";

            internal const string WhatsIncludedHeader = "What's included";

            internal static readonly FeatureItem[] WhatsIncludedItems =
            {
                new("Pre-built XR Building Blocks \u2014 drag and drop to create interactions, tracking, and UI", "default-app"),
                new("Automated Unity setup and testing tools", "tools"),
                new("AI-powered debugging and optimization for Meta devices", "ai-agent"),
                new("Official standards testing to ship faster and pass review", "list-checked"),
            };

            // Skill level step
            internal const string SkillLevelTitle = "What\u2019s your experience level?";

            internal const string SkillLevelSubtitle =
                "We\u2019ll suggest tools and resources based on your experience.";

            internal const string BeginnerLabel = "Beginner";
            internal const string BeginnerDescription = "I\u2019m new to XR development";
            internal const string IntermediateLabel = "Intermediate";
            internal const string IntermediateDescription = "I\u2019ve built XR experiences before";
            internal const string AdvancedLabel = "Advanced";
            internal const string AdvancedDescription = "I\u2019m an experienced XR developer";

            // Role step
            internal const string RoleTitle = "What\u2019s your role?";

            internal const string RoleSubtitle =
                "This helps us show you relevant tools, examples, and documentation.";

            // Footer
            internal const string BackLabel = "Back";
            internal const string NextLabel = "Next";
            internal const string GetStartedLabel = "Get Started";
            internal const string FinishSetupLabel = "Finish setup";
        }
    }
}
