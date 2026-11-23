using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Marks a scene object Looksmith created through a Changeset, so revert and uninstall remove exactly
    /// what Looksmith added and nothing else.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class LooksmithOwned : MonoBehaviour
    {
        [Tooltip("The backup (snapshot) that created this object.")]
        public string snapshotId;
        [Tooltip("Why Looksmith added it.")]
        public string reason;
    }
}
