using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace _Project.Core.Scripts.FlamePhysics
{
    public class Flameable : MonoBehaviour
    {
        [SerializeField] private float _timeToBurn;
        [SerializeField] private float _burnDuration;
        [SerializeField] private float _emitRadius;
        [SerializeField] private ParticleSystem _particleSystem;


        private Renderer _renderer;

        private Coroutine _burningCoroutine;
        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        public void Burn()
        {
            if (_burningCoroutine != null) return;
            _burningCoroutine = StartCoroutine(EmitBurn());
            _particleSystem.Play();
        }

        private IEnumerator EmitBurn()
        {
            yield return new WaitForSeconds(_timeToBurn + Random.Range(-0.6f, 0.6f));
            //TODO change for nonAlloc
            var colliders = Physics.OverlapSphere(transform.position, _emitRadius);

            foreach (var coll in colliders)
            {
                if (coll.TryGetComponent(out Flameable flameable))
                {
                    flameable.Burn();
                }
            }
            yield return new WaitForSeconds(_burnDuration + Random.Range(-0.5f, 0.5f));
            Destroy(gameObject);
        }
    }
}