using System;
using UnityEngine;

namespace Flame
{
    public class ParticleEmitter : MaterialReactionEffect
    {
        [SerializeField] private ParticleSystem _particleSystem;

        public override void PlayEffect()
        {
            _particleSystem.Play();
        }

        public override void ReverEffect()
        {
            _particleSystem.Stop();
        }
    }
}