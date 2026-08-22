using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Server;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blockiverse.Editor
{
    // Generates the dedicated server scene.
    //
    // Deliberately NOT part of Run(): that calls ConfigureAndroidPlayer, which switches the active
    // build target to Android. A server build that ran it would churn the Library, invalidate the
    // build cache, and leave the editor on the wrong target.
    //
    // The scene is the world and network stack with nothing presentational: no XR rig, no camera,
    // no event system, no renderer, no lighting. Blockiverse.Gameplay is excluded from the server
    // platform, so anything from it in this scene would be a missing script in the built player.
    public static partial class BlockiverseProjectBootstrapper
    {
        public const string ServerSceneRootName = "Blockiverse Server";

        [MenuItem("Blockiverse/Bootstrap Dedicated Server Scene")]
        public static void RunServer()
        {
            EnsureFolders();
            ConfigureEditorSerialization();
            EnsureServerScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void EnsureServerScene()
        {
            bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(BlockiverseProject.ServerScenePath) != null;
            Scene scene = sceneExists
                ? EditorSceneManager.OpenScene(BlockiverseProject.ServerScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // The SAME network manager and player prefabs the client uses. Netcode identifies the
            // player prefab by GlobalObjectIdHash and matches NetworkBehaviour ordering, so a
            // server-specific variant would not spawn against a real client.
            GameObject playerPrefab = EnsureNetworkPlayerPrefab();
            GameObject networkManagerPrefab = EnsureNetworkManagerPrefab(playerPrefab);
            GameObject managerObject = FindRootGameObject(scene, NetworkManagerRootName);

            if (managerObject == null)
                managerObject = (GameObject)PrefabUtility.InstantiatePrefab(networkManagerPrefab, scene);

            ConfigureNetworkManagerObject(managerObject, playerPrefab);
            StripClientOnlyComponents(managerObject);

            EnsureServerWorldRoot(scene);
            EnsureServerBootstrap(scene, managerObject);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BlockiverseProject.ServerScenePath);
        }

        // The network manager prefab is SHARED with the client, and it carries components from
        // assemblies the server build excludes. Left in place they deserialize as "The referenced
        // script on this Behaviour is missing!" on every single server start -- an error the
        // operator cannot act on, emitted before logging is even configured, in a process whose
        // whole contract is that silence means healthy.
        //
        // SurvivalVitalsRuntime is the one that applies: it lives in Blockiverse.Gameplay, and a
        // dedicated server has no local player whose vitals it could simulate anyway. Anything else
        // added to that prefab from an excluded assembly belongs in this list too.
        static void StripClientOnlyComponents(GameObject managerObject)
        {
            if (managerObject == null)
                return;

            SurvivalVitalsRuntime vitals = managerObject.GetComponent<SurvivalVitalsRuntime>();
            if (vitals != null)
                UnityEngine.Object.DestroyImmediate(vitals, allowDestroyingAssets: false);
        }

        // The world root, minus presentation. CreativeWorldManager and WorldTimeClock only: the
        // manager resolves no IWorldPresentation here and takes its headless path.
        static void EnsureServerWorldRoot(Scene scene)
        {
            GameObject worldObject = FindRootGameObject(scene, BlockiverseProject.CreativeWorldRootName);

            if (worldObject == null)
            {
                worldObject = new GameObject(BlockiverseProject.CreativeWorldRootName);
                SceneManager.MoveGameObjectToScene(worldObject, scene);
            }

            worldObject.transform.position = Vector3.zero;
            worldObject.transform.rotation = Quaternion.identity;
            worldObject.transform.localScale = Vector3.one;

            // The clock lives on the world root rather than on a sun object: the server has no
            // lighting, and the simulation's tick source must not depend on a presentation object.
            EnsureComponent<WorldTimeClock>(worldObject);

            CreativeWorldManager manager = EnsureComponent<CreativeWorldManager>(worldObject);
            manager.InitializeDefaultWorldOnAwake = false;

            EditorUtility.SetDirty(worldObject);
            EditorUtility.SetDirty(manager);
        }

        static void EnsureServerBootstrap(Scene scene, GameObject managerObject)
        {
            GameObject serverObject = FindRootGameObject(scene, ServerSceneRootName);

            if (serverObject == null)
            {
                serverObject = new GameObject(ServerSceneRootName);
                SceneManager.MoveGameObjectToScene(serverObject, scene);
            }

            EnsureComponent<BlockiverseDedicatedServerBootstrap>(serverObject);
            EditorUtility.SetDirty(serverObject);

            if (managerObject != null)
                EditorUtility.SetDirty(managerObject);
        }
    }
}
