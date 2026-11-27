using UnityEngine;

/// <summary>Plays the configured capture particle prefab on the dragged object's mesh.</summary>
public sealed class CaptureOrbitParticlesView : MonoBehaviour
{
    [SerializeField] private RigidbodyDrag _drag;
    [SerializeField] private ParticleSystem _particlePrefab;

    private ParticleSystem _activeParticles;

    private void Awake()
    {
        if (_drag == null) _drag = GetComponent<RigidbodyDrag>();
    }

    private void OnEnable()
    {
        if (_drag != null) _drag.DragStateChanged += OnDragStateChanged;
    }

    private void OnDisable()
    {
        if (_drag != null) _drag.DragStateChanged -= OnDragStateChanged;
        StopEffect();
    }

    private void OnDragStateChanged(bool isDragging)
    {
        if (!isDragging || _drag == null || _drag.DraggedRigidbody == null)
        {
            StopEffect();
            return;
        }

        StartEffect(_drag.DraggedRigidbody.transform);
    }

    private void StartEffect(Transform target)
    {
        if (_particlePrefab == null) return;

        var meshFilter = target.GetComponentInChildren<MeshFilter>();
        var skinnedMesh = target.GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh = meshFilter != null ? meshFilter.sharedMesh : skinnedMesh != null ? skinnedMesh.sharedMesh : null;
        if (mesh == null) return;

        StopEffect();
        var meshTransform = meshFilter != null ? meshFilter.transform : skinnedMesh.transform;
        _activeParticles = Instantiate(_particlePrefab, meshTransform.position, meshTransform.rotation, target);

        var shape = _activeParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.mesh = mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
        shape.scale = meshTransform.localScale;
        _activeParticles.GetComponent<ParticleSystemRenderer>().mesh = mesh;
        _activeParticles.Play(true);
    }

    private void StopEffect()
    {
        if (_activeParticles == null) return;

        _activeParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        var main = _activeParticles.main;
        var trails = _activeParticles.trails;
        var remainingTime = main.startLifetime.constantMax + (trails.enabled ? trails.lifetime.constantMax : 0f);
        _activeParticles.transform.SetParent(null, true);
        Destroy(_activeParticles.gameObject, remainingTime);
        _activeParticles = null;
    }
}
