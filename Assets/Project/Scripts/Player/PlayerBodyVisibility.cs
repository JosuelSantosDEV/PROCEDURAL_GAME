using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// O que se vê do próprio corpo. Em terceira pessoa o corpo aparece inteiro. Em primeira pessoa há dois modos (<see cref="ShowBodyInFirstPerson"/>):
    /// <list type="bullet">
    /// <item>Corpo visível (padrão): a CABEÇA some (o osso da cabeça é reduzido a zero) e o resto aparece: ao olhar para baixo se veem
    /// peito, braços, pernas e pés. Cabelo, olhos e cílios (malhas separadas) viram só sombra: continuam na sombra do chão. A pele da cabeça
    /// sai da sombra junto com o osso. O PlayerCameraController limita o olhar para baixo e adianta a câmera para a posição dos olhos, para
    /// nunca enxergar o pescoço aberto.</item>
    /// <item>Só sombra: nenhuma malha do corpo é desenhada para a câmera (modo "só sombra" do renderizador), e o corpo inteiro, cabeça
    /// incluída, projeta a sombra no chão.</item>
    /// </list>
    /// Quem chama <see cref="SetFirstPerson"/> é o PlayerCameraController ao trocar de modo.
    /// </summary>
    public class PlayerBodyVisibility : MonoBehaviour
    {
        [Tooltip("Animator do modelo (humanoide). Se vazio, procura nos filhos.")]
        [SerializeField] private Animator animator;

        [Tooltip("Primeira pessoa: ligado = se vê o próprio corpo (sem a cabeça); desligado = o corpo só aparece como sombra.")]
        [SerializeField] private bool showBodyInFirstPerson = true;

        [Tooltip("Partes presas à cabeça (pelo nome da malha). Em primeira pessoa com corpo visível, viram só sombra.")]
        [SerializeField] private string[] headPartNames = { "Hair", "Eye" };

        [Header("Olhos (relativos ao osso da cabeça, na escala 1 do modelo)")]
        [Tooltip("O osso da cabeça do Mixamo fica na base do crânio: os olhos estão aproximadamente este tanto (m) acima dele.")]
        [SerializeField, Min(0f)] private float eyesAboveHead = 0.07f;

        [Tooltip("Idem, à frente dele (m).")]
        [SerializeField, Min(0f)] private float eyesForward = 0.09f;

        [Header("Sombra da cabeça")]
        [Tooltip("Em 1ª pessoa com corpo visível a cabeça some (osso com escala 0) e levaria a cabeça da sombra junto: um elipsoide só-sombra no lugar dela repõe o contorno. Tamanho (largura, altura, profundidade) em metros na escala 1.")]
        [SerializeField] private Vector3 headShadowSize = new Vector3(0.16f, 0.23f, 0.2f);

        private readonly List<SkinnedMeshRenderer> headParts = new List<SkinnedMeshRenderer>();
        private readonly List<SkinnedMeshRenderer> bodyParts = new List<SkinnedMeshRenderer>();
        private Transform head;
        private Renderer headShadow;
        private bool firstPerson;
        private bool headHidden;
        private bool eyesMeasured;
        private float eyeHeightAbovePivot;
        private float eyeForwardOfPivot;

        /// <summary>Primeira pessoa mostra o corpo (sem cabeça)? A câmera consulta para limitar o ângulo e posicionar os olhos.</summary>
        public bool ShowBodyInFirstPerson
        {
            get => showBodyInFirstPerson;
            set
            {
                showBodyInFirstPerson = value;
                Apply();
            }
        }

        /// <summary>
        /// Onde ficam os olhos do modelo em relação ao pivô do Player (altura e distância à frente do eixo do corpo, em metros no mundo),
        /// medidos na pose inicial (escala do modelo incluída). A câmera em 1ª pessoa usa isto em vez de um valor fixo à mão.
        /// </summary>
        public bool TryGetEyes(out float heightAbovePivot, out float forward)
        {
            heightAbovePivot = eyeHeightAbovePivot;
            forward = eyeForwardOfPivot;
            return eyesMeasured;
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();

            Transform root = animator != null ? animator.transform : transform;
            if (animator != null && animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);

            foreach (SkinnedMeshRenderer meshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsHeadPart(meshRenderer.name)) headParts.Add(meshRenderer);
                else bodyParts.Add(meshRenderer);
            }

            if (headParts.Count + bodyParts.Count == 0)
            {
                Debug.LogWarning($"{nameof(PlayerBodyVisibility)}: nenhuma malha do corpo encontrada. Rode PROCEDURAL_GAME > Player > Setup Player Model.", this);
            }

            if (head == null)
            {
                Debug.LogWarning($"{nameof(PlayerBodyVisibility)}: osso da cabeça não encontrado (o modelo precisa ser Humanoid). A cabeça não será escondida.", this);
            }

            MeasureEyes();
            CreateHeadShadow();
            Apply();
        }

        // Mede na pose inicial (antes de qualquer animação, o modelo está na pose-base): posição do osso da cabeça + deslocamento até os olhos.
        private void MeasureEyes()
        {
            if (head == null) return;

            float scale = Mathf.Max(0.01f, head.lossyScale.y);
            Vector3 local = transform.InverseTransformPoint(head.position);
            eyeHeightAbovePivot = local.y + eyesAboveHead * scale;
            eyeForwardOfPivot = local.z + eyesForward * scale;
            eyesMeasured = true;
        }

        // Elipsoide só-sombra preso ao pescoço (o osso da cabeça fica com escala 0 e levaria um filho junto).
        private void CreateHeadShadow()
        {
            if (head == null || head.parent == null || bodyParts.Count == 0) return;

            // Só malha e renderizador, sem colisor: GameObject.CreatePrimitive adiciona um SphereCollider, cuja classe pode ser removida do
            // build pelo "engine code stripping" ("Can't add component because class 'SphereCollider' doesn't exist!").
            GameObject proxy = new GameObject("HeadShadow");
            proxy.AddComponent<MeshFilter>().sharedMesh = GetHeadShadowMesh();
            proxy.AddComponent<MeshRenderer>();

            Transform parent = head.parent;
            proxy.transform.SetParent(parent, false);

            float parentScale = Mathf.Max(0.01f, parent.lossyScale.y);
            float scale = Mathf.Max(0.01f, head.lossyScale.y);

            // Centro da cabeça: um pouco acima e à frente do osso (mesmo deslocamento dos olhos, menos a parte da frente do rosto).
            Vector3 center = head.position + transform.up * (eyesAboveHead * scale) + transform.forward * (eyesForward * 0.4f * scale);
            proxy.transform.position = center;
            proxy.transform.rotation = transform.rotation;
            proxy.transform.localScale = headShadowSize * (scale / parentScale);

            headShadow = proxy.GetComponent<Renderer>();
            headShadow.sharedMaterial = bodyParts[0].sharedMaterial;
            headShadow.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            headShadow.receiveShadows = false;
            headShadow.lightProbeUsage = LightProbeUsage.Off;
            headShadow.reflectionProbeUsage = ReflectionProbeUsage.Off;
            headShadow.enabled = false;
        }

        private static Mesh headShadowMesh;

        // Esfera de diâmetro 1 (12 x 8 segmentos, ~190 triângulos), criada uma vez; a escala do objeto a transforma em elipsoide.
        private static Mesh GetHeadShadowMesh()
        {
            if (headShadowMesh != null) return headShadowMesh;

            const int Longitudes = 12;
            const int Latitudes = 8;

            Vector3[] vertices = new Vector3[(Latitudes + 1) * (Longitudes + 1)];
            Vector3[] normals = new Vector3[vertices.Length];
            for (int lat = 0; lat <= Latitudes; lat++)
            {
                float theta = Mathf.PI * lat / Latitudes;
                for (int lon = 0; lon <= Longitudes; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / Longitudes;
                    Vector3 direction = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    int index = lat * (Longitudes + 1) + lon;
                    vertices[index] = direction * 0.5f;
                    normals[index] = direction;
                }
            }

            int[] triangles = new int[Latitudes * Longitudes * 6];
            int t = 0;
            for (int lat = 0; lat < Latitudes; lat++)
            {
                for (int lon = 0; lon < Longitudes; lon++)
                {
                    int a = lat * (Longitudes + 1) + lon;
                    int b = a + 1;
                    int c = a + Longitudes + 1;
                    int d = c + 1;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }
            }

            headShadowMesh = new Mesh { name = "HeadShadowSphere", vertices = vertices, normals = normals, triangles = triangles };
            headShadowMesh.RecalculateBounds();
            return headShadowMesh;
        }

        /// <summary>Chamado pela câmera: true = primeira pessoa.</summary>
        public void SetFirstPerson(bool value)
        {
            firstPerson = value;
            Apply();
        }

        private void Apply()
        {
            if (headParts.Count + bodyParts.Count == 0) return;

            bool showBody = firstPerson && showBodyInFirstPerson;

            // Terceira pessoa: tudo normal. Primeira com corpo: corpo normal, cabeça (cabelo/olhos) só sombra. Primeira só sombra: tudo só sombra.
            SetShadowMode(bodyParts, firstPerson && !showBody ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On);
            SetShadowMode(headParts, firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On);

            // A cabeça de verdade (osso com escala 0) deixa a sombra: o elipsoide só-sombra a substitui enquanto ela estiver escondida.
            if (headShadow != null) headShadow.enabled = showBody;

            if (!showBody && headHidden && head != null)
            {
                head.localScale = Vector3.one;
                headHidden = false;
            }
        }

        // Depois do Animator (que roda antes do LateUpdate): ele pode reescrever a escala do osso todo frame.
        private void LateUpdate()
        {
            if (head == null || !firstPerson || !showBodyInFirstPerson) return;

            head.localScale = Vector3.zero;
            headHidden = true;
        }

        private bool IsHeadPart(string meshName)
        {
            foreach (string part in headPartNames)
            {
                if (!string.IsNullOrEmpty(part) && meshName.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        private static void SetShadowMode(List<SkinnedMeshRenderer> renderers, ShadowCastingMode mode)
        {
            foreach (SkinnedMeshRenderer meshRenderer in renderers)
            {
                if (meshRenderer != null) meshRenderer.shadowCastingMode = mode;
            }
        }
    }
}
