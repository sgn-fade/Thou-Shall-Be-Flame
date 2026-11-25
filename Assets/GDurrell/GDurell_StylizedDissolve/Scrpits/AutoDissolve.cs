using UnityEngine;
namespace GDurell.StylizedDissolve
{
    public class AutoDissolve : MonoBehaviour
    {
        private Material dissolveMaterial;

        [Tooltip("Duración en segundos para una disolución completa")]
        public float duration = 2f;

        private void Start()
        {
            Renderer rend = GetComponent<Renderer>();

            if (rend != null)
                dissolveMaterial = rend.material;
            else
                Debug.LogWarning("¡Falta el componente Renderer en este objeto, Jefe!");
        }

        void Update()
        {
            if (dissolveMaterial != null && duration > 0f)
            {
                // Time.time / duration hace que tarde exactamente 'duration' segundos en llegar a 1.
                // PingPong con un máximo de 1f asegura que nunca le enviemos al shader un valor que lo rompa.
                float dissolveValue = Mathf.PingPong(Time.time / duration, 1f);

                dissolveMaterial.SetFloat("_DissolveAmount", dissolveValue);
            }
        }
    }
}