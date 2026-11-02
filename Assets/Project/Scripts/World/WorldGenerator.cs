using System.Collections.Generic;
using System.Diagnostics;
using PROCEDURAL_GAME.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Gera o terreno em chunks ao redor do Player: cria os que ficam perto, recicla os que ficam longe.
    /// A mesma seed sempre produz o mesmo mundo.
    /// </summary>
    // Ordem negativa: o mundo precisa existir antes do Player começar a cair com a gravidade.
    [DefaultExecutionOrder(-100)]
    public class WorldGenerator : MonoBehaviour
    {
        [Header("Alvo")]
        [Tooltip("Quem o mundo acompanha. Se vazio, procura o objeto com a tag 'Player'.")]
        [SerializeField] private Transform target;

        [Header("Mundo")]
        [SerializeField] private int seed = 12345;

        [Tooltip("Ignora a seed acima e sorteia uma nova a cada Play. A seed usada aparece no Console.")]
        [SerializeField] private bool randomSeed;

        [Tooltip("Lado de cada chunk, em metros.")]
        [SerializeField, Min(8f)] private float chunkSize = 32f;

        [Tooltip("Quadrados por lado de um chunk. Mais = relevo mais detalhado, porém mais pesado (vértices = (n+1)²).")]
        [SerializeField, Range(4, 128)] private int resolution = 32;

        [Header("Carregamento")]
        [Tooltip("Distância de visão, em chunks. Cresce ao quadrado: 4 ≈ 50 chunks, 6 ≈ 110 chunks.")]
        [SerializeField, Range(1, 12)] private int viewRadius = 4;

        [Tooltip("Chunks ao redor do Player criados de uma vez no início, antes do primeiro frame. Evita o Player cair no vazio.")]
        [SerializeField, Range(1, 3)] private int initialRadius = 1;

        [Tooltip("Tempo máximo por frame gasto criando chunks. Menor = jogo mais fluido, porém o mundo aparece mais devagar.")]
        [SerializeField, Min(0.5f)] private float maxMillisecondsPerFrame = 4f;

        [Header("Relevo")]
        [SerializeField] private NoiseSettings noiseSettings = new NoiseSettings();

        [Tooltip("Material com o shader PROCEDURAL_GAME/TerrainVertexColor (lê a cor calculada nos vértices).")]
        [SerializeField] private Material terrainMaterial;

        [Tooltip("Cores do terreno e atmosfera (neblina, luz, céu). Troque o arquivo para comparar visuais.")]
        [SerializeField] private BiomeProfile biome;

        [Header("Objetos")]
        [Tooltip("Raio, em metros, ao redor do ponto (0, 0) sem objetos. Evita o Player começar dentro de uma árvore.")]
        [SerializeField, Min(0f)] private float spawnClearRadius = 6f;

        [Tooltip("Só os chunks até essa distância (em chunks) têm colisão nos objetos. 2 = o chunk do Player e mais 2 ao redor.")]
        [SerializeField, Range(0, 6)] private int collisionRadius = 2;

        private readonly Dictionary<Vector2Int, TerrainChunk> activeChunks = new Dictionary<Vector2Int, TerrainChunk>();
        private readonly Stack<TerrainChunk> pool = new Stack<TerrainChunk>();
        private readonly List<Vector2Int> pending = new List<Vector2Int>();
        private readonly List<Vector2Int> toRelease = new List<Vector2Int>();
        private readonly List<Vector2Int> repopulateQueue = new List<Vector2Int>();

        // 0 = sem ajuste; senão, raio imposto pela qualidade automática (sempre menor que o base).
        private int viewRadiusOverride;

        private TerrainNoise terrainNoise;
        private ChunkMeshBuilder meshBuilder;
        private ScatterSpawner scatter;
        private Vector2Int lastCenter;
        private bool hasCenter;

        /// <summary>Seed em uso (útil para reproduzir um mundo que você gostou).</summary>
        public int Seed => seed;

        /// <summary>Perfil visual em uso (cores do terreno e atmosfera).</summary>
        public BiomeProfile Biome => biome;

        /// <summary>Raio de carregamento do bioma, em chunks: o dele, se definir um, senão o do World. É o máximo da qualidade automática.</summary>
        public int BaseViewRadius => biome != null && biome.viewRadius > 0 ? biome.viewRadius : viewRadius;

        /// <summary>Raio em uso agora, em chunks: o ajuste da qualidade automática, se houver, senão o base.</summary>
        public int ViewRadius => viewRadiusOverride > 0 ? viewRadiusOverride : BaseViewRadius;

        public float ChunkSize => chunkSize;

        /// <summary>
        /// Muda o raio de carregamento em jogo (qualidade automática). Chunks que ficam fora são devolvidos ao pool
        /// e os que faltam entram na fila normal. Um valor igual ou maior que o base remove o ajuste.
        /// </summary>
        public void SetViewRadiusOverride(int radius)
        {
            int value = radius >= BaseViewRadius ? 0 : Mathf.Max(1, radius);
            if (value == viewRadiusOverride) return;

            viewRadiusOverride = value;
            if (hasCenter) RefreshChunks(lastCenter);
        }

        /// <summary>
        /// Refaz os objetos dos chunks já carregados (a quantidade mudou). É feito aos poucos, dentro do orçamento
        /// de milissegundos por frame, começando pelos chunks mais perto do Player.
        /// </summary>
        public void RepopulateAll()
        {
            if (scatter == null) return;

            repopulateQueue.Clear();
            repopulateQueue.AddRange(activeChunks.Keys);

            // Ordem decrescente de distância: tiramos do fim da lista, então o mais perto sai primeiro.
            Vector2Int center = WorldToChunk(target.position);
            repopulateQueue.Sort((a, b) => SqrDistance(b, center).CompareTo(SqrDistance(a, center)));
        }

        /// <summary>Altura do terreno em qualquer ponto do mundo, mesmo onde o chunk ainda não foi criado.</summary>
        /// <summary>Muda quais objetos espalhados projetam sombra (chamado pelo <see cref="ShadowController"/>).</summary>
        public void ApplyShadowLevel(int level) => scatter?.SetShadowLevel(level);

        public float GetHeight(float x, float z) => terrainNoise.GetHeight(x, z);

        private void Awake()
        {
            if (target == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) target = player.transform;
            }

            if (target == null)
            {
                Debug.LogError($"{nameof(WorldGenerator)}: nenhum alvo definido e nenhum objeto com a tag 'Player' na cena.", this);
                enabled = false;
                return;
            }

            if (terrainMaterial == null)
            {
                Debug.LogError($"{nameof(WorldGenerator)}: 'Terrain Material' não foi atribuído no Inspector.", this);
                enabled = false;
                return;
            }

            // Bioma escolhido no menu inicial tem prioridade sobre o configurado na cena.
            if (GameSession.SelectedBiome != null) biome = GameSession.SelectedBiome;

            if (biome == null)
            {
                // Sem perfil o mundo ainda funciona, com as cores padrão do BiomeProfile.
                Debug.LogWarning($"{nameof(WorldGenerator)}: 'Biome' não foi atribuído; usando cores padrão. Rode PROCEDURAL_GAME > Create Procedural World.", this);
                biome = ScriptableObject.CreateInstance<BiomeProfile>();
            }

            if (randomSeed) seed = Random.Range(int.MinValue, int.MaxValue);
            Debug.Log($"Mundo gerado com a seed {seed}.");

            terrainNoise = new TerrainNoise(seed, noiseSettings);
            meshBuilder = new ChunkMeshBuilder(terrainNoise, chunkSize, resolution, biome);

            if (biome.scatter != null && biome.scatter.Length > 0)
            {
                scatter = new ScatterSpawner(transform, new ChunkScatter(terrainNoise, seed, chunkSize, spawnClearRadius), biome.scatter);
            }

            // Aplica o nível de sombras do menu (luz, URP e objetos). Nasce junto: não exige refazer a cena.
            if (GetComponent<ShadowController>() == null) gameObject.AddComponent<ShadowController>();
        }

        private void Start()
        {
            LoadInitialChunks();
            PlaceTargetOnGround();
        }

        private void Update()
        {
            Vector2Int center = WorldToChunk(target.position);

            // Só reavalia o que carregar/descartar quando o Player muda de chunk.
            if (!hasCenter || center != lastCenter)
            {
                lastCenter = center;
                hasCenter = true;
                RefreshChunks(center);
            }

            BuildPendingChunks();
            RepopulatePending();
        }

        private void OnDestroy()
        {
            foreach (TerrainChunk chunk in activeChunks.Values) chunk.Dispose();
            foreach (TerrainChunk chunk in pool) chunk.Dispose();
        }

        private Vector2Int WorldToChunk(Vector3 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / chunkSize), Mathf.FloorToInt(position.z / chunkSize));
        }

        private void LoadInitialChunks()
        {
            Vector2Int center = WorldToChunk(target.position);

            for (int dz = -initialRadius; dz <= initialRadius; dz++)
            {
                for (int dx = -initialRadius; dx <= initialRadius; dx++)
                {
                    LoadChunk(new Vector2Int(center.x + dx, center.y + dz));
                }
            }

            // As colisões acabaram de ser criadas; sem isso a física só as enxerga no próximo passo.
            Physics.SyncTransforms();
        }

        /// <summary>Coloca o Player em cima do terreno: o ponto inicial (0, 1, 0) pode estar dentro de uma colina.</summary>
        private void PlaceTargetOnGround()
        {
            Vector3 position = target.position;
            float ground = GetHeight(position.x, position.z);

            // O pivô da cápsula fica no centro; sobe meia altura (mais uma folga) para os pés tocarem o chão.
            float halfHeight = 1f;
            CharacterController controller = target.GetComponent<CharacterController>();
            if (controller != null) halfHeight = controller.height * 0.5f - controller.center.y;

            position.y = ground + halfHeight + 0.1f;

            // Com o CharacterController ligado, mudar a posição direto é desfeito por ele no próximo Move.
            if (controller != null) controller.enabled = false;
            target.position = position;
            if (controller != null) controller.enabled = true;
        }

        /// <summary>Descarta o que ficou longe e lista, do mais perto ao mais longe, o que falta criar.</summary>
        private void RefreshChunks(Vector2Int center)
        {
            // Descarta com uma folga de 1 chunk além do raio de carregamento. Sem isso, andar na divisa
            // criaria e apagaria o mesmo chunk toda hora.
            int radius = ViewRadius;
            int releaseRadius = radius + 1;
            toRelease.Clear();
            foreach (KeyValuePair<Vector2Int, TerrainChunk> pair in activeChunks)
            {
                if (SqrDistance(pair.Key, center) > releaseRadius * releaseRadius) toRelease.Add(pair.Key);
            }

            foreach (Vector2Int coord in toRelease)
            {
                TerrainChunk chunk = activeChunks[coord];
                activeChunks.Remove(coord);
                chunk.Hide();
                pool.Push(chunk);
            }

            pending.Clear();
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dz * dz > radius * radius) continue;

                    Vector2Int coord = new Vector2Int(center.x + dx, center.y + dz);
                    if (!activeChunks.ContainsKey(coord)) pending.Add(coord);
                }
            }

            // Ordem decrescente de distância: assim tiramos sempre do fim da lista (barato) e o mais perto sai primeiro.
            pending.Sort((a, b) => SqrDistance(b, center).CompareTo(SqrDistance(a, center)));

            foreach (KeyValuePair<Vector2Int, TerrainChunk> pair in activeChunks)
            {
                pair.Value.SetScatterCollisions(IsNearForCollision(pair.Key, center));
            }
        }

        private bool IsNearForCollision(Vector2Int coord, Vector2Int center)
        {
            return SqrDistance(coord, center) <= collisionRadius * collisionRadius;
        }

        private void BuildPendingChunks()
        {
            if (pending.Count == 0) return;

            Stopwatch watch = Stopwatch.StartNew();

            while (pending.Count > 0)
            {
                int last = pending.Count - 1;
                Vector2Int coord = pending[last];
                pending.RemoveAt(last);

                LoadChunk(coord);

                // Pelo menos um chunk por frame, para o mundo sempre andar; o resto respeita o orçamento de tempo.
                if (watch.Elapsed.TotalMilliseconds >= maxMillisecondsPerFrame) break;
            }
        }

        private void RepopulatePending()
        {
            if (repopulateQueue.Count == 0) return;

            Stopwatch watch = Stopwatch.StartNew();

            while (repopulateQueue.Count > 0)
            {
                int last = repopulateQueue.Count - 1;
                Vector2Int coord = repopulateQueue[last];
                repopulateQueue.RemoveAt(last);

                // O chunk pode ter sido descartado enquanto esperava na fila.
                if (activeChunks.TryGetValue(coord, out TerrainChunk chunk)) chunk.Repopulate();

                if (watch.Elapsed.TotalMilliseconds >= maxMillisecondsPerFrame) break;
            }
        }

        private void LoadChunk(Vector2Int coord)
        {
            if (activeChunks.ContainsKey(coord)) return;

            TerrainChunk chunk = pool.Count > 0 ? pool.Pop() : new TerrainChunk(transform, terrainMaterial);
            chunk.Show(coord, new Vector3(coord.x * chunkSize, 0f, coord.y * chunkSize), meshBuilder, scatter);
            chunk.SetScatterCollisions(IsNearForCollision(coord, WorldToChunk(target.position)));
            activeChunks.Add(coord, chunk);
        }

        private static int SqrDistance(Vector2Int a, Vector2Int b)
        {
            int dx = a.x - b.x;
            int dy = a.y - b.y;
            return dx * dx + dy * dy;
        }
    }
}
