#nullable enable

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Shared.Interfaces;
using UnityEngine;
using UnityEngine.TestTools;
using NUnitAssert = NUnit.Framework.Assert;

namespace Game.Unity.MMO.Client.Tests
{
    public sealed class MmoPresentationTests
    {
        [UnityTest]
        public IEnumerator SnapshotCreatesSwordSelectsTargetAndMovesPerspectiveCamera()
        {
            var game = Object.FindObjectOfType<MmoGame>();
            if (game == null) game = new GameObject("MMO Presentation Test").AddComponent<MmoGame>();

            typeof(MmoGame).GetField("_characterId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(game, "hero-test");
            game.OnWorldSnapshot(new WorldSnapshot
            {
                ZoneId = WorldProtocol.DefaultZoneId,
                WorldHalfExtent = WorldProtocol.WorldHalfExtent,
                Entities =
                {
                    Entity("hero-test", "Test Hero", EntityKind.Character, 12f, -7f),
                    Entity("monster-test", "Test Monster", EntityKind.Monster, 14f, -7f)
                }
            });

            yield return null;
            yield return new WaitForSeconds(0.75f);

            var hero = GameObject.Find("Character: Test Hero");
            NUnitAssert.That(hero, Is.Not.Null);
            NUnitAssert.That(hero!.transform.Find("Sword Pivot/Auto Attack Sword"), Is.Not.Null);
            NUnitAssert.That(GameObject.Find("Monster: Test Monster"), Is.Not.Null);

            var selectedTarget = typeof(MmoGame)
                .GetMethod("FindNearestMonsterInRange", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(game, null);
            NUnitAssert.That(selectedTarget, Is.EqualTo("monster-test"));

            var camera = Camera.main;
            NUnitAssert.That(camera, Is.Not.Null);
            NUnitAssert.That(camera!.orthographic, Is.False);
            NUnitAssert.That(camera.transform.position.x, Is.EqualTo(12f).Within(1f));
            NUnitAssert.That(camera.transform.position.z, Is.EqualTo(-20f).Within(1f));
        }

        private static EntityState Entity(string id, string name, EntityKind kind, float x, float y)
        {
            return new EntityState
            {
                EntityId = id,
                Name = name,
                Kind = kind,
                X = x,
                Y = y,
                FacingX = 1f,
                Health = 100,
                MaxHealth = 100,
                Alive = true
            };
        }
    }
}
