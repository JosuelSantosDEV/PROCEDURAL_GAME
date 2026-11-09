using System.Collections.Generic;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Objetos coletáveis espalhados pelo mundo (hoje, frutas): quais estão ativos perto do Player e quais já foram pegos nesta partida.
    /// As posições vêm do sorteio determinístico dos chunks, então um chunk recriado sortearia a fruta de novo: a lista de coletados
    /// é o que impede comida infinita ao ir e voltar. Vale só durante a partida; gravar em disco fica para quando houver progresso salvo.
    /// </summary>
    public sealed class PickupRegistry
    {
        public sealed class Item
        {
            public ScatterInstance Instance;
            public Vector3 Position;
            public PickupKind Kind;
        }

        private readonly List<Item> active = new List<Item>();
        private readonly HashSet<Vector2Int> collected = new HashSet<Vector2Int>();

        public bool IsCollected(Vector3 position) => collected.Contains(Key(position));

        public void Add(ScatterInstance instance, Vector3 position, PickupKind kind)
        {
            active.Add(new Item { Instance = instance, Position = position, Kind = kind });
        }

        /// <summary>O chunk saiu: o objeto volta ao pool, sem contar como coletado.</summary>
        public void Remove(ScatterInstance instance)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].Instance != instance) continue;

                active.RemoveAt(i);
                return;
            }
        }

        /// <summary>O coletável mais próximo dentro do alcance, medido no plano (a fruta fica no chão e o pivô do Player, na cintura).</summary>
        public bool TryFindNearest(Vector3 from, float reach, out Item nearest)
        {
            nearest = null;
            float best = reach * reach;

            foreach (Item item in active)
            {
                float dx = item.Position.x - from.x;
                float dz = item.Position.z - from.z;
                float distance = dx * dx + dz * dz;
                if (distance > best) continue;

                best = distance;
                nearest = item;
            }

            return nearest != null;
        }

        /// <summary>Tira o objeto do mundo e lembra da posição. Ele continua na lista do chunk e volta ao pool quando o chunk sair.</summary>
        public void Collect(Item item)
        {
            collected.Add(Key(item.Position));
            item.Instance.Deactivate();
            active.Remove(item);
        }

        // Só x e z, em décimos de metro: a altura pode variar com a calibração da ferramenta DEV.
        private static Vector2Int Key(Vector3 position) => new Vector2Int(Mathf.RoundToInt(position.x * 10f), Mathf.RoundToInt(position.z * 10f));
    }
}
