#nullable enable

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Unity.MMO.Client.Tests
{
    public sealed class WorldSceneTests
    {
        private const string WorldScenePath = "Assets/Scenes/World.unity";

        [Test]
        public void WorldSceneIsPlayableAndIncludedInBuild()
        {
            var scene = EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Single);

            Assert.That(scene.IsValid(), Is.True);
            Assert.That(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MmoGame>(true)).SingleOrDefault(), Is.Not.Null);

            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).SingleOrDefault();
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera!.orthographic, Is.False);
            Assert.That(camera.transform.position.y, Is.GreaterThan(10f));

            Assert.That(GameObject.Find("Greenfield Zone Preview"), Is.Not.Null);
            Assert.That(GameObject.Find("World Background"), Is.Not.Null);
            Assert.That(GameObject.Find("North Boundary"), Is.Not.Null);
            Assert.That(GameObject.Find("South Boundary"), Is.Not.Null);
            Assert.That(GameObject.Find("East Boundary"), Is.Not.Null);
            Assert.That(GameObject.Find("West Boundary"), Is.Not.Null);

            var background = GameObject.Find("World Background");
            Assert.That(background.transform.localScale.x * 10f, Is.GreaterThanOrEqualTo(Shared.Interfaces.WorldProtocol.WorldHalfExtent * 2f));
            Assert.That(background.transform.localScale.z * 10f, Is.GreaterThanOrEqualTo(Shared.Interfaces.WorldProtocol.WorldHalfExtent * 2f));

            var buildScene = EditorBuildSettings.scenes.SingleOrDefault(candidate => candidate.path == WorldScenePath);
            Assert.That(buildScene, Is.Not.Null);
            Assert.That(buildScene!.enabled, Is.True);
        }

        [Test]
        public void LogicPlaneMapsToTheThreeDimensionalGroundPlane()
        {
            var world = MmoGame.LogicToWorld(12f, -7f);

            Assert.That(world.x, Is.EqualTo(12f));
            Assert.That(world.y, Is.GreaterThan(0f));
            Assert.That(world.z, Is.EqualTo(-7f));
        }
    }
}
