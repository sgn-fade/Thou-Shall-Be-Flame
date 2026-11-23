using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The runtime side of Tweaks (the panel itself is editor code, in Editor Play Mode only): the in-game overlay's
    /// switch, and the links that let a runtime copy stand in for an asset. A demo that shows a copy of a Look (so its own
    /// changes never touch the asset) registers the copy here: a tweak made on screen then lands on the asset itself, as a
    /// draft that Save keeps and leaving Play Mode otherwise drops.
    /// </summary>
    public static class LooksmithTweaksHooks
    {
        static readonly Dictionary<Object, Object> s_Sources = new Dictionary<Object, Object>();

        /// <summary>Set by the editor while playing; null in builds and outside Play Mode.</summary>
        public static Action toggleOverlay;

        /// <summary>True while the in-game Tweaks overlay is open (a game should leave the mouse alone then).</summary>
        public static bool overlayOpen;

        /// <summary>True while someone types a number into the overlay (a game should ignore its keys then).</summary>
        public static bool typing;

        /// <summary>True when the in-game Tweaks overlay can open (Editor Play Mode).</summary>
        public static bool overlayAvailable => toggleOverlay != null;

        public static void ToggleOverlay() => toggleOverlay?.Invoke();

        /// <summary>Declares <paramref name="copy"/> a runtime stand-in for <paramref name="source"/> (an asset).</summary>
        public static void RegisterStandIn(Object copy, Object source)
        {
            if (copy != null && source != null && copy != source)
                s_Sources[copy] = source;
        }

        public static void UnregisterStandIn(Object copy)
        {
            if (copy != null)
                s_Sources.Remove(copy);
        }

        /// <summary>The asset an object stands in for, or the object itself.</summary>
        public static Object SourceOf(Object o) => o != null && s_Sources.TryGetValue(o, out var s) && s != null ? s : o;

        /// <summary>The live stand-ins of an asset (what's on screen in its place).</summary>
        public static IEnumerable<Object> StandInsOf(Object source)
        {
            foreach (var kv in s_Sources)
                if (kv.Value == source && kv.Key != null)
                    yield return kv.Key;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // only the copies that are gone: a scene can register its copies before this runs when Play Mode starts
            foreach (var copy in s_Sources.Keys.Where(k => k == null).ToList())
                s_Sources.Remove(copy);
            overlayOpen = false;
            typing = false;
        }
    }

    /// <summary>Hosts the in-game Tweaks overlay (created by the editor in Play Mode, never saved, never in builds).</summary>
    [AddComponentMenu("")]
    public sealed class LooksmithTweaksOverlayHost : MonoBehaviour
    {
        /// <summary>Draws the overlay (set by the editor).</summary>
        public static Action onGUI;

        void OnGUI() => onGUI?.Invoke();
    }
}
