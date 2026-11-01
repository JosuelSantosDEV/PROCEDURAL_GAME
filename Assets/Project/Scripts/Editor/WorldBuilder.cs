using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Adiciona o gerador de mundo procedural à cena aberta: cria o objeto "World", o material do terreno
    /// (se ainda não existir) e desativa o Terrain antigo, que ficaria sobreposto aos chunks.
    /// Se já houver um WorldGenerator na cena, apenas o seleciona (para não apagar seus ajustes).
    /// </summary>
    public static class WorldBuilder
    {
        private const string UndoName = "Create Procedural World";
        private const string MaterialFolder = "Assets/Project/Materials";
        private const string MaterialPath = MaterialFolder + "/ProceduralTerrain.mat";

        [MenuItem("PROCEDURAL_GAME/Create Procedural World")]
        public static void Build()
        {
            WorldGenerator existing = Object.FindObjectOfType<WorldGenerator>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("Já existe um WorldGenerator na cena; nada foi alterado. Ajuste os parâmetros no Inspector.");
                return;
            }

            Material material = GetOrCreateMaterial();
            if (material == null) return;

            GameObject world = new GameObject("World", typeof(WorldGenerator));
            Undo.RegisterCreatedObjectUndo(world, UndoName);

            SerializedObject generator = new SerializedObject(world.GetComponent<WorldGenerator>());
            generator.FindProperty("terrainMaterial").objectReferenceValue = material;
            generator.ApplyModifiedPropertiesWithoutUndo();

            // O Terrain de teste (50x50) ficaria no meio dos chunks. Só desativamos; apague quando tiver certeza.
            foreach (Terrain terrain in Object.FindObjectsOfType<Terrain>())
            {
                Undo.RecordObject(terrain.gameObject, UndoName);
                terrain.gameObject.SetActive(false);
                Debug.Log($"Terrain '{terrain.name}' desativado (continua na cena, só desligado).");
            }

            EditorSceneManager.MarkSceneDirty(world.scene);
            Selection.activeGameObject = world;
            Debug.Log("Mundo procedural criado. Salve a cena (Ctrl+S) e dê Play.");
        }

        private static Material GetOrCreateMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("Shader 'Universal Render Pipeline/Lit' não encontrado. O projeto está com o URP instalado?");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Project", "Materials");

            material = new Material(shader) { name = "ProceduralTerrain" };
            material.SetColor("_BaseColor", new Color(0.36f, 0.55f, 0.26f));
            // Sem brilho: terreno de grama não reflete a luz como plástico.
            material.SetFloat("_Smoothness", 0f);

            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
