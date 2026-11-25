using System.Collections;
using UnityEngine;
namespace GDurell.StylizedDissolve
{
    public class DissolveController : MonoBehaviour
    {
        [Header("Dissolve Settings")]
        [Tooltip("Time in seconds it takes to fully dissolve or appear.")]
        public float dissolveDuration = 2f;

        private Material dissolveMaterial;
        private Coroutine dissolveCoroutine;

        private void Start()
        {
            Renderer rend = GetComponent<Renderer>();
            if (rend != null)
                dissolveMaterial = rend.material;
            else
                Debug.LogWarning("DissolveController: No Renderer component found on " + gameObject.name);
        }

        /// <summary>
        /// Starts the dissolve effect (Object disappears).
        /// </summary>
        public void TriggerDissolve()
        {
            if (dissolveMaterial == null) return;

            // Stop any running dissolve to prevent visual glitches
            if (dissolveCoroutine != null)
            {
                StopCoroutine(dissolveCoroutine);
            }

            dissolveCoroutine = StartCoroutine(DissolveRoutine(0f, 1f));
        }

        /// <summary>
        /// Starts the appear effect (Object materializes).
        /// </summary>
        public void TriggerAppear()
        {
            if (dissolveMaterial == null) return;

            // Stop any running dissolve to prevent visual glitches
            if (dissolveCoroutine != null)
            {
                StopCoroutine(dissolveCoroutine);
            }

            dissolveCoroutine = StartCoroutine(DissolveRoutine(1f, 0f));
        }

        private IEnumerator DissolveRoutine(float startValue, float endValue)
        {
            float elapsedTime = 0f;

            while (elapsedTime < dissolveDuration)
            {
                elapsedTime += Time.deltaTime;

                // Calculate the exact percentage of completion
                float lerpValue = Mathf.Lerp(startValue, endValue, elapsedTime / dissolveDuration);
                dissolveMaterial.SetFloat("_DissolveAmount", lerpValue);

                yield return null; // Wait for the next frame
            }

            // Hard set the final value to ensure mathematical precision at the end
            dissolveMaterial.SetFloat("_DissolveAmount", endValue);
            Destroy(gameObject);
        }
    }
}