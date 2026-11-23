using UnityEngine;

namespace PaleFernStudio.Looksmith.Demo
{
    /// <summary>
    /// Y on the showcase panel opens Tweaks over the game (F9 does too): the knobs of every Looksmith module in the scene,
    /// changed live, with Save. Saved changes are written to the assets when Play Mode ends; the rest goes back. Editor
    /// Play Mode only: in a build there's nothing to save into, so the key isn't listed.
    /// </summary>
    [AddComponentMenu("Looksmith/Demo/Demo Tune")]
    public sealed class DemoTune : MonoBehaviour
    {
        DemoShowcase.Control m_Control;
        bool m_Listed;

        void OnEnable()
        {
            m_Control = new DemoShowcase.Control
            {
                keys = new[] { DemoKey.Tune },
                label = "Tweaks (change any setting)",
                status = () => LooksmithTweaksHooks.overlayOpen ? "open" : null,
                onPress = _ => LooksmithTweaksHooks.ToggleOverlay(),
            };
        }

        void OnDisable()
        {
            DemoShowcase.Unregister(m_Control);
            m_Listed = false;
        }

        void Update()
        {
            // the overlay comes up just after the scene starts playing, and never in a build
            bool available = Application.isPlaying && LooksmithTweaksHooks.overlayAvailable;
            if (available == m_Listed)
                return;
            m_Listed = available;
            if (available)
                DemoShowcase.Register(m_Control);
            else
                DemoShowcase.Unregister(m_Control);
        }
    }
}
