#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class AutoOpenConnectionScene
{
    private const string InitKeyPrefix = "Lakona.Game.Unity.Agar.DefaultSceneInitialized";
    private const string ScenePath = "Assets/Scenes/Gameplay.unity";

    static AutoOpenConnectionScene()
    {
        EditorApplication.delayCall += TryOpenScene;
    }

    private static void TryOpenScene()
    {
        if (Application.isBatchMode)
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var initKey = $"{InitKeyPrefix}:{Application.dataPath}";
        if (EditorPrefs.HasKey(initKey))
            return;

        if (!System.IO.File.Exists(ScenePath))
        {
            Debug.LogWarning($"[Lakona.Tool] Missing default scene: {ScenePath}");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log($"[Lakona.Tool] Opened default scene: {ScenePath}");
        }

        EditorPrefs.SetBool(initKey, true);
    }
}
#endif
