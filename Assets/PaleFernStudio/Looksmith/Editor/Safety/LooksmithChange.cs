using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    public enum ChangeGroup
    {
        Project = 0,
        Scene = 1,
        Assets = 2,
        Materials = 3,
    }

    /// <summary>
    /// One previewable, opt-in write. Concrete changes are [Serializable] with public fields only, so a
    /// running Changeset can be journaled to disk and resumed after a domain reload.
    /// </summary>
    [Serializable]
    public abstract class LooksmithChange
    {
        public bool enabled = true;

        public abstract ChangeGroup group { get; }
        /// <summary>Preview label, e.g. "Switch PC_Renderer to Forward+".</summary>
        public abstract string title { get; }
        public virtual string detail => string.Empty;

        /// <summary>Project-relative files this change will modify or create (assets, scenes, ProjectSettings).</summary>
        public abstract IEnumerable<string> Writes();

        /// <summary>Scenes this change edits; they must be saved (clean) before Apply.</summary>
        public virtual IEnumerable<string> Scenes() => Enumerable.Empty<string>();

        public abstract void Apply(ChangeContext context);

        /// <summary>
        /// Called after Revert restored this change's files. Settings Unity keeps in memory (PlayerSettings...)
        /// are re-synced here so the editor doesn't write the old in-memory value back.
        /// </summary>
        public virtual void AfterRevert() { }

        public override string ToString() => title;
    }

    /// <summary>A serialized change inside a journal or manifest.</summary>
    [Serializable]
    public sealed class SerializedChange
    {
        public string type;
        public string json;

        public static SerializedChange From(LooksmithChange change) =>
            new SerializedChange { type = change.GetType().AssemblyQualifiedName, json = JsonUtility.ToJson(change) };

        public LooksmithChange Restore()
        {
            var t = Type.GetType(type);
            if (t == null)
                throw new InvalidOperationException("Looksmith: change type not found: " + type);
            var change = (LooksmithChange)Activator.CreateInstance(t);
            JsonUtility.FromJsonOverwrite(json, change);
            return change;
        }
    }
}
