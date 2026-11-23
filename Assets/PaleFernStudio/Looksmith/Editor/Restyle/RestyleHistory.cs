using System.Collections.Generic;
using System.Linq;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>Which Looksmith backup restyled a material (for the material inspector's header).</summary>
    public static class RestyleHistory
    {
        public sealed class Record
        {
            public SnapshotManifest manifest;
            public string fromShader;
            public bool inPlace;     // a .mat swapped in place (revertable on its own); false = extracted from a model
        }

        /// <summary>The newest applied (or partly reverted) backup that restyled this material, or null.</summary>
        public static Record Find(string materialPath)
        {
            if (string.IsNullOrEmpty(materialPath))
                return null;
            foreach (var m in LooksmithBackups.List().OrderByDescending(x => x.createdUtc))
            {
                if (m.status != SnapshotManifest.Applied && m.status != SnapshotManifest.PartiallyReverted)
                    continue;
                foreach (var sc in m.changes)
                {
                    LooksmithChange change;
                    try { change = sc.Restore(); }
                    catch { continue; }
                    if (change is RestyleMaterialChange r && r.materialPath == materialPath)
                        return Still(m, materialPath) ? new Record { manifest = m, fromShader = r.sourceShader, inPlace = true } : null;
                    if (change is ExtractAndRestyleChange x && x.extractedPath == materialPath)
                        return new Record { manifest = m, fromShader = x.sourceShader, inPlace = false };
                }
            }
            return null;
        }

        // Partly reverted snapshots may already have this file back.
        static bool Still(SnapshotManifest m, string path)
        {
            var e = m.entries.FirstOrDefault(x => x.path == path);
            return e != null && LooksmithBackups.Hash(path) != e.sha256Before;
        }

        /// <summary>Reverts just this material from its backup (hash-checked like any revert).</summary>
        public static string RevertMaterial(Record record, string materialPath)
        {
            var plan = Revert.Plan(record.manifest);
            return Revert.Execute(plan, new HashSet<string> { materialPath });
        }
    }
}
