using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// Capacete com lanterna presa em cima (improviso), na cabeça do Player: as mãos ficam livres para lutar e atirar.
    /// <list type="bullet">
    /// <item>O conjunto (prefab gerado por PROCEDURAL_GAME > Player > Setup Head Lamp) fica num objeto filho do pescoço que copia, a cada
    /// frame, a pose do osso da cabeça (sem a escala dele: em 1ª pessoa o osso da cabeça vira escala 0). Encaixe medido na pose inicial:
    /// o ponto de dentro do topo do capacete no topo da cabeça (osso HeadTop do Mixamo). Ajuste fino em <see cref="GearOffset"/> e
    /// <see cref="GearScale"/> (ferramenta DEV).</item>
    /// <item>Em 1ª pessoa o capacete e a lanterna somem como a cabeça, mas continuam na sombra (<see cref="PlayerBodyVisibility.AddHeadPart"/>).</item>
    /// <item>A luz (spot com máscara/cookie) é um objeto à parte, posicionado na lente: em 1ª pessoa aponta para onde a câmera olha; em 3ª,
    /// entre a frente da cabeça e a direção da câmera. A direção é suavizada (a cabeça balança com as animações).</item>
    /// </list>
    /// Liga/desliga: botão Lanterna (tela), F ou Y do gamepad. Depois virá como item da 1ª missão (encontrar a lanterna).
    /// </summary>
    // Depois da animação, do IK, da câmera (100) e do PlayerBodyVisibility: usa as poses finais deste frame.
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(PlayerMovement), typeof(PlayerInputReader))]
    public class HeadLamp : MonoBehaviour
    {
        // Espessura do casco do capacete (m, na escala do capacete): o lado de dentro do topo encosta no topo da cabeça.
        private const float HelmetShell = 0.015f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("Animator do modelo (humanoide). Se vazio, procura nos filhos.")]
        [SerializeField] private Animator animator;

        [Tooltip("Capacete com a lanterna (gerado por PROCEDURAL_GAME > Player > Setup Head Lamp): filhos Helmet, Flashlight e LensPoint.")]
        [SerializeField] private GameObject gearPrefab;

        [Header("Encaixe na cabeça (m na escala 1 do modelo; x = direita, y = cima, z = frente)")]
        [Tooltip("Primeiro teste (capacete_3): o capacete ficava alto e caído para trás, com a testa de fora. Por isso desce um pouco e vem para a frente.")]
        [SerializeField] private Vector3 gearOffset = new Vector3(0f, -0.02f, 0.015f);

        [SerializeField, Range(0.5f, 1.5f)] private float gearScale = 0.9f;

        [Tooltip("Inclinação do capacete (graus; positivo = frente para baixo), em volta do topo da cabeça.")]
        [SerializeField, Range(-45f, 45f)] private float gearPitch = 12f;

        [Tooltip("Inclinação da lanterna sobre o capacete (graus; positivo = lente para baixo), em volta do centro dela. Soma com a do capacete.")]
        [SerializeField, Range(-45f, 45f)] private float lampPitch;

        [Tooltip("Gira o capacete (não a lanterna) 180°, caso a frente do modelo esteja para trás.")]
        [SerializeField] private bool helmetBackwards;

        [Tooltip("Esconde o cabelo, caso atravesse o capacete.")]
        [SerializeField] private bool hideHair;

        [Header("Luz")]
        [SerializeField] private bool startOn;

        [SerializeField] private Color lightColor = new Color(1f, 0.94f, 0.82f);

        [SerializeField, Min(0f)] private float intensity = 3f;

        [Tooltip("Alcance (m). A neblina esconde o resto.")]
        [SerializeField, Min(1f)] private float range = 30f;

        [SerializeField, Range(10f, 120f)] private float spotAngle = 60f;

        [Tooltip("Parte do cone com luz cheia (0 a 1 do ângulo): o resto vai apagando até a borda.")]
        [SerializeField, Range(0f, 1f)] private float innerFraction = 0.45f;

        [Tooltip("Máscara da luz (cookie): dá o desenho de anel/manchas de uma lanterna de verdade.")]
        [SerializeField] private Texture cookie;

        [Tooltip("Em 3ª pessoa a luz aponta entre a frente da cabeça (0) e para onde a câmera olha (1).")]
        [SerializeField, Range(0f, 1f)] private float thirdPersonCameraAim = 0.6f;

        [Tooltip("Suavização (s) da direção da luz: a cabeça balança com as animações e a luz tremeria junto.")]
        [SerializeField, Min(0.001f)] private float aimSmoothing = 0.08f;

        private readonly List<Renderer> hair = new List<Renderer>();
        private PlayerMovement movement;
        private PlayerInputReader input;
        private Transform head;
        private Transform follower;
        private Transform tuning;
        private Transform gearRoot;
        private Transform helmet;
        private Transform lensPoint;
        private Transform lamp;
        private Renderer flashlightRenderer;
        private readonly List<int> glowSlots = new List<int>();
        private MaterialPropertyBlock block;
        private Vector3 helmetPivot;
        private Vector3 followOffset;
        private Quaternion followRotation = Quaternion.identity;
        private Light lampLight;
        private Camera viewCamera;
        private Quaternion aim;
        private bool aimReady;
        private bool isOn;

        /// <summary>Lanterna ligada.</summary>
        public bool IsOn => isOn;

        /// <summary>Disparado ao ligar (true) ou desligar (false).</summary>
        public event Action<bool> Toggled;

        // Ajustes ao vivo (ferramenta DEV). Valem na hora.
        public Vector3 GearOffset { get => gearOffset; set { gearOffset = value; ApplyFit(); } }
        public float GearScale { get => gearScale; set { gearScale = Mathf.Clamp(value, 0.5f, 1.5f); ApplyFit(); } }
        public float GearPitch { get => gearPitch; set { gearPitch = Mathf.Clamp(value, -45f, 45f); ApplyFit(); } }
        public float LampPitch { get => lampPitch; set { lampPitch = Mathf.Clamp(value, -45f, 45f); ApplyFit(); } }
        public bool HelmetBackwards { get => helmetBackwards; set { helmetBackwards = value; ApplyFit(); } }
        public bool HideHair { get => hideHair; set { hideHair = value; ApplyFit(); } }
        public float Intensity { get => intensity; set { intensity = Mathf.Max(0f, value); ApplyLight(); } }
        public float Range { get => range; set { range = Mathf.Max(1f, value); ApplyLight(); } }
        public float SpotAngle { get => spotAngle; set { spotAngle = Mathf.Clamp(value, 10f, 120f); ApplyLight(); } }

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            input = GetComponent<PlayerInputReader>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (gearPrefab == null || head == null || head.parent == null)
            {
                Debug.LogWarning($"{nameof(HeadLamp)}: sem o prefab do capacete ou sem o osso da cabeça. Rode PROCEDURAL_GAME > Player > Setup Head Lamp.", this);
                enabled = false;
                return;
            }

            block = new MaterialPropertyBlock();
            BuildGear();
            CreateLight();
            CollectHair();
            ApplyFit();
            ApplyLight();
        }

        private void Start()
        {
            // Some em 1ª pessoa junto com a cabeça (fica na sombra).
            PlayerBodyVisibility visibility = GetComponent<PlayerBodyVisibility>();
            if (visibility != null)
            {
                foreach (Renderer part in gearRoot.GetComponentsInChildren<Renderer>(true)) visibility.AddHeadPart(part);
            }

            SetOn(startOn);
        }

        private void OnDisable()
        {
            if (lampLight != null) lampLight.enabled = false;
        }

        private void OnEnable()
        {
            if (lampLight != null) lampLight.enabled = isOn;
        }

        private void Update()
        {
            if (input.FlashlightPressed) SetOn(!isOn);
        }

        private void LateUpdate()
        {
            // Copia a pose do osso da cabeça (sem a escala dele) para o capacete.
            follower.localPosition = head.localPosition + head.localRotation * followOffset;
            follower.localRotation = head.localRotation * followRotation;

            if (isOn) UpdateLightPose();
        }

        /// <summary>Liga ou desliga a lanterna (luz, brilho da lente e o botão da tela).</summary>
        public void SetOn(bool on)
        {
            isOn = on;
            aimReady = false;
            if (lampLight != null) lampLight.enabled = on && enabled;

            if (flashlightRenderer != null)
            {
                Color glow = on ? lightColor * 2f : Color.black;
                foreach (int slot in glowSlots)
                {
                    flashlightRenderer.GetPropertyBlock(block, slot);
                    block.SetColor(EmissionColorId, glow);
                    flashlightRenderer.SetPropertyBlock(block, slot);
                }
            }

            input.ShowFlashlight(on);
            Toggled?.Invoke(on);
        }

        // ---------------------------------------------------------------------------------------------------------------

        // Monta: pescoço > HeadGear (segue a cabeça, origem no topo da cabeça) > Tuning (ajuste fino) > prefab (deslocado para o ponto de
        // dentro do topo do capacete ficar na origem). Assim a escala do ajuste fino aumenta o capacete em volta do topo da cabeça.
        private void BuildGear()
        {
            float modelScale = Mathf.Max(0.01f, animator.transform.lossyScale.y);
            Transform neck = head.parent;

            follower = new GameObject("HeadGear").transform;
            follower.SetParent(neck, false);
            follower.rotation = transform.rotation; // pose inicial: orientado como o corpo
            follower.localScale = Vector3.one * (modelScale / Mathf.Max(0.0001f, neck.lossyScale.y));
            follower.position = FindHeadTop(modelScale);

            tuning = new GameObject("Tuning").transform;
            tuning.SetParent(follower, false);

            GameObject gear = Instantiate(gearPrefab, tuning, false);
            gear.name = gearPrefab.name;
            gearRoot = gear.transform;
            helmet = gearRoot.Find("Helmet");
            lamp = gearRoot.Find("Lamp");
            lensPoint = gearRoot.Find("Lamp/LensPoint");
            if (lensPoint == null) lensPoint = gearRoot.Find("LensPoint");

            Transform flashlight = gearRoot.Find("Lamp/Flashlight");
            if (flashlight == null) flashlight = gearRoot.Find("Flashlight");
            flashlightRenderer = flashlight != null ? flashlight.GetComponent<Renderer>() : null;
            if (flashlightRenderer != null)
            {
                Material[] materials = flashlightRenderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0) glowSlots.Add(i);
                }
            }

            // Ponto de dentro do topo do capacete (centro do casco, logo abaixo do topo), no espaço do prefab.
            helmetPivot = Vector3.up * 0.15f;
            if (helmet != null && helmet.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                Bounds bounds = filter.sharedMesh.bounds;
                Vector3 inside = new Vector3(bounds.center.x, bounds.max.y - HelmetShell, bounds.center.z);
                helmetPivot = helmet.localPosition + helmet.localRotation * Vector3.Scale(helmet.localScale, inside);
            }

            gearRoot.localPosition = -helmetPivot;

            foreach (Renderer part in gearRoot.GetComponentsInChildren<Renderer>(true))
            {
                part.shadowCastingMode = ShadowCastingMode.On;
                part.receiveShadows = true;
            }

            // Relação com o osso da cabeça no espaço do pescoço (sem a escala da cabeça).
            Quaternion inverseHead = Quaternion.Inverse(head.localRotation);
            followOffset = inverseHead * (follower.localPosition - head.localPosition);
            followRotation = inverseHead * follower.localRotation;
        }

        // Topo da cabeça: o osso final do Mixamo (HeadTop_End). Sem ele, uma estimativa acima do osso da cabeça.
        private Vector3 FindHeadTop(float modelScale)
        {
            foreach (Transform bone in head.GetComponentsInChildren<Transform>(true))
            {
                if (bone != head && bone.name.IndexOf("HeadTop", StringComparison.OrdinalIgnoreCase) >= 0) return bone.position;
            }

            return head.position + transform.up * (0.2f * modelScale);
        }

        private void CollectHair()
        {
            foreach (SkinnedMeshRenderer part in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (part.name.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0) hair.Add(part);
            }
        }

        private void ApplyFit()
        {
            if (tuning == null) return;

            // A origem do Tuning é o topo da cabeça: inclinar e escalar acontecem em volta dele.
            tuning.localPosition = gearOffset;
            tuning.localRotation = Quaternion.Euler(gearPitch, 0f, 0f);
            tuning.localScale = Vector3.one * gearScale;

            // Lanterna: inclina em volta do próprio centro (origem do grupo Lamp).
            if (lamp != null) lamp.localRotation = Quaternion.Euler(lampPitch, 0f, 0f);

            // Capacete virado: gira em volta do próprio centro (a lanterna não gira junto).
            if (helmet != null)
            {
                Quaternion turn = helmetBackwards ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                Vector3 center = new Vector3(helmetPivot.x, 0f, helmetPivot.z);
                helmet.localRotation = turn;
                helmet.localPosition = center - turn * center;
            }

            foreach (Renderer part in hair)
            {
                if (part != null) part.enabled = !hideHair;
            }
        }

        private void CreateLight()
        {
            GameObject lightObject = new GameObject("HeadLampLight");
            lightObject.transform.SetParent(transform, false);

            lampLight = lightObject.AddComponent<Light>();
            lampLight.type = LightType.Spot;
            lampLight.shadows = LightShadows.None;
            // Por pixel sempre: é a única luz extra do jogo e a que mais aparece.
            lampLight.renderMode = LightRenderMode.ForcePixel;
            lampLight.enabled = false;
        }

        private void ApplyLight()
        {
            if (lampLight == null) return;

            lampLight.color = lightColor;
            lampLight.intensity = intensity;
            lampLight.range = range;
            lampLight.spotAngle = spotAngle;
            lampLight.innerSpotAngle = spotAngle * innerFraction;
            lampLight.cookie = cookie;
        }

        // Na lente; 1ª pessoa: para onde a câmera olha; 3ª: entre a frente da cabeça e a câmera. Suavizado.
        private void UpdateLightPose()
        {
            if (viewCamera == null) viewCamera = Camera.main;

            Vector3 origin = lensPoint != null ? lensPoint.position : follower.position;
            Vector3 lampForward = lensPoint != null ? lensPoint.forward : transform.forward;
            Vector3 target = lampForward;

            if (viewCamera != null)
            {
                Vector3 cameraForward = viewCamera.transform.forward;
                target = movement.FaceCamera ? cameraForward : Vector3.Slerp(lampForward, cameraForward, thirdPersonCameraAim);
            }

            Quaternion wanted = Quaternion.LookRotation(target.sqrMagnitude > 0.0001f ? target : transform.forward, Vector3.up);
            if (!aimReady || Time.deltaTime <= 0f)
            {
                aim = wanted;
                aimReady = true;
            }
            else
            {
                aim = Quaternion.Slerp(aim, wanted, 1f - Mathf.Exp(-Time.deltaTime / aimSmoothing));
            }

            lampLight.transform.SetPositionAndRotation(origin, aim);
        }
    }
}
