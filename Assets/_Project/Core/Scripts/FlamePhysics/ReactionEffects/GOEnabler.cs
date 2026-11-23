using UnityEngine;

namespace Flame
{
    public class GOEnabler : MaterialReactionEffect
    {
        [SerializeField] private GameObject _go;
        public override void PlayEffect()
        {
            _go.SetActive(true);
        }

        public override void ReverEffect()
        {
            _go.SetActive(false);
        }
    }
}