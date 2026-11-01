using System.Collections.Generic;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Cria os objetos de um chunk e os recicla quando o chunk some. Há um pool por (perfil, prefab):
    /// instanciar e destruir árvores o tempo todo causaria travadas e lixo de memória no celular.
    /// </summary>
    public sealed class ScatterSpawner
    {
        private readonly Transform root;
        private readonly Transform staging;
        private readonly ChunkScatter generator;
        private readonly IReadOnlyList<ScatterProfile> profiles;
        private readonly Dictionary<(ScatterProfile, GameObject), Stack<ScatterInstance>> pools =
            new Dictionary<(ScatterProfile, GameObject), Stack<ScatterInstance>>();

        // Reaproveitada a cada chunk para não gerar lixo de memória.
        private readonly List<ScatterPlacement> placements = new List<ScatterPlacement>();

        public ScatterSpawner(Transform parent, ChunkScatter generator, IReadOnlyList<ScatterProfile> profiles)
        {
            this.generator = generator;
            this.profiles = profiles;

            root = new GameObject("Scatter").transform;
            root.SetParent(parent, false);

            // Área de montagem desativada: ver o parâmetro 'staging' de ScatterInstance.
            GameObject stagingObject = new GameObject("Staging");
            stagingObject.SetActive(false);
            staging = stagingObject.transform;
            staging.SetParent(root, false);
        }

        /// <summary>Coloca no mundo os objetos de todos os perfis para esse chunk e os devolve em <paramref name="into"/>.</summary>
        public void Populate(Vector2Int coord, List<ScatterInstance> into)
        {
            foreach (ScatterProfile profile in profiles)
            {
                if (profile == null) continue;

                generator.Generate(coord, profile, placements);

                foreach (ScatterPlacement placement in placements)
                {
                    GameObject prefab = profile.prefabs[placement.PrefabIndex];
                    if (prefab == null) continue;

                    ScatterInstance instance = Acquire(profile, prefab);
                    instance.Place(placement.Position, placement.Rotation, placement.Scale);
                    into.Add(instance);
                }
            }
        }

        /// <summary>Devolve os objetos ao pool (ficam desativados, prontos para o próximo chunk).</summary>
        public void Release(List<ScatterInstance> instances)
        {
            foreach (ScatterInstance instance in instances)
            {
                instance.Deactivate();
                pools[(instance.Profile, instance.Prefab)].Push(instance);
            }

            instances.Clear();
        }

        private ScatterInstance Acquire(ScatterProfile profile, GameObject prefab)
        {
            var key = (profile, prefab);

            if (!pools.TryGetValue(key, out Stack<ScatterInstance> pool))
            {
                pool = new Stack<ScatterInstance>();
                pools.Add(key, pool);
            }

            return pool.Count > 0 ? pool.Pop() : new ScatterInstance(profile, prefab, staging, root);
        }
    }
}
