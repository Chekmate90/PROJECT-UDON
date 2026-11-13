using UnityEditor;
using UnityEngine;

public static class NetworkPlayerArmIKSetup
{
    private const string PlayerPrefabPath = "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";
    private const string ModelPath = "Assets/Midans mdol.fbx";

    [MenuItem("Tools/Project Udon/Set Up Player Model and Arm IK")]
    public static void SetUp()
    {
        ApplyToNetworkPlayerPrefab();
    }

    public static void ApplyToNetworkPlayerPrefab()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
            throw new System.InvalidOperationException("Could not find " + ModelPath);

        GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform model = player.transform.Find("Midans mdol");
            if (model == null)
            {
                foreach (Transform child in player.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.Contains("Midans"))
                    {
                        model = child;
                        break;
                    }
                }
            }

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

            // 1. Reparent Left Hand & Right Hand back to player root and restore full scale
            Transform leftHandTarget = FindRecursive(player.transform, "Left Hand");
            if (leftHandTarget != null)
            {
                leftHandTarget.SetParent(player.transform, false);
                leftHandTarget.localPosition = new Vector3(-0.2f, 1.2f, 0.3f);
                leftHandTarget.localRotation = Quaternion.identity;
                leftHandTarget.localScale = Vector3.one;
            }

            Transform rightHandTarget = FindRecursive(player.transform, "Right Hand");
            if (rightHandTarget != null)
            {
                rightHandTarget.SetParent(player.transform, false);
                rightHandTarget.localPosition = new Vector3(0.2f, 1.2f, 0.3f);
                rightHandTarget.localRotation = Quaternion.identity;
                rightHandTarget.localScale = Vector3.one;
            }

            // 2. Hide and deactivate all purple template hand meshes
            string[] templateHandNames = new string[] {
                "Hand_Left_Networked", "Hand_Right_Networked",
                "Left Hand Networked", "Right Hand Networked",
                "LeftHand", "RightHand",
                "L_Wrist", "R_Wrist", "L_Wrist - Quest", "R_Wrist - Quest",
                "Quest", "AXR"
            };

            foreach (string tName in templateHandNames)
            {
                foreach (Transform child in player.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.Equals(tName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (child.name.StartsWith("mixamorig") || child.name.Contains("Midans")) continue;
                        foreach (Renderer r in child.GetComponentsInChildren<Renderer>(true))
                        {
                            r.enabled = false;
                        }
                        child.gameObject.SetActive(false);
                    }
                }
            }

            Transform capsule = player.transform.Find("Capsule Player Body");
            Renderer capsuleRenderer = capsule != null ? capsule.GetComponent<Renderer>() : null;
            if (capsuleRenderer != null) capsuleRenderer.enabled = false;

            // 3. Configure NetworkPlayerArmIK
            NetworkPlayerArmIK ik = player.GetComponent<NetworkPlayerArmIK>();
            if (ik == null) ik = player.AddComponent<NetworkPlayerArmIK>();
            SerializedObject serializedIK = new SerializedObject(ik);
            AssignRequired(serializedIK, "leftUpperArm", FindBone(model, "mixamorig:LeftArm"));
            AssignRequired(serializedIK, "leftForearm", FindBone(model, "mixamorig:LeftForeArm"));
            AssignRequired(serializedIK, "leftHand", FindBone(model, "mixamorig:LeftHand"));
            AssignRequired(serializedIK, "rightUpperArm", FindBone(model, "mixamorig:RightArm"));
            AssignRequired(serializedIK, "rightForearm", FindBone(model, "mixamorig:RightForeArm"));
            AssignRequired(serializedIK, "rightHand", FindBone(model, "mixamorig:RightHand"));
            AssignRequired(serializedIK, "leftTarget", leftHandTarget);
            AssignRequired(serializedIK, "rightTarget", rightHandTarget);
            serializedIK.ApplyModifiedPropertiesWithoutUndo();

            // 4. Set up Mixamo Animator & Locomotion Animation (Boxing Idle + Short Step Forward Walk)
            RuntimeAnimatorController animController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animations/PlayerBoxingAvatar.controller");
            if (animController != null && model != null)
            {
                Animator animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = animController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                PlayerLocomotionAnimation locomotion = player.GetComponent<PlayerLocomotionAnimation>();
                if (locomotion == null) locomotion = player.AddComponent<PlayerLocomotionAnimation>();
                SerializedObject serializedLocomotion = new SerializedObject(locomotion);
                SerializedProperty animProp = serializedLocomotion.FindProperty("animator");
                if (animProp != null)
                {
                    animProp.objectReferenceValue = animator;
                    serializedLocomotion.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // 5. Configure DesktopPlayerController for PC Host
            DesktopPlayerController desktopCtrl = player.GetComponent<DesktopPlayerController>();
            if (desktopCtrl == null) desktopCtrl = player.AddComponent<DesktopPlayerController>();
            SerializedObject serializedDesktop = new SerializedObject(desktopCtrl);
            SerializedProperty leftTargetProp = serializedDesktop.FindProperty("leftHandTarget");
            if (leftTargetProp != null) leftTargetProp.objectReferenceValue = leftHandTarget;
            SerializedProperty rightTargetProp = serializedDesktop.FindProperty("rightHandTarget");
            if (rightTargetProp != null) rightTargetProp.objectReferenceValue = rightHandTarget;
            SerializedProperty headTargetProp = serializedDesktop.FindProperty("headTarget");
            if (headTargetProp != null) headTargetProp.objectReferenceValue = FindRecursive(player.transform, "Head");
            serializedDesktop.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Network player model, animations, and two-bone arm IK setup successfully applied and saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(player);
        }
    }

    private static void AssignRequired(SerializedObject target, string propertyName, Object value)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property == null || value == null)
            throw new System.InvalidOperationException("Missing IK assignment: " + propertyName);
        property.objectReferenceValue = value;
    }

    private static Transform FindBone(Transform root, string boneName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == boneName) return child;
        return null;
    }

    private static Transform FindRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }
}
