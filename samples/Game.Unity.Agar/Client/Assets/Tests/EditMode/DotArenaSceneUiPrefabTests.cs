#nullable enable

using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SampleClient.Gameplay.Tests
{
    public sealed class DotArenaSceneUiPrefabTests
    {
        private const string SceneUiPrefabPath = "Assets/Prefabs/UI/SceneUI.prefab";

        [Test]
        public void SceneUiPrefabContainsStablePresenterPaths()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertPath(prefab!, "MenuBackground");
            AssertPath(prefab!, "OverlayLayer");
            AssertPath(prefab!, "OverlayLayer/CountdownText");
            AssertPath(prefab!, "HUDPanel");
            AssertPath(prefab!, "MatchRankingPanel");
            AssertPath(prefab!, "MatchRankingPanel/TitleText");
            AssertPath(prefab!, "MatchRankingPanel/HeaderText");
            AssertPath(prefab!, "MatchRankingPanel/Row1/RankText");
            AssertPath(prefab!, "MatchRankingPanel/Row10/MassText");
            AssertPath(prefab!, "DebugPanel");
            AssertPath(prefab!, "EntryPanel");
            AssertPath(prefab!, "EntryPanel/ModeSelectPanel/SinglePlayerButton/Label");
            AssertPath(prefab!, "EntryPanel/ModeSelectPanel/InvincibleSinglePlayerButton/Label");
            AssertPath(prefab!, "EntryPanel/ModeSelectPanel/MultiplayerButton/Label");
            AssertPath(prefab!, "EntryPanel/MultiplayerPanel/AccountInput");
            AssertPath(prefab!, "EntryPanel/MultiplayerPanel/PasswordInput");
            AssertPath(prefab!, "EntryPanel/MultiplayerPanel/MatchButton/Label");
            AssertPath(prefab!, "EntryPanel/MultiplayerPanel/GuestLoginButton/Label");
            AssertPath(prefab!, "EntryPanel/MultiplayerPanel/BackButton/Label");
            AssertPath(prefab!, "MatchmakingPanel/TitleText");
            AssertPath(prefab!, "MatchmakingPanel/DetailText");
            AssertPath(prefab!, "MatchmakingPanel/CancelButton/Label");
            AssertPath(prefab!, "LobbyPanel/TitleText");
            AssertPath(prefab!, "LobbyPanel/ProfileButton/Label");
            AssertPath(prefab!, "LobbyPanel/LeaderboardButton/Label");
            AssertPath(prefab!, "LobbyPanel/SettingsButton/Label");
            AssertPath(prefab!, "LobbyPanel/QuickActionButton1/Label");
            AssertPath(prefab!, "LobbyPanel/QuickActionButton4/Label");
            AssertPath(prefab!, "LobbyPanel/PrimaryActionButton/Label");
            AssertPath(prefab!, "LobbyPanel/SecondaryActionButton/Label");
            AssertPath(prefab!, "SettlementPanel/TitleText");
            AssertPath(prefab!, "SettlementPanel/PrimaryButton/Label");
            AssertPath(prefab!, "SettlementPanel/SecondaryButton/Label");
        }

        [Test]
        public void SceneUiPrefabContainsAuthoredInputShellsAndNoMissingScripts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(CountMissingScripts(prefab!), Is.Zero);
            AssertInputShell(prefab!, "EntryPanel/MultiplayerPanel/AccountInput");
            AssertInputShell(prefab!, "EntryPanel/MultiplayerPanel/PasswordInput");
        }

        private static void AssertPath(GameObject root, string path)
        {
            Assert.That(root.transform.Find(path), Is.Not.Null, path);
        }

        private static void AssertInputShell(GameObject root, string path)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            Assert.That(target!.GetComponent<Image>(), Is.Not.Null, path);
            Assert.That(target.Find("Text Area"), Is.Not.Null, $"{path}/Text Area");
            Assert.That(target.Find("Text Area/Placeholder")?.GetComponent<TMP_Text>(), Is.Not.Null, $"{path}/Text Area/Placeholder");
            Assert.That(target.Find("Text Area/Text")?.GetComponent<TMP_Text>(), Is.Not.Null, $"{path}/Text Area/Text");
        }

        private static int CountMissingScripts(GameObject root)
        {
            var count = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            }

            return count;
        }
    }
}
