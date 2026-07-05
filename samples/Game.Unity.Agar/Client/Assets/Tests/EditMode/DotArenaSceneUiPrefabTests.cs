#nullable enable

using System.IO;
using System.Linq;
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
        private const string RuntimeFontResourcePath = "Fonts & Materials/LiberationSans SDF";
        private const string TmpFontMaterialsPath = "Assets/TextMesh Pro/Resources/Fonts & Materials";
        private const string LegacyTmpFontsPath = "Assets/TextMesh Pro/Fonts";
        private static readonly string[] ImageAssetExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
            ".tga",
            ".psd",
            ".bmp",
            ".gif",
            ".tif",
            ".tiff",
            ".exr",
            ".hdr",
            ".svg"
        };

        [Test]
        public void SceneUiPrefabContainsStablePresenterPaths()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertPath(prefab!, "MenuBackground");
            AssertPath(prefab!, "OverlayLayer");
            AssertPath(prefab!, "OverlayLayer/TopStatusPanel");
            AssertPath(prefab!, "OverlayLayer/TopStatusPanel/StatusText");
            AssertPath(prefab!, "OverlayLayer/TopStatusPanel/CountdownText");
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

        [Test]
        public void SceneUiPrefabMatchesRuntimeEntryLayoutAndTheme()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertRect(prefab!, "EntryPanel", new Vector2(0f, 0f), new Vector2(1050f, 430f));
            AssertImageColor(prefab!, "EntryPanel", new Color(0.98f, 1f, 1f, 0.88f));
            AssertGradientBackground(prefab!, "MenuBackground");
            AssertChildRectInsideParent(prefab!, "EntryPanel", "EntryPanel/TitleText");
            AssertChildRectInsideParent(prefab!, "EntryPanel", "EntryPanel/StatusText");
            AssertImageColor(prefab!, "EntryPanel/ModeSelectPanel", Color.clear);
            AssertRect(prefab!, "EntryPanel/ModeSelectPanel/SinglePlayerButton", new Vector2(250f, -82f), new Vector2(360f, 54f));
            AssertRect(prefab!, "EntryPanel/ModeSelectPanel/InvincibleSinglePlayerButton", new Vector2(250f, -154f), new Vector2(360f, 54f));
            AssertRect(prefab!, "EntryPanel/ModeSelectPanel/MultiplayerButton", new Vector2(250f, -226f), new Vector2(360f, 54f));
            AssertGradientButton(prefab!, "EntryPanel/ModeSelectPanel/SinglePlayerButton");
            AssertGradientButton(prefab!, "EntryPanel/ModeSelectPanel/InvincibleSinglePlayerButton");
            AssertGradientButton(prefab!, "EntryPanel/ModeSelectPanel/MultiplayerButton");
            AssertText(prefab!, "EntryPanel/ModeSelectPanel/SinglePlayerButton/Label", "Single Player: Normal");
            AssertText(prefab!, "EntryPanel/ModeSelectPanel/InvincibleSinglePlayerButton/Label", "Single Player: Invincible");
            AssertText(prefab!, "EntryPanel/ModeSelectPanel/MultiplayerButton/Label", "Multiplayer");
            AssertRect(prefab!, "EntryPanel/MultiplayerPanel", new Vector2(260f, -80f), new Vector2(360f, 242f));
            AssertImageColor(prefab!, "EntryPanel/MultiplayerPanel", new Color(0.98f, 1f, 1f, 0.88f));
            AssertGradientButton(prefab!, "EntryPanel/MultiplayerPanel/MatchButton");
            AssertGradientButton(prefab!, "EntryPanel/MultiplayerPanel/GuestLoginButton");
            AssertGradientButton(prefab!, "EntryPanel/MultiplayerPanel/BackButton");
            AssertText(prefab!, "EntryPanel/MultiplayerPanel/AccountLabel", "Account");
            AssertText(prefab!, "EntryPanel/MultiplayerPanel/PasswordLabel", "Password");
        }

        [Test]
        public void SceneUiPrefabButtonsUseEditorVisibleRoundedGradients()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            foreach (var button in prefab!.GetComponentsInChildren<Button>(true))
            {
                AssertGradientButton(prefab, GetTransformPathRelativeToRoot(prefab.transform, button.transform));
            }
        }

        [Test]
        public void SceneUiPrefabTopStatusTextsShareOneNonOverlappingOverlayRegion()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertRect(prefab!, "OverlayLayer/TopStatusPanel", new Vector2(0f, -12f), new Vector2(360f, 58f));
            Assert.That(prefab!.transform.Find("HUDPanel/StatusText"), Is.Null, "HUD status should be owned by the shared overlay status region");
            AssertNoSiblingRectOverlap(prefab, "OverlayLayer/TopStatusPanel", "StatusText", "CountdownText");
        }

        [Test]
        public void ProjectTmpFontIsLoadableForRuntimeCreatedText()
        {
            var font = Resources.Load<TMP_FontAsset>(RuntimeFontResourcePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(font, Is.Not.Null);
            Assert.That(prefab, Is.Not.Null);

            foreach (var text in prefab!.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.font, Is.SameAs(font), GetTransformPath(text.transform));
            }
        }

        [Test]
        public void TextMeshProSpriteAssetsAreDisabled()
        {
            var settings = TMP_Settings.instance;

            Assert.That(settings, Is.Not.Null);
            Assert.That(TMP_Settings.defaultSpriteAsset, Is.Null);
        }

        [Test]
        public void ClientUsesOnlyEnglishFonts()
        {
            var tmpFontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { TmpFontMaterialsPath });
            var legacyFontGuids = AssetDatabase.FindAssets("t:Font", new[] { LegacyTmpFontsPath });

            foreach (var guid in tmpFontGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Assert.That(assetPath, Does.Contain("LiberationSans"), assetPath);
            }

            foreach (var guid in legacyFontGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Assert.That(assetPath, Does.Contain("LiberationSans"), assetPath);
            }
        }

        [Test]
        public void ClientAssetsDoNotContainImageFiles()
        {
            var rootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var imageFiles = Directory.EnumerateFiles(Application.dataPath, "*", SearchOption.AllDirectories)
                .Where(path => ImageAssetExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .Select(path => Path.GetRelativePath(rootPath, path).Replace('\\', '/'))
                .OrderBy(path => path)
                .ToArray();

            Assert.That(imageFiles, Is.Empty, string.Join("\n", imageFiles));
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

        private static void AssertRect(GameObject root, string path, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var rect = target!.GetComponent<RectTransform>();
            Assert.That(rect, Is.Not.Null, path);
            Assert.That(rect!.anchoredPosition.x, Is.EqualTo(anchoredPosition.x).Within(0.01f), $"{path} x");
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(anchoredPosition.y).Within(0.01f), $"{path} y");
            Assert.That(rect.sizeDelta.x, Is.EqualTo(sizeDelta.x).Within(0.01f), $"{path} width");
            Assert.That(rect.sizeDelta.y, Is.EqualTo(sizeDelta.y).Within(0.01f), $"{path} height");
        }

        private static void AssertImageColor(GameObject root, string path, Color expected)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var image = target!.GetComponent<Image>();
            Assert.That(image, Is.Not.Null, path);
            Assert.That(image!.color.r, Is.EqualTo(expected.r).Within(0.001f), $"{path} r");
            Assert.That(image.color.g, Is.EqualTo(expected.g).Within(0.001f), $"{path} g");
            Assert.That(image.color.b, Is.EqualTo(expected.b).Within(0.001f), $"{path} b");
            Assert.That(image.color.a, Is.EqualTo(expected.a).Within(0.001f), $"{path} a");
        }

        private static void AssertGradientBackground(GameObject root, string path)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var gradient = target!.GetComponent<DotArenaGradientGraphic>();
            Assert.That(gradient, Is.Not.Null, path);
            AssertColor(gradient!.TopLeft, new Color(0.88f, 0.98f, 1f, 1f), $"{path} top left");
            AssertColor(gradient.TopRight, new Color(0.98f, 1f, 0.97f, 1f), $"{path} top right");
            AssertColor(gradient.BottomLeft, new Color(0.78f, 0.95f, 1f, 1f), $"{path} bottom left");
            AssertColor(gradient.BottomRight, new Color(1f, 0.91f, 0.86f, 1f), $"{path} bottom right");
            Assert.That(target.GetComponent<Image>(), Is.Null, $"{path} should use an editor-visible gradient graphic instead of Image");
        }

        private static void AssertGradientButton(GameObject root, string path)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var gradient = target!.GetComponent<DotArenaGradientGraphic>();
            Assert.That(gradient, Is.Not.Null, path);
            Assert.That(target.GetComponent<Image>(), Is.Null, $"{path} should use an editor-visible rounded gradient graphic instead of Image");

            var serialized = new SerializedObject(gradient!);
            var cornerRadius = serialized.FindProperty("_cornerRadius");
            Assert.That(cornerRadius, Is.Not.Null, $"{path} corner radius");
            Assert.That(cornerRadius!.floatValue, Is.GreaterThan(0f), $"{path} corner radius");

            var button = target.GetComponent<Button>();
            Assert.That(button, Is.Not.Null, path);
            Assert.That(button!.targetGraphic, Is.SameAs(gradient), $"{path} targetGraphic");
        }

        private static void AssertColor(Color actual, Color expected, string name)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f), $"{name} r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f), $"{name} g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f), $"{name} b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f), $"{name} a");
        }

        private static void AssertChildRectInsideParent(GameObject root, string parentPath, string childPath)
        {
            var parentTransform = root.transform.Find(parentPath);
            var childTransform = root.transform.Find(childPath);

            Assert.That(parentTransform, Is.Not.Null, parentPath);
            Assert.That(childTransform, Is.Not.Null, childPath);

            var parent = parentTransform!.GetComponent<RectTransform>();
            var child = childTransform!.GetComponent<RectTransform>();
            Assert.That(parent, Is.Not.Null, parentPath);
            Assert.That(child, Is.Not.Null, childPath);

            var parentRect = parent!.rect;
            var childRect = GetRectInParentSpace(parent, child!);
            const float tolerance = 0.01f;
            Assert.That(childRect.xMin + tolerance, Is.GreaterThanOrEqualTo(parentRect.xMin), $"{childPath} left");
            Assert.That(childRect.xMax - tolerance, Is.LessThanOrEqualTo(parentRect.xMax), $"{childPath} right");
            Assert.That(childRect.yMin + tolerance, Is.GreaterThanOrEqualTo(parentRect.yMin), $"{childPath} bottom");
            Assert.That(childRect.yMax - tolerance, Is.LessThanOrEqualTo(parentRect.yMax), $"{childPath} top");
        }

        private static Rect GetRectInParentSpace(RectTransform parent, RectTransform child)
        {
            var parentRect = parent.rect;
            var parentAnchorPoint = new Vector2(
                parentRect.xMin + (child.anchorMin.x * parentRect.width),
                parentRect.yMin + (child.anchorMin.y * parentRect.height));
            var childMin = parentAnchorPoint + child.anchoredPosition - Vector2.Scale(child.pivot, child.sizeDelta);
            var childMax = childMin + child.sizeDelta;
            return Rect.MinMaxRect(childMin.x, childMin.y, childMax.x, childMax.y);
        }

        private static void AssertNoSiblingRectOverlap(GameObject root, string parentPath, string firstName, string secondName)
        {
            var parentTransform = root.transform.Find(parentPath);

            Assert.That(parentTransform, Is.Not.Null, parentPath);
            var parent = parentTransform!.GetComponent<RectTransform>();
            var first = parentTransform.Find(firstName)?.GetComponent<RectTransform>();
            var second = parentTransform.Find(secondName)?.GetComponent<RectTransform>();
            Assert.That(parent, Is.Not.Null, parentPath);
            Assert.That(first, Is.Not.Null, $"{parentPath}/{firstName}");
            Assert.That(second, Is.Not.Null, $"{parentPath}/{secondName}");

            var firstRect = GetRectInParentSpace(parent!, first!);
            var secondRect = GetRectInParentSpace(parent!, second!);
            Assert.That(firstRect.Overlaps(secondRect), Is.False, $"{parentPath}/{firstName} overlaps {secondName}");
        }

        private static void AssertText(GameObject root, string path, string expected)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var text = target!.GetComponent<TMP_Text>();
            Assert.That(text, Is.Not.Null, path);
            Assert.That(text!.text, Is.EqualTo(expected), path);
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

        private static string GetTransformPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private static string GetTransformPathRelativeToRoot(Transform root, Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null && transform.parent != root)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
