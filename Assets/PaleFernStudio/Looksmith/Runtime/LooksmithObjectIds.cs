using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    public static class LooksmithObjectIds
    {
        public static string Get(Object target)
        {
            if (target == null)
                return "";
#if UNITY_6000_3_OR_NEWER
            return target.GetEntityId().ToString();
#else
            return target.GetInstanceID().ToString();
#endif
        }

        public static int Hash(Object target)
        {
            if (target == null)
                return 0;
#if UNITY_6000_3_OR_NEWER
            return target.GetEntityId().GetHashCode();
#else
            return target.GetInstanceID();
#endif
        }
    }
}
