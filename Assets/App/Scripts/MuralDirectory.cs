using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARMurals
{
    /// <summary>
    /// What the campus map knows about each mural: its name, level, where it is, a line
    /// of story, and where its pin sits on the map. Edit the asset, not the scene - the
    /// main scene builder keeps this asset when it rebuilds.
    /// </summary>
    [CreateAssetMenu(menuName = "AR Murals/Mural Directory", fileName = "MuralDirectory")]
    public class MuralDirectory : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("M1..M5. Must match the mural's slot and reference image name.")]
            public string slot = "M1";
            public string title = "Untitled mural";
            [Tooltip("Building level the mural is on, 1..levelCount.")]
            [Min(1)] public int level = 1;
            [Tooltip("Where to find it, in words a first-year would understand.")]
            public string location = "Location to be confirmed";
            [TextArea(2, 4)] public string blurb = "";
            [Tooltip("Pin position on the map image. (0,0) bottom-left, (1,1) top-right.")]
            public Vector2 mapPosition = new Vector2(0.5f, 0.5f);
            [Tooltip("Photo shown on the card and as the scanning hint. Defaults to the mural's target image.")]
            public Texture2D photo;
        }

        [Tooltip("The combined map, shown on the ALL tab and for any level without its own map.")]
        public Texture2D campusMap;
        [Min(1)] public int levelCount = 3;
        [Tooltip("Optional map per level (element 0 = level 1). Leave empty to reuse the combined map.")]
        public List<Texture2D> levelMaps = new List<Texture2D>();
        public List<Entry> murals = new List<Entry>();

        public Entry Find(string slot) =>
            murals.Find(e => string.Equals(e.slot, slot, StringComparison.OrdinalIgnoreCase));

        /// <summary>The map for a level (0 = all levels), falling back to the combined map.</summary>
        public Texture2D MapFor(int level)
        {
            if (level >= 1 && level <= levelMaps.Count && levelMaps[level - 1] != null)
                return levelMaps[level - 1];
            return campusMap;
        }
    }
}
