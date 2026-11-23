using System;
using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    public static class LooksmithFind
    {
        public static T[] All<T>(FindObjectsInactive inactive = FindObjectsInactive.Exclude) where T : UnityEngine.Object
        {
#if UNITY_6000_6_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(inactive);
#else
            return UnityEngine.Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
#endif
        }

        public static UnityEngine.Object[] All(Type type, FindObjectsInactive inactive = FindObjectsInactive.Exclude)
        {
#if UNITY_6000_6_OR_NEWER
            return UnityEngine.Object.FindObjectsByType(type, inactive);
#else
            return UnityEngine.Object.FindObjectsByType(type, inactive, FindObjectsSortMode.None);
#endif
        }
    }
}
