using UnityEngine;

namespace Flame
{
    public class GOSpawner : MaterialReactionEffect
    {
        [SerializeField] private GameObject _go;
        [SerializeField] private bool _deleteSpawner;
        [SerializeField] private bool _oneShot;
        private bool _played;

        public override void PlayEffect()
        {
            if (_oneShot && _played) return;
            _played = true;

            Instantiate(_go, transform.position, transform.rotation);

            if (_deleteSpawner) Destroy(gameObject);
        }
    }
}