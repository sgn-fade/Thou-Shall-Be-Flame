using System;
using DG.Tweening;
using UnityEngine;

namespace Flame
{
    public class MaterialColorChange : MaterialReactionEffect
    {
        [SerializeField] private Color _targetColor;
        [SerializeField] private float _transitionDuration;
        private Renderer _renderer;
        private Color _startColor;
        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        public override void PlayEffect()
        {
            _startColor = _renderer.material.GetColor("_BaseColor");
            _renderer.material.DOColor(
                _targetColor,
                "_BaseColor",
                _transitionDuration
            );
        }

        public override void ReverEffect()
        {
            _renderer.material.DOColor(
                _targetColor,
                "_BaseColor",
                _transitionDuration
            );
        }
    }
}