using UnityEngine;

namespace Flame
{
    public sealed class MeshVariantRandomizer : MonoBehaviour
    {
        [SerializeField] private MeshFilter _meshFilter;
        [SerializeField] private MeshCollider _meshCollider;
        [SerializeField] private Mesh[] _variants;

        private void Awake()
        {
            if (_meshFilter == null)
                _meshFilter = GetComponent<MeshFilter>();

            if (_meshCollider == null)
                _meshCollider = GetComponent<MeshCollider>();

            if (_meshFilter == null || _variants == null || _variants.Length == 0)
            {
                Debug.LogError($"{nameof(MeshVariantRandomizer)} on '{name}' has no mesh variants configured.", this);
                return;
            }

            var mesh = _variants[Random.Range(0, _variants.Length)];
            if (mesh == null)
            {
                Debug.LogError($"{nameof(MeshVariantRandomizer)} on '{name}' selected a missing mesh variant.", this);
                return;
            }

            _meshFilter.sharedMesh = mesh;

            if (_meshCollider != null)
            {
                _meshCollider.sharedMesh = null;
                _meshCollider.sharedMesh = mesh;
            }
        }
    }
}
