using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Counts what Looksmith's passes actually executed, per camera type. Used by tests and the
    /// Hub's health checks to prove passes ran, without reading screen pixels.
    /// </summary>
    public static class LooksmithPassDiagnostics
    {
        public struct Entry
        {
            public int setupExecutions;
            public int debugExecutions;
            public int linesExecutions, outlineExecutions, highlightExecutions, idsExecutions;
            public float lastLineWidthPx, lastTargetHeight;
            public int styleExecutions, maskExecutions;
            /// <summary>The styles of the last stack executed, in order ("LooksmithPosterize > Finish").</summary>
            public string lastStyleOrder;
            public bool lastStylesMasked;
            /// <summary>Instance id of the camera that last ran the style stack.</summary>
            public int lastStyledCamera;
            public int waterExecutions;
            /// <summary>The Yarn surface's draws (Pro).</summary>
            public int yarnExecutions;
            /// <summary>The last water setup requested the opaque texture (refraction).</summary>
            public bool lastWaterRefraction;
            public bool lastHadDepth, lastHadNormals, lastHadOpaque;
        }

        static readonly Dictionary<CameraType, Entry> s_Entries = new Dictionary<CameraType, Entry>();

        public static void Reset() => s_Entries.Clear();

        public static Entry Get(CameraType type) => s_Entries.TryGetValue(type, out var e) ? e : default;

        internal static void RecordSetup(CameraType type)
        {
            var e = Get(type);
            e.setupExecutions++;
            s_Entries[type] = e;
        }

        public static void RecordLines(CameraType type, float widthPx = 0f, float targetHeight = 0f)
        {
            var e = Get(type);
            e.linesExecutions++;
            e.lastLineWidthPx = widthPx;
            e.lastTargetHeight = targetHeight;
            s_Entries[type] = e;
        }
        public static void RecordOutline(CameraType type) { var e = Get(type); e.outlineExecutions++; s_Entries[type] = e; }
        public static void RecordHighlight(CameraType type) { var e = Get(type); e.highlightExecutions++; s_Entries[type] = e; }
        public static void RecordIds(CameraType type) { var e = Get(type); e.idsExecutions++; s_Entries[type] = e; }
        public static void RecordMask(CameraType type) { var e = Get(type); e.maskExecutions++; s_Entries[type] = e; }
        public static void RecordYarn(CameraType type) { var e = Get(type); e.yarnExecutions++; s_Entries[type] = e; }

        public static void RecordWater(CameraType type, bool refraction)
        {
            var e = Get(type);
            e.waterExecutions++;
            e.lastWaterRefraction = refraction;
            s_Entries[type] = e;
        }

        public static void RecordStyles(CameraType type, string order, bool masked, int cameraId = 0)
        {
            var e = Get(type);
            e.lastStyledCamera = cameraId;
            e.styleExecutions++;
            e.lastStyleOrder = order;
            e.lastStylesMasked = masked;
            s_Entries[type] = e;
        }

        internal static void RecordDebug(CameraType type, bool depth, bool normals, bool opaque)
        {
            var e = Get(type);
            e.debugExecutions++;
            e.lastHadDepth = depth;
            e.lastHadNormals = normals;
            e.lastHadOpaque = opaque;
            s_Entries[type] = e;
        }

        public static string Describe()
        {
            var sb = new StringBuilder();
            foreach (var pair in s_Entries)
                sb.AppendFormat("{0}: setup={1} lines={6} ids={7} outline={8} highlight={9} styles={10} [{11}] mask={12} debug={2} depth={3} normals={4} opaque={5}\n", pair.Key,
                    pair.Value.setupExecutions, pair.Value.debugExecutions, pair.Value.lastHadDepth, pair.Value.lastHadNormals,
                    pair.Value.lastHadOpaque, pair.Value.linesExecutions, pair.Value.idsExecutions, pair.Value.outlineExecutions,
                    pair.Value.highlightExecutions, pair.Value.styleExecutions, pair.Value.lastStyleOrder, pair.Value.maskExecutions);
            return sb.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Entries.Clear();
    }
}
