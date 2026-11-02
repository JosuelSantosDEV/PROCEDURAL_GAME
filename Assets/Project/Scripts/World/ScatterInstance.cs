using System.Collections.Generic;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Um objeto reciclável (árvore, pedra...) criado a partir de um prefab. Na criação o prefab é "limpo":
    /// colisores originais saem (malhas de colisão pesam), as sombras seguem o nível escolhido no menu e,
    /// se o perfil pedir, entra um colisor simples no lugar.
    /// </summary>
    public sealed class ScatterInstance
    {
        private readonly GameObject gameObject;
        private readonly Transform transform;
        private readonly Vector3 baseScale;
        private readonly LODGroup lodGroup;
        private readonly Collider collider;
        private readonly Renderer[] renderers;

        // Só os renderers do LOD0 (o mais detalhado) projetam sombra; as versões simplificadas, usadas de mais longe, nunca.
        private readonly bool[] canCastShadow;

        // Só no colisor de tronco: raio base (espaço local) e a cápsula, para aplicar o multiplicador do grupo em Place.
        private CapsuleCollider trunkCapsule;
        private float trunkRadiusLocal;

        public ScatterProfile Profile { get; }
        public GameObject Prefab { get; }

        /// <param name="staging">Objeto DESATIVADO onde o prefab nasce. Sob um pai inativo o Unity não ativa os componentes,
        /// então os colisores originais (convexos, caros de calcular) nunca chegam a ser criados antes de os removermos.</param>
        /// <param name="parent">Onde a instância fica depois de pronta.</param>
        public ScatterInstance(ScatterProfile profile, GameObject prefab, Transform staging, Transform parent, int shadowLevel)
        {
            Profile = profile;
            Prefab = prefab;

            gameObject = Object.Instantiate(prefab, staging);
            gameObject.SetActive(false);
            transform = gameObject.transform;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            baseScale = transform.localScale;

            // true: o objeto está inativo e, por padrão, o Unity não procuraria nele.
            lodGroup = gameObject.GetComponentInChildren<LODGroup>(true);

            // DestroyImmediate de propósito: Destroy só age no fim do frame, e a instância é ativada no mesmo frame
            // em que nasce, então o colisor convexo original seria calculado antes de sumir. É seguro aqui porque
            // mexemos numa cópia inativa nossa, nunca no asset do prefab.
            foreach (Collider original in gameObject.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(original);

            renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            canCastShadow = FindShadowCasters(renderers, lodGroup);
            SetShadowLevel(shadowLevel);

            collider = AddCollider(profile);
            if (collider != null) collider.enabled = false;

            // Já inativo e sem os colisores originais: agora pode sair do pai inativo.
            transform.SetParent(parent, false);
        }

        /// <param name="colliderScale">Multiplicador do raio do colisor de tronco (1 = padrão do perfil).</param>
        public void Place(Vector3 position, Quaternion rotation, float scale, float colliderScale = 1f)
        {
            if (trunkCapsule != null)
            {
                float radius = trunkRadiusLocal * colliderScale;
                if (!Mathf.Approximately(trunkCapsule.radius, radius)) trunkCapsule.radius = radius;
            }

            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = baseScale * scale;

            // O LODGroup guarda o tamanho do objeto de quando foi criado; sem recalcular, a troca de LOD
            // e o descarte por distância usariam o tamanho errado depois de mudar a escala.
            if (lodGroup != null) lodGroup.RecalculateBounds();

            if (collider != null) collider.enabled = false;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Projeta sombra só a partir do nível definido no perfil (0 = nunca); recebe sempre que há sombras. Com sombras
        /// desligadas nada projeta nem recebe, como antes.
        /// </summary>
        public void SetShadowLevel(int level)
        {
            int castFrom = Profile.ShadowFromLevel;
            bool cast = castFrom > 0 && level >= castFrom;
            UnityEngine.Rendering.ShadowCastingMode mode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;

            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = canCastShadow[i] ? mode : UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = level > 0;
            }
        }

        /// <summary>Com mais de um LOD, só o primeiro projeta sombra; sem LODGroup, todos os renderers podem.</summary>
        private static bool[] FindShadowCasters(Renderer[] all, LODGroup group)
        {
            bool[] result = new bool[all.Length];
            LOD[] lods = group != null ? group.GetLODs() : null;

            if (lods == null || lods.Length < 2)
            {
                for (int i = 0; i < result.Length; i++) result[i] = true;
                return result;
            }

            HashSet<Renderer> nearest = new HashSet<Renderer>(lods[0].renderers);
            for (int i = 0; i < all.Length; i++) result[i] = nearest.Contains(all[i]);
            return result;
        }

        public void Deactivate() => gameObject.SetActive(false);

        /// <summary>Colisões só perto do Player: milhares de colisores ligados custam física à toa.</summary>
        public void SetCollisionEnabled(bool enabled)
        {
            if (collider != null && collider.enabled != enabled) collider.enabled = enabled;
        }

        private Collider AddCollider(ScatterProfile profile)
        {
            if (profile.collision == ScatterCollision.None) return null;

            Bounds bounds = GetLocalBounds();
            if (bounds.size == Vector3.zero) return null;

            if (profile.collision == ScatterCollision.Box)
            {
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center;
                // 80%: pedras são arredondadas, a caixa cheia deixaria "paredes invisíveis" nos cantos.
                box.size = bounds.size * 0.8f;
                return box;
            }

            // O raio é em metros; o colisor vive no espaço local, que pode ter escala (ex.: prefab ×12). O multiplicador
            // do grupo (ScatterTuning) entra em Place, porque muda com a calibração.
            float prefabScale = Mathf.Max(0.0001f, transform.lossyScale.x);
            trunkRadiusLocal = profile.trunkRadius / prefabScale;

            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = trunkRadiusLocal;
            capsule.height = Mathf.Max(bounds.size.y, trunkRadiusLocal * 2f);

            // O pivô da árvore é a base do tronco: centro em x/z = 0 (o centro da malha seguiria a copa, que pode pender).
            capsule.center = new Vector3(0f, bounds.center.y, 0f);
            trunkCapsule = capsule;
            return capsule;
        }

        /// <summary>Caixa que envolve todas as malhas, no espaço local da raiz do prefab.</summary>
        private Bounds GetLocalBounds()
        {
            Bounds result = new Bounds();
            bool hasAny = false;
            Matrix4x4 toRoot = transform.worldToLocalMatrix;

            foreach (MeshFilter filter in gameObject.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;

                Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
                Bounds mesh = filter.sharedMesh.bounds;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 point = matrix.MultiplyPoint3x4(corner);

                    if (!hasAny)
                    {
                        result = new Bounds(point, Vector3.zero);
                        hasAny = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }

            return result;
        }
    }
}
