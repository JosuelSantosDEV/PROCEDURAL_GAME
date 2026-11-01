using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Um objeto reciclável (árvore, pedra...) criado a partir de um prefab. Na criação o prefab é "limpo":
    /// colisores originais saem (malhas de colisão pesam), sombras são desligadas (como no terreno) e,
    /// se o perfil pedir, entra um colisor simples no lugar.
    /// </summary>
    public sealed class ScatterInstance
    {
        private readonly GameObject gameObject;
        private readonly Transform transform;
        private readonly Vector3 baseScale;
        private readonly LODGroup lodGroup;
        private readonly Collider collider;

        public ScatterProfile Profile { get; }
        public GameObject Prefab { get; }

        /// <param name="staging">Objeto DESATIVADO onde o prefab nasce. Sob um pai inativo o Unity não ativa os componentes,
        /// então os colisores originais (convexos, caros de calcular) nunca chegam a ser criados antes de os removermos.</param>
        /// <param name="parent">Onde a instância fica depois de pronta.</param>
        public ScatterInstance(ScatterProfile profile, GameObject prefab, Transform staging, Transform parent)
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

            foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            collider = AddCollider(profile);
            if (collider != null) collider.enabled = false;

            // Já inativo e sem os colisores originais: agora pode sair do pai inativo.
            transform.SetParent(parent, false);
        }

        public void Place(Vector3 position, Quaternion rotation, float scale)
        {
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = baseScale * scale;

            // O LODGroup guarda o tamanho do objeto de quando foi criado; sem recalcular, a troca de LOD
            // e o descarte por distância usariam o tamanho errado depois de mudar a escala.
            if (lodGroup != null) lodGroup.RecalculateBounds();

            if (collider != null) collider.enabled = false;
            gameObject.SetActive(true);
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

            // O raio é pedido em metros; o colisor vive no espaço local, que pode ter escala (ex.: prefab ×12).
            float localRadius = profile.trunkRadius / Mathf.Max(0.0001f, transform.lossyScale.x);
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = localRadius;
            capsule.height = Mathf.Max(bounds.size.y, localRadius * 2f);
            capsule.center = new Vector3(0f, bounds.center.y, 0f);
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
