#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
internal static class AutoOpenConnectionScene
{
    private const string SessionStateKey = "Lakona.Tool.ConnectionSceneOpened";
    private const string ScenePath = "Assets/Scenes/ConnectionTest.unity";

    static AutoOpenConnectionScene()
    {
        EditorApplication.delayCall += TryOpenScene;
    }

    private static void TryOpenScene()
    {
        if (SessionState.GetBool(SessionStateKey, false))
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!System.IO.File.Exists(ScenePath))
            return;

        SessionState.SetBool(SessionStateKey, true);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
#endif
