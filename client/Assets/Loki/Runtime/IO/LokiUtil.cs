using UnityEngine;

namespace Loki
{
    public static class LokiUtil
    {
        /// <summary>Destroy that also works in edit mode (batch tools).</summary>
        public static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
