using UnityEditor;
using UnityEngine;

public static class NetworkPlayerArmIKSetup
{
    private const string PlayerPrefabPath = "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";
    private const string ModelPath = "Assets/Midans mdol.fbx";

    [MenuItem("Tools/Project Udon/Set Up Player Model and Arm IK")]
    private static void SetUp()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
        {
            EditorUtility.DisplayDialog("Player model setup", "Could not find Assets/Midans mdol.fbx.", "OK");
            return;
        }

        GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform model = player.transform.Find("Midans mdol");
            if (model == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, player.transform);
                instance.name = "Midans mdol";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                model = instance.transform;

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    if (bounds.size.y > 0.001f)
                    {
                        float scale = 1.8f / bounds.size.y;
                        instance.transform.localScale = Vector3.one * scale;
                        instance.transform.localPosition = new Vector3(0f, -bounds.min.y * scale, 0f);
                    }
                }
            }

            Transform capsule = player.transform.Find("Capsule Player Body");
            Renderer capsuleRenderer = capsule != null ? capsule.GetComponent<Renderer>() : null;
            if (capsuleRenderer != null) capsuleRenderer.enabled = false;

            NetworkPlayerArmIK ik = player.GetComponent<NetworkPlayerArmIK>();
            if (ik == null) ik = player.AddComponent<NetworkPlayerArmIK>();
            SerializedObject serializedIK = new SerializedObject(ik);
            Assign(serializedIK, "leftUpperArm", FindBone(model, "mixamorig:LeftArm"));
            Assign(serializedIK, "leftForearm", FindBone(model, "mixamorig:LeftForeArm"));
            Assign(serializedIK, "leftHand", FindBone(model, "mixamorig:LeftHand"));
            Assign(serializedIK, "rightUpperArm", FindBone(model, "mixamorig:RightArm"));
            Assign(serializedIK, "rightForearm", FindBone(model, "mixamorig:RightForeArm"));
            Assign(serializedIK, "rightHand", FindBone(model, "mixamorig:RightHand"));
            Assign(serializedIK, "leftTarget", player.transform.Find("Left Hand"));
            Assign(serializedIK, "rightTarget", player.transform.Find("Right Hand"));
            serializedIK.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Player model setup", "The rigged model is now on the network player, with arm IK targeting the replicated left and right VR hand poses.", "OK");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(player);
        }
    }

    private static void Assign(SerializedObject target, string propertyName, Object value)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property != null) property.objectReferenceValue = value;
    }

    private static Transform FindBone(Transform root, string boneName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == boneName) return child;
        return null;
    }
}
