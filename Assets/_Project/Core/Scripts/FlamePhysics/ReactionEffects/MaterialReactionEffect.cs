using UnityEngine;

namespace Flame
{
    public class MaterialReactionEffect : MonoBehaviour
    {
        [SerializeField] private float _maxTemperature = 1000f;

        protected float MaxTemperature => _maxTemperature;

        public virtual void PlayEffect()
        {

        }

        public virtual void ReverEffect()
        {

        }

        public virtual void UpdateEffectStrength(double temperature, float minTemperature)
        {

        }
    }
}
