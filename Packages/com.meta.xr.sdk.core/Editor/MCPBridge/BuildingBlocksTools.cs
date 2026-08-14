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
using Meta.MCPBridge.Attributes;
using Meta.MCPBridge.Services;
using Meta.XR.BuildingBlocks;
using Meta.XR.BuildingBlocks.Editor;
using Meta.XR.Json;
using UnityEditor;
using UnityEngine;

namespace MCPServices.Tools
{
    [Tool(
        "Add and configure Meta Quest XR features in a Unity project. Features include hand tracking, passthrough (mixed reality), controller input, spatial anchors, multiplayer, grab/poke/ray interactions, eye tracking, voice, AI, haptics, and more. Each feature is a self-contained module (called a 'Building Block') that can be searched, installed into a scene, and customized.",
        "WHEN TO USE: Use when the user wants to add, find, enable, or configure a Meta Quest / VR / MR / XR feature in their Unity project. Also use when listing what features are available or already installed.",
        "WORKFLOW: 1) SearchBlocks(query) to find features by name (e.g. 'hand tracking', 'passthrough', 'grab') 2) GetBlockInfo(blockId) for details and dependencies 3) InstallBlock(blockId) to add to the scene (dependencies auto-install) 4) GetConfigurableProperties(blockId) + SetBlockProperty() to customize after installation. Use ListBlocks() to browse all available features, or ListInstalledBlocks() to see what's already in the scene.",
        "IMPORTANT: Each feature has an ID (a GUID). Use SearchBlocks() or ListBlocks() to discover IDs — do not guess them. Some features depend on others and those dependencies are resolved automatically during installation."
    )]
    internal class BuildingBlocksTools : SingletonService<BuildingBlocksTools>
    {
        [Tool(Description = "List all available Quest/XR features that can be added to the Unity project. Optionally filter by tag (e.g. 'Interaction', 'Passthrough', 'Multiplayer', 'Tracking', 'AI', 'Voice'). Shows feature name, ID, tags, and whether it's already installed in the scene.",
            Returns = "List of available features with their IDs and metadata")]
        internal string ListBlocks(string tag = null)
        {
            IEnumerable<BlockBaseData> blocks = Utils.FilteredRegistry
                .Where(b => !b.Hidden)
                .OrderByDescending(b => b);

            if (!string.IsNullOrEmpty(tag))
            {
                blocks = blocks.Where(b =>
                    b.Tags.Any(t => t.Name.Contains(tag, StringComparison.OrdinalIgnoreCase)));
            }

            var blockList = blocks.ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"=== BUILDING BLOCKS ({blockList.Count}) ===");
            if (!string.IsNullOrEmpty(tag)) sb.AppendLine($"Filter: Tag={tag}");
            sb.AppendLine();

            if (blockList.Count == 0)
            {
                sb.AppendLine("No blocks found matching the criteria.");
                return sb.ToString();
            }

            foreach (var block in blockList)
            {
                var blockData = block as BlockData;
                var inScene = blockData != null && blockData.ComputeNumberOfBlocksInScene() > 0;
                var tagNames = string.Join(", ", block.Tags.Select(t => t.Name));

                sb.Append($"  {block.BlockName}");
                if (inScene) sb.Append(" [INSTALLED]");
                sb.AppendLine();
                sb.AppendLine($"    ID: {block.Id}");
                if (!string.IsNullOrEmpty(tagNames))
                    sb.AppendLine($"    Tags: {tagNames}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        [Tool(Description = "Search for Quest/XR features by name or tag (e.g. 'hand tracking', 'grab', 'passthrough', 'multiplayer'). Returns matching features with their IDs, which are needed for installation.",
            Returns = "List of matching features with IDs")]
        internal string SearchBlocks(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return "Error: query is required. Provide a search term to find blocks by name or tag.";
            }

            var blocks = Utils.FilteredRegistry
                .Where(b => !b.Hidden)
                .Where(b =>
                    b.BlockName.Value.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || b.Tags.Any(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(b => b)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"=== SEARCH RESULTS for '{query}' ({blocks.Count} matches) ===");
            sb.AppendLine();

            if (blocks.Count == 0)
            {
                sb.AppendLine("No blocks found. Try different keywords or use ListBlocks() to see all available blocks.");
                return sb.ToString();
            }

            foreach (var block in blocks)
            {
                var blockData = block as BlockData;
                var inScene = blockData != null && blockData.ComputeNumberOfBlocksInScene() > 0;

                sb.Append($"  {block.BlockName}");
                if (inScene) sb.Append(" [INSTALLED]");
                sb.AppendLine();
                sb.AppendLine($"    ID: {block.Id}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        [Tool(Description = "Get detailed information about a specific Quest/XR feature by its ID, including what it does, what other features it depends on, usage instructions, and whether it's already installed.",
            Returns = "Feature details including description, dependencies, and installation status")]
        internal string GetBlockInfo(string blockId)
        {
            if (string.IsNullOrEmpty(blockId))
            {
                return "Error: blockId is required. Use ListBlocks() or SearchBlocks() to find block IDs.";
            }

            var baseData = BlockBaseData.Registry[blockId];
            if (baseData == null)
            {
                return $"Error: No block found with ID '{blockId}'.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== {baseData.BlockName} ===");
            sb.AppendLine();
            sb.AppendLine($"ID: {baseData.Id}");
            sb.AppendLine($"Description: {baseData.Description}");

            var tagNames = string.Join(", ", baseData.Tags.Select(t => t.Name));
            if (!string.IsNullOrEmpty(tagNames))
                sb.AppendLine($"Tags: {tagNames}");

            if (baseData.Experimental)
                sb.AppendLine("Status: Experimental");

            if (baseData is BlockData blockData)
            {
                var inScene = blockData.ComputeNumberOfBlocksInScene();
                sb.AppendLine($"Installed in scene: {(inScene > 0 ? $"Yes ({inScene} instance(s))" : "No")}");
                sb.AppendLine($"Singleton: {(blockData.IsSingleton ? "Yes" : "No")}");
                sb.AppendLine($"Installable: {(blockData.IsInstallable ? "Yes" : "No")}");

                // Dependencies
                var deps = blockData.Dependencies.Where(d => d != null).ToList();
                if (deps.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Dependencies:");
                    foreach (var dep in deps)
                    {
                        sb.AppendLine($"  - {dep.BlockName} ({dep.Id})");
                    }
                }

                // Package dependencies
                var pkgDeps = blockData.PackageDependencies.ToList();
                if (pkgDeps.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Package Dependencies:");
                    foreach (var pkg in pkgDeps)
                    {
                        var installed = Utils.IsPackageInstalled(pkg);
                        sb.AppendLine($"  - {pkg} {(installed ? "[installed]" : "[missing]")}");
                    }
                }

                // Usage instructions
                if (!string.IsNullOrEmpty(blockData.UsageInstructions))
                {
                    sb.AppendLine();
                    sb.AppendLine($"Usage: {blockData.UsageInstructions}");
                }

                // Documentation
                if (!string.IsNullOrEmpty(blockData.FeatureDocumentationUrl))
                {
                    sb.AppendLine($"Documentation: {blockData.FeatureDocumentationUrl}");
                }
            }

            return sb.ToString();
        }

        [Tool(Description = "Get installation options for features that offer multiple setup configurations. Some features can be installed in different ways (e.g. different networking backends). Returns available configurations and their customizable parameters. For features with a single setup path, indicates to call InstallBlock directly.",
            Returns = "Available installation configurations with customizable parameters, or message to install directly")]
        internal string GetBlockInstallationOptions(string blockId)
        {
            if (string.IsNullOrEmpty(blockId))
            {
                return "Error: blockId is required. Use ListBlocks() or SearchBlocks() to find block IDs.";
            }

            var blockData = Utils.GetBlockData(blockId);
            if (blockData == null)
            {
                return $"Error: No block found with ID '{blockId}'. Use ListBlocks() to see available blocks.";
            }

            if (blockData is not InterfaceBlockData interfaceBlock)
            {
                return $"Block '{blockData.BlockName}' has no installation options - it uses a single default configuration.\nCall InstallBlock(\"{blockId}\") to install it directly.";
            }

            var routines = interfaceBlock.GetAvailableInstallationRoutines().ToList();
            if (routines.Count == 0)
            {
                return $"Error: Block '{blockData.BlockName}' has no available installation routines.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== INSTALLATION OPTIONS: {blockData.BlockName} ===");
            sb.AppendLine();
            sb.AppendLine($"This block supports {routines.Count} installation routine(s).");
            sb.AppendLine();

            foreach (var routine in routines)
            {
                sb.AppendLine($"Routine: {routine.DisplayName}");
                sb.AppendLine($"  ID: {routine.Id}");
                if (!string.IsNullOrEmpty(routine.Description))
                {
                    sb.AppendLine($"  Description: {routine.Description}");
                }

                // Definition variants
                var definitionVariants = routine.DefinitionVariants.ToList();
                if (definitionVariants.Count > 0)
                {
                    sb.AppendLine($"  Definition Variants:");
                    foreach (var variant in definitionVariants)
                    {
                        sb.AppendLine($"    - {variant.MemberInfo.Name}");
                        sb.AppendLine($"        Value: {variant.RawValue}");
                        if (!string.IsNullOrEmpty(variant.Attribute.Description))
                        {
                            sb.AppendLine($"        Description: {variant.Attribute.Description}");
                        }
                    }
                }

                // Parameter variants
                var parameterVariants = routine.ParameterVariants.ToList();
                if (parameterVariants.Count > 0)
                {
                    sb.AppendLine($"  Configurable Parameters:");
                    foreach (var variant in parameterVariants)
                    {
                        sb.AppendLine($"    - {variant.MemberInfo.Name}");
                        sb.AppendLine($"        Type: {variant.RawValue?.GetType().Name ?? "unknown"}");
                        sb.AppendLine($"        Default: {variant.RawValue}");
                        if (!string.IsNullOrEmpty(variant.Attribute.Description))
                        {
                            sb.AppendLine($"        Description: {variant.Attribute.Description}");
                        }
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("To install with a specific routine:");
            if (routines.Count == 1)
            {
                sb.AppendLine($"  InstallBlock(\"{blockId}\", routineId: \"{routines[0].Id}\")");
            }
            else
            {
                sb.AppendLine($"  InstallBlock(\"{blockId}\", routineId: \"<routine-id>\")");
            }
            sb.AppendLine();
            sb.AppendLine("To customize parameters (JSON format):");
            sb.AppendLine($"  InstallBlock(\"{blockId}\", routineId: \"<routine-id>\", variants: \"{{\\\"paramName\\\": \\\"value\\\"}}\")");

            return sb.ToString();
        }

        [Tool(Description = "List all Quest/XR features currently installed in the active Unity scene, showing their names, IDs, and associated GameObjects.",
            Returns = "List of installed features with their scene GameObjects")]
        internal string ListInstalledBlocks()
        {
            var blocksInScene = Utils.GetBlocksInScene();

            var sb = new StringBuilder();
            sb.AppendLine($"=== INSTALLED BLOCKS ({blocksInScene.Count}) ===");
            sb.AppendLine();

            if (blocksInScene.Count == 0)
            {
                sb.AppendLine("No Building Blocks installed in the current scene.");
                return sb.ToString();
            }

            foreach (var block in blocksInScene)
            {
                var blockData = block.GetBlockData();
                var name = blockData != null ? blockData.BlockName.Value : "Unknown";
                var id = block.BlockId;

                sb.AppendLine($"  {name}");
                sb.AppendLine($"    ID: {id}");
                sb.AppendLine($"    GameObject: {block.gameObject.name}");

                if (blockData != null && blockData.IsUpdateAvailableForBlock(block))
                {
                    sb.AppendLine($"    Update available: v{block.Version} -> v{blockData.Version}");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        [Tool(Description = "Install a Quest/XR feature into the current Unity scene by its ID. Required dependencies are automatically installed. Use SearchBlocks() or ListBlocks() to find feature IDs. For features with multiple setup options, specify routineId (see GetBlockInstallationOptions). The scene is saved after installation.",
            Returns = "Installation result - success or failure with details")]
        internal async Task<string> InstallBlock(string blockId, string routineId = null, string variants = null)
        {
            if (string.IsNullOrEmpty(blockId))
            {
                return "Error: blockId is required. Use ListBlocks() or SearchBlocks() to find block IDs.";
            }

            var blockData = Utils.GetBlockData(blockId);
            if (blockData == null)
            {
                return $"Error: No block found with ID '{blockId}'. Use ListBlocks() to see available blocks.";
            }

            if (!blockData.IsInstallable)
            {
                var reasons = new StringBuilder();
                reasons.Append($"Block '{blockData.BlockName}' cannot be installed. Reasons: ");

                if (blockData.IsSingletonAndAlreadyPresent)
                    reasons.Append("Already installed (singleton). ");
                if (blockData.HasMissingDependencies)
                    reasons.Append("Missing block dependencies. ");
                if (blockData.HasMissingPackageDependencies)
                    reasons.Append($"Missing packages: {string.Join(", ", blockData.GetMissingPackageDependencies)}. ");

                return reasons.ToString();
            }

            // Handle InterfaceBlockData blocks with installation routines
            if (blockData is InterfaceBlockData interfaceBlock)
            {
                var availableRoutines = interfaceBlock.GetAvailableInstallationRoutines().ToList();

                if (availableRoutines.Count == 0)
                {
                    return $"Error: Block '{blockData.BlockName}' has no available installation routines.";
                }

                InstallationRoutine selectedRoutine = null;

                // If no routineId provided
                if (string.IsNullOrEmpty(routineId))
                {
                    if (availableRoutines.Count > 1)
                    {
                        // Multiple routines available - user must choose
                        return $"Error: Block '{blockData.BlockName}' is an interface block with multiple installation routines.\n" +
                               $"You must select an installation routine before installing.\n\n" +
                               $"Call GetBlockInstallationOptions(\"{blockId}\") to see available routines and their\n" +
                               $"configurable parameters, then call InstallBlock(\"{blockId}\", routineId: \"chosen-id\")\n" +
                               $"with the selected routine.";
                    }
                    // Auto-select the single routine
                    selectedRoutine = availableRoutines[0];
                }
                else
                {
                    // Find routine by ID or display name
                    selectedRoutine = availableRoutines.FirstOrDefault(r =>
                        r.Id == routineId || r.DisplayName.Equals(routineId, StringComparison.OrdinalIgnoreCase));

                    if (selectedRoutine == null)
                    {
                        var availableIds = string.Join(", ", availableRoutines.Select(r => $"{r.DisplayName} ({r.Id})"));
                        return $"Error: Routine '{routineId}' not found for block '{blockData.BlockName}'.\n" +
                               $"Available routines: {availableIds}";
                    }
                }

                // Pre-configure the VariantsSelection to bypass the popup
                InterfaceBlockData.Selection.SetupForSelection(interfaceBlock);
                InterfaceBlockData.Selection.FavouriteRoutine = selectedRoutine;

                // Parse and apply variants if provided
                if (!string.IsNullOrEmpty(variants))
                {
                    try
                    {
                        var variantsJson = JsonObject.Parse(variants);
                        foreach (var variant in InterfaceBlockData.Selection)
                        {
                            if (variantsJson.TryGetValue(variant.MemberInfo.Name, out var value))
                            {
                                var valueStr = value.ToString();
                                variant.FromJson(valueStr);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        return $"Error parsing variants JSON: {ex.Message}\n" +
                               $"Expected format: {{\"paramName\": \"value\", ...}}";
                    }
                }

                // Update variants to refresh dependencies and missing package checks
                InterfaceBlockData.Selection.UpdateVariants();

                // Validate that all requirements are met before bypassing the popup
                if (InterfaceBlockData.Selection.HasMissingDependencies)
                {
                    var missingPackages = string.Join(", ", InterfaceBlockData.Selection.MissingDependencies);
                    return $"Error: Cannot install '{blockData.BlockName}' with routine '{selectedRoutine.DisplayName}'.\n" +
                           $"Missing required packages: {missingPackages}\n\n" +
                           $"Please install these packages first:\n" +
                           $"Window > Package Manager > Add package by name\n" +
                           $"Then retry the installation.";
                }

                // Check for required parameter variants that need values
                var requiredParams = InterfaceBlockData.Selection
                    .Where(v => v.Attribute.Behavior == VariantAttribute.VariantBehavior.Parameter)
                    .ToList();

                if (requiredParams.Any() && string.IsNullOrEmpty(variants))
                {
                    var paramInfo = new StringBuilder();
                    paramInfo.AppendLine($"Error: Routine '{selectedRoutine.DisplayName}' requires parameter configuration.\n");
                    paramInfo.AppendLine("Required parameters:");
                    foreach (var param in requiredParams)
                    {
                        paramInfo.AppendLine($"  - {param.MemberInfo.Name}: {param.RawValue?.GetType().Name ?? "unknown"} (default: {param.RawValue})");
                        if (!string.IsNullOrEmpty(param.Attribute.Description))
                        {
                            paramInfo.AppendLine($"    {param.Attribute.Description}");
                        }
                    }
                    paramInfo.AppendLine();
                    paramInfo.AppendLine($"Call GetBlockInstallationOptions(\"{blockId}\") to see all parameters,");
                    paramInfo.AppendLine($"then call InstallBlock with variants JSON to configure them.");
                    return paramInfo.ToString();
                }

                // All requirements met - mark selection as completed to bypass the popup
                InterfaceBlockData.Selection.Completed = true;
                InterfaceBlockData.Selection.Canceled = false;
            }

            try
            {
                // Enter silent mode to suppress UI operations (like Package Manager window) during MCP installation
                using (OVRSilentMode.Enter())
                {
                    await blockData.AddToProject();
                }
                return $"Successfully installed '{blockData.BlockName}' into the scene.";
            }
            catch (InstallationCancelledException ex)
            {
                return $"Installation cancelled: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Installation failed: {ex.Message}";
            }
        }

        [Tool(Description = "Get all configurable properties on an installed Quest/XR feature's components. Shows property names, types, and current values. Use this after installation to discover what settings can be customized (e.g. tracking modes, thresholds, visual options).",
            Returns = "List of configurable properties grouped by component, with current values and types")]
        internal string GetConfigurableProperties(string blockId)
        {
            if (string.IsNullOrEmpty(blockId))
            {
                return "Error: blockId is required. Use ListInstalledBlocks() to find installed block IDs.";
            }

            var blocksInScene = Utils.GetBlocksInScene()
                .Where(b => b.BlockId == blockId)
                .ToList();

            if (blocksInScene.Count == 0)
            {
                return $"Error: No installed block found with ID '{blockId}'. Use ListInstalledBlocks() to see installed blocks, or InstallBlock() to install it first.";
            }

            var sb = new StringBuilder();
            var block = blocksInScene[0];
            var blockData = block.GetBlockData();
            var name = blockData != null ? blockData.BlockName.Value : "Unknown";

            sb.AppendLine($"=== CONFIGURABLE PROPERTIES: {name} ===");
            sb.AppendLine($"GameObject: {block.gameObject.name}");
            sb.AppendLine();

            // Show modifiable properties from remote content (curated highlights)
            var modProps = BlocksContentManager.GetBlockModifiablePropertyById(blockId);
            if (modProps is { Length: > 0 })
            {
                sb.AppendLine("--- Recommended customizations ---");
                foreach (var mp in modProps)
                {
                    sb.AppendLine($"  {mp.name}: {mp.description}");
                    sb.AppendLine($"    (highlight: {mp.highlightIdentifier})");
                }
                sb.AppendLine();
            }

            // Walk all components on the block's GameObject hierarchy
            var components = block.gameObject.GetComponentsInChildren<Component>(true);
            foreach (var comp in components)
            {
                if (comp == null) continue;
                if (comp is Transform) continue;
                if (comp is BuildingBlock) continue;

                var so = new SerializedObject(comp);
                var prop = so.GetIterator();
                var hasProps = false;

                while (prop.NextVisible(true))
                {
                    // Skip internal Unity properties
                    if (prop.name == "m_Script" || prop.name == "m_ObjectHideFlags") continue;
                    if (prop.propertyPath.StartsWith("m_")) continue;
                    if (prop.depth > 1) continue; // Only top-level properties

                    if (!hasProps)
                    {
                        sb.AppendLine($"--- {comp.GetType().Name} (on {comp.gameObject.name}) ---");
                        hasProps = true;
                    }

                    var valueStr = GetSerializedPropertyValueString(prop);
                    sb.AppendLine($"  {prop.name} ({prop.propertyType}): {valueStr}");
                }
            }

            if (sb.Length < 100)
            {
                sb.AppendLine("No configurable properties found on this block's components.");
            }

            return sb.ToString();
        }

        [Tool(Description = "Set a property value on an installed Quest/XR feature's component. Use GetConfigurableProperties() first to discover available properties, component types, and current values.",
            Returns = "Result of the property change - success with old and new values, or error details")]
        internal string SetBlockProperty(string blockId, string componentType, string propertyName, string value)
        {
            if (string.IsNullOrEmpty(blockId) || string.IsNullOrEmpty(componentType) ||
                string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(value))
            {
                return "Error: All parameters are required - blockId, componentType, propertyName, value. Use GetConfigurableProperties() first to discover available properties.";
            }

            var blocksInScene = Utils.GetBlocksInScene()
                .Where(b => b.BlockId == blockId)
                .ToList();

            if (blocksInScene.Count == 0)
            {
                return $"Error: No installed block found with ID '{blockId}'.";
            }

            var block = blocksInScene[0];
            var components = block.gameObject.GetComponentsInChildren<Component>(true);
            var targetComp = components.FirstOrDefault(c =>
                c != null && c.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase));

            if (targetComp == null)
            {
                var available = string.Join(", ", components
                    .Where(c => c != null && !(c is Transform) && !(c is BuildingBlock))
                    .Select(c => c.GetType().Name)
                    .Distinct());
                return $"Error: Component '{componentType}' not found on block. Available: {available}";
            }

            var so = new SerializedObject(targetComp);
            var prop = so.FindProperty(propertyName);

            if (prop == null)
            {
                return $"Error: Property '{propertyName}' not found on {componentType}. Use GetConfigurableProperties() to see available properties.";
            }

            var oldValue = GetSerializedPropertyValueString(prop);

            try
            {
                Undo.RecordObject(targetComp, $"Set {propertyName} on {componentType}");
                SetSerializedPropertyValue(prop, value);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(targetComp);

                return $"Set {componentType}.{propertyName}: {oldValue} → {value}";
            }
            catch (Exception ex)
            {
                return $"Error setting property: {ex.Message}";
            }
        }

        private static string GetSerializedPropertyValueString(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue.ToString();
                case SerializedPropertyType.Boolean: return prop.boolValue.ToString();
                case SerializedPropertyType.Float: return prop.floatValue.ToString("F4");
                case SerializedPropertyType.String: return $"\"{prop.stringValue}\"";
                case SerializedPropertyType.Enum:
                    return prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0
                    ? prop.enumDisplayNames[prop.enumValueIndex]
                    : prop.enumValueIndex.ToString();
                case SerializedPropertyType.Color: return prop.colorValue.ToString();
                case SerializedPropertyType.Vector2: return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return prop.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return prop.vector4Value.ToString();
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "None";
                case SerializedPropertyType.LayerMask: return LayerMask.LayerToName(prop.intValue);
                default: return $"({prop.propertyType})";
            }
        }

        private static void SetSerializedPropertyValue(SerializedProperty prop, string value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = int.Parse(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = bool.Parse(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = float.Parse(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value;
                    break;
                case SerializedPropertyType.Enum:
                    // Try by name first, then by index
                    var idx = Array.IndexOf(prop.enumDisplayNames, value);
                    if (idx >= 0)
                        prop.enumValueIndex = idx;
                    else if (int.TryParse(value, out var enumIdx))
                        prop.enumValueIndex = enumIdx;
                    else
                        throw new ArgumentException($"Invalid enum value '{value}'. Valid values: {string.Join(", ", prop.enumDisplayNames)}");
                    break;
                case SerializedPropertyType.Color:
                    if (ColorUtility.TryParseHtmlString(value, out var color))
                        prop.colorValue = color;
                    else
                        throw new ArgumentException($"Invalid color '{value}'. Use hex format like #FF0000.");
                    break;
                default:
                    throw new NotSupportedException($"Setting {prop.propertyType} properties is not yet supported. Please modify this value in the Unity Inspector.");
            }
        }
    }
}
