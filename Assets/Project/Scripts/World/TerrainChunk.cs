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
        private readonly List<ScatterInstance> scatterInstances = new List<ScatterInstance>();

        private ScatterSpawner scatter;
        private bool scatterCollisionsOn;

        public Vector2Int Coord { get; private set; }

        public TerrainChunk(Transform parent, Material material)
        {
            gameObject = new GameObject("Chunk", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            gameObject.transform.SetParent(parent, false);

            mesh = new Mesh { name = "ChunkMesh" };
            // Avisa o Unity que a malha será reescrita várias vezes (usa buffers adequados a isso).
            mesh.MarkDynamic();

            gameObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            // Sombras dinâmicas desligadas por enquanto: o terreno não projeta nem recebe (poupa processamento no celular).
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshCollider = gameObject.GetComponent<MeshCollider>();

            gameObject.SetActive(false);
        }

        /// <param name="scatterSpawner">Cria árvores, pedras etc. sobre o chunk. Pode ser nulo (chunk só com terreno).</param>
        public void Show(Vector2Int coord, Vector3 worldPosition, ChunkMeshBuilder builder, ScatterSpawner scatterSpawner = null)
        {
            Coord = coord;
            gameObject.transform.position = worldPosition;
#if UNITY_EDITOR
            gameObject.name = $"Chunk {coord.x},{coord.y}";
#endif

            builder.Build(mesh, coord);
            gameObject.SetActive(true);

            // Atribuir de novo (null e depois a malha) força o Unity a recalcular a colisão com a malha nova.
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;

            scatter = scatterSpawner;
            scatterCollisionsOn = false;
            if (scatter != null) scatter.Populate(coord, scatterInstances);
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
        }
    }
}
