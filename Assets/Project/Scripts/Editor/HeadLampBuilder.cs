using System.Collections.Generic;
using PROCEDURAL_GAME.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Capacete com lanterna na cabeça do Player (improviso): junta o capacete do pacote "Scooter helmet" e a lanterna do pacote "Flashlight"
    /// num prefab próprio (Assets/Project/Characters/Player/Gear/HeadLamp.prefab), com materiais URP Simple Lit e texturas reduzidas no
    /// celular, e liga o componente <see cref="HeadLamp"/> no Player da cena aberta. Dos pacotes só se usam as malhas e 4 texturas; os
    /// materiais, prefabs e cenas deles ficam de fora do jogo, mas os materiais deles (Standard, do pipeline antigo: rosa no URP) também
    /// são convertidos para Simple Lit, para os prefabs e as cenas de exemplo dos pacotes não aparecerem rosa no Editor.
    /// Menu PROCEDURAL_GAME > Player > Setup Head Lamp (também chamado pelo Setup Player Model). Pode rodar de novo.
    /// </summary>
    public static class HeadLampBuilder
    {
        private const string GearFolder = "Assets/Project/Characters/Player/Gear";
        private const string PrefabPath = GearFolder + "/HeadLamp.prefab";

        private const string HelmetMeshPath = "Assets/Scooter helmet/Mesh/scooter_helmet.fbx";
        private const string HelmetAlbedoPath = "Assets/Scooter helmet/Textures/Grey_no_name_bc.tga";
        private const string HelmetNormalPath = "Assets/Scooter helmet/Textures/livall_autosave_1_n.tga";

        private const string FlashlightMeshPath = "Assets/Flashlight/Misc/Flashlight.FBX";
        private const string FlashlightAlbedoPath = "Assets/Flashlight/Flashlight_Gold/Textures/FlashLight_DFopac.tga";
        private const string FlashlightNormalPath = "Assets/Flashlight/Flashlight_Gold/Textures/FlashLight_NM.png";
        private const string CookiePath = "Assets/Flashlight/Textures/Flashlight_Cookie.png";

        private static readonly string[] PackageFolders = { "Assets/Scooter helmet", "Assets/Flashlight" };

        // A lanterna do pacote tem 23 cm: reduzida para parecer uma lanterna de mão presa no capacete.
        private const float FlashlightScale = 0.8f;

        // Texturas: 1024 no PC, 512 + ASTC 6x6 no celular (objeto pequeno na tela; as originais têm 4096).
        private const int DesktopTextureSize = 1024;
        private const int MobileTextureSize = 512;

        [MenuItem("PROCEDURAL_GAME/Player/Setup Head Lamp")]
        public static void BuildMenu()
        {
            if (Build()) Debug.Log("Capacete com lanterna pronto. Salve a cena (Ctrl+S). Lanterna: botão Lanterna (rode Create Mobile Controls para criá-lo), F ou Y do gamepad.");
        }

        /// <summary>Cria o prefab (se os pacotes existirem) e liga o HeadLamp no Player da cena aberta. False se faltar algo.</summary>
        public static bool Build()
        {
            PlayerMovement movement = Object.FindObjectOfType<PlayerMovement>();
            if (movement == null)
            {
                Debug.LogError("Nenhum Player (PlayerMovement) na cena aberta. Abra Assets/Scenes/GameWorld.unity.");
                return false;
            }

            ConvertPackageMaterials();

            GameObject prefab = EnsurePrefab();
            if (prefab == null) return false;

            Texture cookie = EnsureCookie();

            HeadLamp lamp = movement.GetComponent<HeadLamp>();
            if (lamp == null) lamp = Undo.AddComponent<HeadLamp>(movement.gameObject);

            SerializedObject so = new SerializedObject(lamp);
            so.FindProperty("gearPrefab").objectReferenceValue = prefab;
            so.FindProperty("cookie").objectReferenceValue = cookie;

            // Encaixe inicial corrigido pelo primeiro teste (capacete alto e caído para trás). Só troca se ainda estiver no valor antigo (zero):
            // um ajuste já feito no Inspector fica.
            SerializedProperty offset = so.FindProperty("gearOffset");
            if (offset.vector3Value == Vector3.zero) offset.vector3Value = new Vector3(0f, -0.02f, 0.015f);
            Animator animator = movement.GetComponentInChildren<Animator>();
            if (animator != null) so.FindProperty("animator").objectReferenceValue = animator;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(movement.gameObject.scene);
            return true;
        }

        // Materiais originais dos pacotes (Standard) -> URP Simple Lit, com o mesmo conversor do menu Fix Pink Materials.
        private static void ConvertPackageMaterials()
        {
            List<string> folders = new List<string>();
            foreach (string folder in PackageFolders)
            {
                if (AssetDatabase.IsValidFolder(folder)) folders.Add(folder);
            }

            if (folders.Count == 0) return;

            int converted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", folders.ToArray()))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && MaterialUpgrader.ConvertIfNeeded(material)) converted++;
            }

            if (converted > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"{converted} material(is) dos pacotes do capacete e da lanterna convertido(s) para URP Simple Lit (não ficam mais rosa).");
            }
        }

        private static GameObject EnsurePrefab()
        {
            Mesh helmetMesh = LoadMesh(HelmetMeshPath);
            Mesh flashlightMesh = LoadMesh(FlashlightMeshPath);
            if (helmetMesh == null || flashlightMesh == null)
            {
                Debug.LogWarning($"Capacete ou lanterna não encontrados ('{HelmetMeshPath}', '{FlashlightMeshPath}'): importe os pacotes \"Scooter helmet\" e \"Flashlight\".");
                return null;
            }

            EnsureFolder(GearFolder);

            Material helmetMaterial = EnsureMaterial("Helmet", HelmetAlbedoPath, HelmetNormalPath, Color.white, 0.3f, false);
            Material bodyMaterial = EnsureMaterial("Flashlight", FlashlightAlbedoPath, FlashlightNormalPath, Color.white, 0.45f, false);
            Material glowMaterial = EnsureMaterial("FlashlightGlow", null, null, new Color(0.85f, 0.85f, 0.8f), 0.8f, true);

            GameObject root = new GameObject("HeadLamp");
            try
            {
                // Capacete na origem (a origem do modelo fica na borda de baixo, centrada).
                GameObject helmet = CreatePart(root.transform, "Helmet", helmetMesh, new[] { helmetMaterial });
                Bounds helmetBounds = helmetMesh.bounds;

                // Lanterna deitada em cima do capacete, com a lente para a frente (+z) e passando um pouco da aba da frente. Fica num grupo "Lamp"
                // com origem no centro dela: o HeadLamp inclina esse grupo (ajuste da inclinação da lanterna) sem mexer no capacete.
                Bounds flashlightBounds = flashlightMesh.bounds;
                Vector3 axis = FlashlightAxis(flashlightMesh);
                Quaternion toForward = Quaternion.FromToRotation(axis, Vector3.forward);

                Vector3 rotatedExtents = Abs(toForward * flashlightBounds.extents) * FlashlightScale;
                float length = rotatedExtents.z * 2f;
                float radius = Mathf.Max(rotatedExtents.x, rotatedExtents.y);

                Vector3 center = new Vector3(helmetBounds.center.x, helmetBounds.max.y + radius * 0.55f, helmetBounds.max.z - length * 0.45f);
                GameObject lamp = new GameObject("Lamp");
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.localPosition = center;

                Material[] flashlightMaterials = new Material[Mathf.Max(1, flashlightMesh.subMeshCount)];
                for (int i = 0; i < flashlightMaterials.Length; i++) flashlightMaterials[i] = i == 0 ? bodyMaterial : glowMaterial;
                GameObject flashlight = CreatePart(lamp.transform, "Flashlight", flashlightMesh, flashlightMaterials);
                flashlight.transform.localScale = Vector3.one * FlashlightScale;
                flashlight.transform.localRotation = toForward;
                flashlight.transform.localPosition = -(toForward * (flashlightBounds.center * FlashlightScale));

                // Ponto da lente (origem da luz), olhando para a frente.
                GameObject lensPoint = new GameObject("LensPoint");
                lensPoint.transform.SetParent(lamp.transform, false);
                lensPoint.transform.localPosition = Vector3.forward * (length * 0.5f + 0.01f);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"Capacete: {helmetBounds.size.x:0.00} x {helmetBounds.size.y:0.00} x {helmetBounds.size.z:0.00} m; lanterna {length:0.00} m em cima. Prefab: {PrefabPath}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreatePart(Transform parent, string name, Mesh mesh, Material[] materials)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return part;
        }

        private static Material EnsureMaterial(string name, string albedoPath, string normalPath, Color color, float smoothness, bool emissive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            string path = $"{GearFolder}/{name}.mat";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetColor("_SpecColor", new Color(0.12f, 0.12f, 0.12f, 1f));
            material.EnableKeyword("_SPECULAR_COLOR");

            Texture2D albedo = albedoPath != null ? PrepareTexture(albedoPath, false) : null;
            material.SetTexture("_BaseMap", albedo);

            Texture2D normal = normalPath != null ? PrepareTexture(normalPath, true) : null;
            material.SetTexture("_BumpMap", normal);
            if (normal != null) material.EnableKeyword("_NORMALMAP");
            else material.DisableKeyword("_NORMALMAP");

            // Lente: brilho próprio quando a lanterna liga (o HeadLamp troca a cor por renderizador; aqui fica apagada).
            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // 1024 no PC; 512 + ASTC 6x6 no Android/iOS. Só reimporta se mudou algo.
        private static Texture2D PrepareTexture(string path, bool normalMap)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"Textura '{path}' não encontrada.");
                return null;
            }

            bool changed = false;
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type) { importer.textureType = type; changed = true; }
            if (importer.maxTextureSize != DesktopTextureSize) { importer.maxTextureSize = DesktopTextureSize; changed = true; }

            foreach (string platform in new[] { "Android", "iPhone" })
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                if (settings.overridden && settings.maxTextureSize == MobileTextureSize && settings.format == TextureImporterFormat.ASTC_6x6) continue;

                settings.overridden = true;
                settings.maxTextureSize = MobileTextureSize;
                settings.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(settings);
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Máscara da luz (cookie) para spot: pequena, sem repetição nas bordas.
        private static Texture EnsureCookie()
        {
            TextureImporter importer = AssetImporter.GetAtPath(CookiePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"Máscara da lanterna '{CookiePath}' não encontrada: a luz fica sem desenho.");
                return null;
            }

            // O URP aceita textura comum como máscara de spot (cor RGB); só não pode repetir nas bordas.
            bool changed = importer.textureType != TextureImporterType.Default || importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != 256;
            if (changed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture>(CookiePath);
        }

        private static Mesh LoadMesh(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Mesh mesh) return mesh;
            }

            return null;
        }

        /// <summary>
        /// Eixo da lanterna (direção da lente) no espaço da malha: o lado mais comprido dos limites; o sentido é o da lente (2ª parte da
        /// malha), medido pelos vértices dela quando a malha é legível, senão pelos limites dela. Sem como saber, vale o sentido positivo.
        /// (A 1ª versão usava só os limites da lente: vinham vazios, o eixo saía errado e a lanterna ficava em pé.)
        /// </summary>
        private static Vector3 FlashlightAxis(Mesh mesh)
        {
            Vector3 extents = mesh.bounds.extents;
            Vector3 axis = extents.x >= extents.y && extents.x >= extents.z ? Vector3.right : extents.y >= extents.z ? Vector3.up : Vector3.forward;
            if (mesh.subMeshCount < 2) return axis;

            Vector3 lens = Vector3.zero;
            bool found = false;
            if (mesh.isReadable)
            {
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.GetIndices(1);
                if (indices.Length > 0)
                {
                    foreach (int index in indices) lens += vertices[index];
                    lens /= indices.Length;
                    found = true;
                }
            }

            if (!found)
            {
                Bounds lensBounds = mesh.GetSubMesh(1).bounds;
                found = lensBounds.extents.sqrMagnitude > 0f;
                lens = lensBounds.center;
            }

            float side = found ? Vector3.Dot(lens - mesh.bounds.center, axis) : 0f;
            Debug.Log($"Lanterna: eixo {(side < 0f ? -axis : axis)} na malha (lente {(found ? "medida" : "não medida, sentido positivo")}).");
            return side < 0f ? -axis : axis;
        }

        private static Vector3 DominantAxis(Vector3 direction)
        {
            Vector3 a = Abs(direction);
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(direction.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(direction.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(direction.z));
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }
    }
}
