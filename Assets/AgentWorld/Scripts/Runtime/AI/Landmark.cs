using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentWorld
{
    /// A named place agents can refer to. The LLM only ever sees and returns these ids.
    public class Landmark : MonoBehaviour
    {
        public static readonly List<Landmark> All = new List<Landmark>();

        public string id = "place";
        public string description = "";
        /// Where an agent stands to "be at" this place (offset from the transform).
        public Vector2 approach = new Vector2(0, -1);

        public Vector2 StandPoint => (Vector2)transform.position + approach;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static Landmark Find(string id) => All.FirstOrDefault(l => l.id == id);
    }
}
