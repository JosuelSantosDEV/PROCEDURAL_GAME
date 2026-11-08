using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Espuma e ondas do Player na água. Enquanto os pés do Player estão abaixo da superfície de um lago, deixa pontos (posição, idade,
    /// força) por onde ele anda; o shader da água (Water.shader) desenha em cada ponto um anel que se expande e uma espuma que
    /// acompanha o rastro e some em <see cref="Life"/> segundos. Ao ENTRAR na água dispara uma rajada de 3 anéis (splash).
    /// Sem lago, fora d'água e sem pontos vivos, não faz nada (e o shader nem roda o laço). Só existe em biomas com lagos.
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    [DefaultExecutionOrder(-84)]
    public class WaterWakeController : MonoBehaviour
    {
        /// <summary>Quantidade de pontos (igual a WAKE_MAX no Water.shader). Anéis mais antigos são reaproveitados.</summary>
        public const int MaxWakes = 20;

        /// <summary>Segundos de vida de cada ponto (igual a WAKE_LIFE no Water.shader).</summary>
        public const float Life = 2.0f;

        private const float MoveStep = 0.7f;      // metros andados entre um ponto e o seguinte
        private const float IdleInterval = 1.2f;  // parado na água: um anel fraco a cada tanto

        private static readonly int DataId = Shader.PropertyToID("_WakeData");
        private static readonly int CountId = Shader.PropertyToID("_WakeCount");

        private struct Wake
        {
            public float x, z, birth, strength;
        }

        private readonly Wake[] wakes = new Wake[MaxWakes];
        private readonly Vector4[] data = new Vector4[MaxWakes];

        private WorldGenerator world;
        private LakeMap lakes;
        private Transform player;
        private CharacterController controller;
        private int next;
        private bool wasInWater;
        private bool uploadedActive;
        private float lastX, lastZ, lastEmitTime;

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();

            // Todos os pontos começam mortos (nascimento muito no passado).
            for (int i = 0; i < MaxWakes; i++) wakes[i].birth = -1000f;
        }

        private void Start()
        {
            lakes = world.Lakes;

            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found != null)
            {
                player = found.transform;
                controller = found.GetComponent<CharacterController>();
            }

            if (lakes == null || player == null) enabled = false;
        }

        private void Update()
        {
            float now = Time.time;
            Vector3 position = player.position;

            // Pés abaixo da superfície de um lago em que o Player está dentro.
            float feet = controller != null ? controller.bounds.min.y : position.y - 1f;
            bool inWater = lakes.Query(position.x, position.z, out float distance, out float level, out _) && distance < 0f && feet < level + 0.05f;

            if (inWater)
            {
                if (!wasInWater)
                {
                    Splash(position.x, position.z, now);
                    lastX = position.x;
                    lastZ = position.z;
                    lastEmitTime = now;
                }
                else
                {
                    float dx = position.x - lastX;
                    float dz = position.z - lastZ;

                    if (dx * dx + dz * dz >= MoveStep * MoveStep)
                    {
                        float speed = controller != null ? controller.velocity.magnitude : 3f;
                        Emit(position.x, position.z, now, Mathf.Clamp(speed / 4f, 0.7f, 1.3f));
                        lastX = position.x;
                        lastZ = position.z;
                        lastEmitTime = now;
                    }
                    else if (now - lastEmitTime >= IdleInterval)
                    {
                        Emit(position.x, position.z, now, 0.6f);
                        lastEmitTime = now;
                    }
                }
            }

            wasInWater = inWater;
            Upload(now);
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(CountId, 0f);
        }

        private void Splash(float x, float z, float now)
        {
            // Três anéis seguidos (o 2º e o 3º nascem um instante depois): a água "espirra" e acalma.
            Emit(x, z, now, 1.9f);
            Emit(x, z, now + 0.12f, 1.5f);
            Emit(x, z, now + 0.26f, 1.1f);
        }

        private void Emit(float x, float z, float birth, float strength)
        {
            wakes[next] = new Wake { x = x, z = z, birth = birth, strength = strength };
            next = (next + 1) % MaxWakes;
        }

        // Manda os pontos ao shader. A idade é calculada aqui (em segundos de jogo), então não depende do relógio do shader.
        private void Upload(float now)
        {
            int alive = 0;

            for (int i = 0; i < MaxWakes; i++)
            {
                float age = now - wakes[i].birth;
                bool active = age < Life;
                if (active) alive++;

                // Morto: força 0 (o shader pula). Ainda não nasceu: idade negativa (o shader também pula).
                data[i] = new Vector4(wakes[i].x, wakes[i].z, age, active ? wakes[i].strength : 0f);
            }

            // Sem nenhum ponto vivo, avisa uma vez só (o laço do shader deixa de rodar) e para de enviar.
            if (alive == 0 && !uploadedActive) return;

            Shader.SetGlobalVectorArray(DataId, data);
            Shader.SetGlobalFloat(CountId, alive > 0 ? 1f : 0f);
            uploadedActive = alive > 0;
        }
    }
}
