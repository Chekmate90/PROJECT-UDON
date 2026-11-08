using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class RepairOATTextureLinks
{
    [Serializable]
    private class TextureManifest
    {
        public MaterialEntry[] materials;
    }

    [Serializable]
    private class MaterialEntry
    {
        public string name;
        public string @base;
        public string normal;
    }

    public static void Run()
    {
        const string textureFolder = "Assets/OAT_ExtractedTextures";
        var manifest = JsonUtility.FromJson<TextureManifest>(
            File.ReadAllText(textureFolder + "/manifest.json"));
        int assigned = 0;

        foreach (var entry in manifest.materials)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/" + entry.name + ".mat");
            if (material == null)
            {
                Debug.LogWarning("No matching material asset for " + entry.name);
                continue;
            }

            if (!string.IsNullOrEmpty(entry.@base))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    textureFolder + "/" + entry.@base);
                if (texture != null)
                {
                    if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                    if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                }
                else Debug.LogWarning("Missing base texture " + entry.@base);
            }

            if (!string.IsNullOrEmpty(entry.normal))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    textureFolder + "/" + entry.normal);
                if (texture != null)
                {
                    if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", texture);
                    material.EnableKeyword("_NORMALMAP");
                }
                else Debug.LogWarning("Missing normal texture " + entry.normal);
            }

            EditorUtility.SetDirty(material);
            assigned++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("OAT texture repair assigned textures to " + assigned + " materials.");
    }
}
