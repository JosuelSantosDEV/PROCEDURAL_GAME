using System.Collections.Generic;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Um pedaço do terreno: GameObject com malha, renderer e colisor. Os objetos são reciclados
    /// (<see cref="Hide"/> e depois <see cref="Show"/> de novo) em vez de criados e destruídos,
    /// porque instanciar a todo momento causa travadas e lixo de memória no celular.
    /// </summary>
    public sealed class TerrainChunk
    {
        private readonly GameObject gameObject;
        private readonly Mesh mesh;
        private readonly MeshCollider meshCollider;

        // Água do lago (só em biomas com lagos): um filho com a própria malha, ligado só nos chunks que têm lago.
        private readonly GameObject waterObject;
        private readonly Mesh waterMesh;
        private readonly List<ScatterInstance> scatterInstances = new List<ScatterInstance>();

        private ScatterSpawner scatter;
        private bool scatterCollisionsOn;

        public Vector2Int Coord { get; private set; }

        public TerrainChunk(Transform parent, Material material, Material waterMaterial = null)
        {
            gameObject = new GameObject("Chunk", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            gameObject.transform.SetParent(parent, false);

            mesh = new Mesh { name = "ChunkMesh" };
            // Avisa o Unity que a malha será reescrita várias vezes (usa buffers adequados a isso).
            mesh.MarkDynamic();

            gameObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            // O terreno só RECEBE sombras (das árvores e pedras); não projeta, o que poupa o mapa de sombras. Se as sombras
            // estiverem desligadas, o shader nem as calcula (a variante sem sombras é usada).
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = true;
            meshCollider = gameObject.GetComponent<MeshCollider>();

            if (waterMaterial != null)
            {
                waterObject = new GameObject("Water", typeof(MeshFilter), typeof(MeshRenderer));
                waterObject.transform.SetParent(gameObject.transform, false);

                waterMesh = new Mesh { name = "WaterMesh" };
                waterMesh.MarkDynamic();
                waterObject.GetComponent<MeshFilter>().sharedMesh = waterMesh;

                // A água só aparece: sem sombras, sem sondas de luz/reflexo e sem colisor (o Player entra nela).
                MeshRenderer waterRenderer = waterObject.GetComponent<MeshRenderer>();
                waterRenderer.sharedMaterial = waterMaterial;
                waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                waterRenderer.receiveShadows = false;
                waterRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                waterRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                waterObject.SetActive(false);
            }

            gameObject.SetActive(false);
        }

        /// <param name="scatterSpawner">Cria árvores, pedras etc. sobre o chunk. Pode ser nulo (chunk só com terreno).</param>
        /// <param name="waterBuilder">Monta a água dos lagos do chunk. Pode ser nulo (bioma sem lagos).</param>
        public void Show(Vector2Int coord, Vector3 worldPosition, ChunkMeshBuilder builder, ScatterSpawner scatterSpawner = null, WaterMeshBuilder waterBuilder = null)
        {
            Coord = coord;
            gameObject.transform.position = worldPosition;
#if UNITY_EDITOR
            gameObject.name = $"Chunk {coord.x},{coord.y}";
#endif

            builder.Build(mesh, coord);
            gameObject.SetActive(true);

            // O chunk reciclado pode ter tido água antes: o filho é ligado ou desligado conforme o chunk novo.
            if (waterObject != null) waterObject.SetActive(waterBuilder != null && waterBuilder.Build(waterMesh, coord));

            // Atribuir de novo (null e depois a malha) força o Unity a recalcular a colisão com a malha nova.
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;

            scatter = scatterSpawner;
            scatterCollisionsOn = false;
            if (scatter != null) scatter.Populate(coord, scatterInstances);
        }

        /// <summary>Refaz só os objetos do chunk (a quantidade mudou), sem recriar o terreno. As posições seguem as mesmas.</summary>
        public void Repopulate()
        {
            if (scatter == null) return;

            // Place() deixa os colisores desligados; guardar o estado e reaplicar mantém a colisão dos chunks perto do Player.
            bool collisions = scatterCollisionsOn;

            scatter.Release(scatterInstances);
            scatterCollisionsOn = false;
            scatter.Populate(Coord, scatterInstances);
            SetScatterCollisions(collisions);
        }

        /// <summary>Liga ou desliga os colisores dos objetos do chunk (só os chunks perto do Player precisam).</summary>
        public void SetScatterCollisions(bool enabled)
        {
            if (scatterCollisionsOn == enabled) return;
            scatterCollisionsOn = enabled;

            foreach (ScatterInstance instance in scatterInstances) instance.SetCollisionEnabled(enabled);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (scatter != null) scatter.Release(scatterInstances);
        }

        /// <summary>A Mesh é um recurso próprio; sem destruir, ela vaza memória quando o mundo é descartado.</summary>
        public void Dispose()
        {
            if (mesh != null) Object.Destroy(mesh);
            if (waterMesh != null) Object.Destroy(waterMesh);
        }
    }
}
