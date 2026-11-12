using UnityEditor;
using System.IO;
using UnityEngine;

[InitializeOnLoad]
public static class PurgeObsoleteRes
{
    [InitializeOnLoadMethod]
    static void CleanRes()
    {
        string obsoleteResPath = Path.Combine(Application.dataPath, "Plugins/Android/res");
        string obsoleteResMeta = obsoleteResPath + ".meta";
        bool changed = false;

        if (Directory.Exists(obsoleteResPath))
        {
            try
            {
                Directory.Delete(obsoleteResPath, true);
                changed = true;
                Debug.Log("[CleanRes] Deleted obsolete folder: Assets/Plugins/Android/res");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[CleanRes] Error deleting res folder: " + ex.Message);
            }
        }

        if (File.Exists(obsoleteResMeta))
        {
            try
            {
                File.Delete(obsoleteResMeta);
                changed = true;
                Debug.Log("[CleanRes] Deleted obsolete meta: Assets/Plugins/Android/res.meta");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[CleanRes] Error deleting res.meta: " + ex.Message);
            }
        }

        if (changed)
        {
            AssetDatabase.Refresh();
        }
    }
}
