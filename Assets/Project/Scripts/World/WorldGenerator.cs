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

        // A distância de visão (m) vem do menu Desempenho (Perto/Médio/Longe) e é reduzida pela qualidade automática, sempre
        // por meio do ViewDistanceController. O mundo carrega todo chunk que toca o círculo dessa distância ao redor do Player.
        private static readonly int[] GroundTexIds =
        {
            Shader.PropertyToID("_GroundTex"), Shader.PropertyToID("_GroundTex2"), Shader.PropertyToID("_GroundTex3")
        };

        private static readonly int[] GroundNrmIds =
        {
            Shader.PropertyToID("_GroundNrm"), Shader.PropertyToID("_GroundNrm2"), Shader.PropertyToID("_GroundNrm3")
        };

        private static readonly int GroundNormalOnId = Shader.PropertyToID("_GroundNormalOn");
        private static readonly int GroundTilingId = Shader.PropertyToID("_GroundTiling");
        private static readonly int GroundStrengthId = Shader.PropertyToID("_GroundStrength");
        private static readonly int VertexInfluenceId = Shader.PropertyToID("_VertexInfluence");
        private static readonly int LumaRefId = Shader.PropertyToID("_LumaRef");
        private static readonly int SandTexId = Shader.PropertyToID("_SandTex");
        private static readonly int BedTexId = Shader.PropertyToID("_BedTex");
        private static readonly int SandNrmId = Shader.PropertyToID("_SandNrm");
        private static readonly int BedNrmId = Shader.PropertyToID("_BedNrm");

        private bool hasGroundNormals;
        private bool groundNormalsApplied;
        private float viewDistance;
        private Vector3 lastRefreshPosition;
        private Vector3 sortPosition;
        private System.Comparison<Vector2Int> farthestFirst;

        // Chunks só são descartados bem além de onde são carregados (histerese): andar na divisa não cria e apaga o mesmo chunk.
        private const float ReleaseMargin = 24f;

        private TerrainNoise terrainNoise;
        private ChunkMeshBuilder meshBuilder;
        private ScatterSpawner scatter;
        private WaterMeshBuilder waterBuilder;
        private Material waterMaterial;
        private bool hasCenter;

        /// <summary>Seed em uso (útil para reproduzir um mundo que você gostou).</summary>
        public int Seed => seed;

        /// <summary>Perfil visual em uso (cores do terreno e atmosfera).</summary>
        public BiomeProfile Biome => biome;

        /// <summary>Distância de visão em uso, em metros: o mundo carrega tudo o que está a esta distância do Player.</summary>
        public float ViewDistanceMeters => viewDistance;

        public float ChunkSize => chunkSize;

        /// <summary>
        /// Muda a distância de carregamento (menu Desempenho ou qualidade automática). Chunks que ficam fora são devolvidos
        /// ao pool e os que faltam entram na fila normal.
        /// </summary>
        public void SetViewDistance(float meters)
        {
            meters = Mathf.Max(chunkSize, meters);
            if (Mathf.Approximately(meters, viewDistance)) return;

            viewDistance = meters;
            if (hasCenter) RefreshChunks(target.position);
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

        /// <summary>Lagos do mundo; nulo se o bioma não tem.</summary>
        public LakeMap Lakes => terrainNoise != null ? terrainNoise.Lakes : null;

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

            viewDistance = Mathf.Max(chunkSize, PerformanceSettings.GetViewDistance());
            farthestFirst = (a, b) => SqrDistanceToChunkCenter(b, sortPosition).CompareTo(SqrDistanceToChunkCenter(a, sortPosition));

            if (randomSeed) seed = Random.Range(int.MinValue, int.MaxValue);
            Debug.Log($"Mundo gerado com a seed {seed}.");

            // Cópia do material: a textura de chão é do bioma, e mexer no asset sujaria o arquivo no Editor.
            terrainMaterial = CreateTerrainMaterial(terrainMaterial, biome);

            terrainNoise = new TerrainNoise(seed, noiseSettings, biome.lakes);
            meshBuilder = new ChunkMeshBuilder(terrainNoise, chunkSize, resolution, biome, seed);

            if (terrainNoise.Lakes != null)
            {
                if (biome.lakes.waterMaterial == null)
                {
                    Debug.LogWarning($"Bioma '{biome.name}' com lagos ligados, mas sem 'Water Material': o lago aparece sem água. Rode PROCEDURAL_GAME > Create Procedural World.", this);
                }
                else
                {
                    waterMaterial = biome.lakes.waterMaterial;
                    waterBuilder = new WaterMeshBuilder(terrainNoise, chunkSize);
                }

                // Efeito de estar debaixo d'água (neblina e cor azuladas).
                if (GetComponent<UnderwaterController>() == null) gameObject.AddComponent<UnderwaterController>();
            }

            if (biome.scatter != null && biome.scatter.Length > 0)
            {
                scatter = new ScatterSpawner(transform, new ChunkScatter(terrainNoise, seed, chunkSize, spawnClearRadius), biome.scatter);
            }

            // Aplica o nível de sombras do menu (luz, URP e objetos). Nasce junto: não exige refazer a cena.
            if (GetComponent<ShadowController>() == null) gameObject.AddComponent<ShadowController>();

            // Resolução interna do jogo (Baixa/Média/Alta) e HDR desligado.
            if (GetComponent<RenderQualityController>() == null) gameObject.AddComponent<RenderQualityController>();

            // Neblina e plano de corte da câmera acompanham a distância de visão.
            if (GetComponent<ViewDistanceController>() == null) gameObject.AddComponent<ViewDistanceController>();
        }

        // Passa ao shader as texturas de chão do bioma (uma por camada/zona) e, se houver, os mapas de normal. Sem textura:
        // uma branca com força 0 (só a cor de vértice). Não há variante de shader: tudo é feito por propriedades do material.
        private Material CreateTerrainMaterial(Material source, BiomeProfile profile)
        {
            Material material = new Material(source);

            Texture2D[] albedos = new Texture2D[BiomeProfile.MaxGroundLayers];
            Texture2D[] normals = new Texture2D[BiomeProfile.MaxGroundLayers];
            int layers = 0;

            if (profile.groundLayers != null)
            {
                for (int i = 0; i < profile.groundLayers.Length && layers < BiomeProfile.MaxGroundLayers; i++)
                {
                    if (profile.groundLayers[i].albedo == null) continue;

                    albedos[layers] = profile.groundLayers[i].albedo;
                    normals[layers] = profile.groundLayers[i].normal;
                    layers++;
                }
            }

            // Sem camadas: a textura única antiga, se houver.
            if (layers == 0 && profile.groundTexture != null)
            {
                albedos[0] = profile.groundTexture;
                layers = 1;
            }

            hasGroundNormals = false;
            for (int i = 0; i < BiomeProfile.MaxGroundLayers; i++)
            {
                // Camadas que faltam repetem a primeira (o peso delas é 0, mas o material precisa de uma textura válida).
                Texture2D albedo = albedos[i] != null ? albedos[i] : (albedos[0] != null ? albedos[0] : Texture2D.whiteTexture);
                material.SetTexture(GroundTexIds[i], albedo);

                if (normals[i] != null)
                {
                    material.SetTexture(GroundNrmIds[i], normals[i]);
                    hasGroundNormals = true;
                }
            }

            // Areia dos lagos (só com lagos; o peso vem do UV1 da malha, que sem lagos fica zerado).
            LakeSettings lakeSettings = profile.lakes;
            Texture2D sand = lakeSettings != null && lakeSettings.enabled && lakeSettings.sandTexture != null ? lakeSettings.sandTexture : Texture2D.whiteTexture;
            Texture2D bed = lakeSettings != null && lakeSettings.enabled && lakeSettings.bedTexture != null ? lakeSettings.bedTexture : sand;
            material.SetTexture(SandTexId, sand);
            material.SetTexture(BedTexId, bed);

            // Mapas de normal da areia (Qualidade do solo). Sem eles, o padrão do material ("bump") vale: relevo plano.
            if (lakeSettings != null && lakeSettings.enabled)
            {
                if (lakeSettings.sandNormal != null)
                {
                    material.SetTexture(SandNrmId, lakeSettings.sandNormal);
                    hasGroundNormals = true;
                }

                if (lakeSettings.bedNormal != null)
                {
                    material.SetTexture(BedNrmId, lakeSettings.bedNormal);
                    hasGroundNormals = true;
                }
            }

            bool hasTexture = layers > 0;
            material.SetFloat(GroundTilingId, 1f / Mathf.Max(0.5f, profile.groundTileMeters));
            material.SetFloat(GroundStrengthId, hasTexture ? profile.groundTextureStrength : 0f);
            material.SetFloat(VertexInfluenceId, profile.groundVertexInfluence);

            // Luminância (linear) da cor de chão do bioma: a cor de vértice acima dela clareia a textura e abaixo escurece.
            Color grass = profile.grassColor.linear;
            material.SetFloat(LumaRefId, grass.r * 0.299f + grass.g * 0.587f + grass.b * 0.114f);

            groundNormalsApplied = !PerformanceSettings.EffectiveGroundQuality;
            ApplyGroundQuality(material);

            Debug.Log(hasTexture
                ? $"Textura de chão do bioma '{profile.name}': {layers} camada(s), {(hasGroundNormals ? "com" : "sem")} mapa de normal, repete a cada {profile.groundTileMeters:0.##} m, shader '{material.shader.name}'."
                : $"Bioma '{profile.name}' sem textura de chão: só a cor de vértice.");
            return material;
        }

        // Qualidade do solo (menu Desempenho): liga ou desliga a leitura dos mapas de normal. Só tem efeito se o bioma os tem.
        private void ApplyGroundQuality(Material material)
        {
            bool on = hasGroundNormals && PerformanceSettings.EffectiveGroundQuality;
            if (on == groundNormalsApplied) return;

            groundNormalsApplied = on;
            material.SetFloat(GroundNormalOnId, on ? 1f : 0f);
        }

        private void Start()
        {
            LoadInitialChunks();
            PlaceTargetOnGround();
        }

        private void Update()
        {
            // Reavalia o que carregar/descartar a cada RefreshStep metros andados (e não só ao trocar de chunk): o que o
            // Player enxerga é um círculo em metros, e a borda de um chunk pode estar perto demais mesmo no meio dele.
            ApplyGroundQuality(terrainMaterial);

            Vector3 position = target.position;
            float movedX = position.x - lastRefreshPosition.x;
            float movedZ = position.z - lastRefreshPosition.z;
            if (!hasCenter || movedX * movedX + movedZ * movedZ >= ViewDistance.RefreshStep * ViewDistance.RefreshStep)
            {
                hasCenter = true;
                RefreshChunks(position);
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

        /// <summary>
        /// Descarta o que ficou longe e lista, do mais perto ao mais longe, o que falta criar. Carrega todo chunk que toca o
        /// círculo de (distância de visão + passo de reavaliação) ao redor do Player: assim, até a próxima reavaliação, tudo
        /// o que está dentro da distância de visão existe, em qualquer direção (um raio contado em chunks deixava buracos nas diagonais).
        /// </summary>
        private void RefreshChunks(Vector3 position)
        {
            lastRefreshPosition = position;
            Vector2Int center = WorldToChunk(position);

            float loadDistance = viewDistance + ViewDistance.RefreshStep;
            float loadSqr = loadDistance * loadDistance;
            float releaseDistance = loadDistance + ReleaseMargin;
            float releaseSqr = releaseDistance * releaseDistance;

            toRelease.Clear();
            foreach (KeyValuePair<Vector2Int, TerrainChunk> pair in activeChunks)
            {
                if (SqrDistanceToChunk(pair.Key, position) > releaseSqr) toRelease.Add(pair.Key);
            }

            foreach (Vector2Int coord in toRelease)
            {
                TerrainChunk chunk = activeChunks[coord];
                activeChunks.Remove(coord);
                chunk.Hide();
                pool.Push(chunk);
            }

            pending.Clear();
            int range = Mathf.CeilToInt(loadDistance / chunkSize) + 1;
            for (int dz = -range; dz <= range; dz++)
            {
                for (int dx = -range; dx <= range; dx++)
                {
                    Vector2Int coord = new Vector2Int(center.x + dx, center.y + dz);
                    if (SqrDistanceToChunk(coord, position) > loadSqr) continue;
                    if (!activeChunks.ContainsKey(coord)) pending.Add(coord);
                }
            }

            // Ordem decrescente de distância: assim tiramos sempre do fim da lista (barato) e o mais perto sai primeiro.
            sortPosition = position;
            pending.Sort(farthestFirst);

            foreach (KeyValuePair<Vector2Int, TerrainChunk> pair in activeChunks)
            {
                pair.Value.SetScatterCollisions(IsNearForCollision(pair.Key, center));
            }
        }

        // Distância ao quadrado do ponto até o retângulo do chunk (0 se estiver dentro dele).
        private float SqrDistanceToChunk(Vector2Int coord, Vector3 point)
        {
            float minX = coord.x * chunkSize;
            float minZ = coord.y * chunkSize;
            float dx = Mathf.Max(minX - point.x, 0f, point.x - (minX + chunkSize));
            float dz = Mathf.Max(minZ - point.z, 0f, point.z - (minZ + chunkSize));
            return dx * dx + dz * dz;
        }

        private float SqrDistanceToChunkCenter(Vector2Int coord, Vector3 point)
        {
            float dx = (coord.x + 0.5f) * chunkSize - point.x;
            float dz = (coord.y + 0.5f) * chunkSize - point.z;
            return dx * dx + dz * dz;
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

            TerrainChunk chunk = pool.Count > 0 ? pool.Pop() : new TerrainChunk(transform, terrainMaterial, waterMaterial);
            chunk.Show(coord, new Vector3(coord.x * chunkSize, 0f, coord.y * chunkSize), meshBuilder, scatter, waterBuilder);
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
