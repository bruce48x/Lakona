#nullable enable

using Shared.Gameplay;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal static class DotArenaTuning
    {
        public const int WindowWidth = 1200;
        public const int WindowHeight = 600;
        public const float ArenaVisualPadding = 1.8f;
        public const float FollowCameraSize = 11.8f;
        public const float MaxFollowCameraSize = 22f;
        public const float FollowCameraRadiusMultiplier = 3.6f;
        public const float CameraFollowSharpness = 7f;
        public const float CameraZoomSharpness = 5f;
        public const float InvincibleSinglePlayerInitialMass = 1000f;
        public const float PickupPulseAmplitude = 0.08f;
        public const float PickupPulseFrequency = 3.2f;
        public const float PickupAbsorbDurationSeconds = 0.26f;
        public const string JellyShaderName = "SampleClient/PlayerJelly";
        public const string PickupAbsorbShaderName = "SampleClient/PickupAbsorb";
        public const int PlayerSortingOrder = 20;
        public const int PickupSortingOrder = 12;
        public const int PickupLabelSortingOrder = 14;
        public const float PlayerTextDepth = -0.2f;
        public const float PickupLabelScale = 0.45f;
        public const float InputSendIntervalSeconds = 0.05f;
        public const float SinglePlayerTickSeconds = 0.05f;
        public const int MaxSinglePlayerCatchUpTicks = 4;
        public const float InterpolationDurationSeconds = 0.1f;
        public const string TmpFallbackFontAssetResourcePath = "Fonts & Materials/LiberationSans SDF";

        public static readonly Color BackgroundColor = new(0.9f, 0.98f, 1f, 1f);
        public static readonly Color BoardColor = new(0.82f, 0.96f, 0.96f, 1f);
        public static readonly Color SafeZoneColor = new(0.94f, 1f, 0.98f, 0.92f);
        public static readonly Color GridColor = new(0.14f, 0.56f, 0.62f, 0.13f);
        public static readonly Color BorderColor = new(1f, 0.55f, 0.36f, 0.52f);
        public static readonly Color DangerColor = new(1f, 0.41f, 0.37f, 0.12f);
        public static readonly Color PlayerOutlineColor = new(1f, 1f, 1f, 0.92f);
        public static readonly Color UiPanelBackgroundColor = new(0.98f, 1f, 1f, 0.88f);
        public static readonly Color UiInputBackgroundColor = new(0.92f, 0.98f, 1f, 0.96f);
        public static readonly Color UiPrimaryTextColor = new(0.05f, 0.13f, 0.19f, 1f);
        public static readonly Color UiSecondaryTextColor = new(0.15f, 0.31f, 0.39f, 1f);
        public static readonly Color UiMutedTextColor = new(0.39f, 0.55f, 0.61f, 1f);
        public static readonly Color UiAccentTextColor = new(0f, 0.6f, 0.64f, 1f);
        public static readonly Color MassPickupColor = new(0.22f, 0.9f, 1f, 0.95f);
        public static readonly Color SpeedPickupColor = new(1f, 0.86f, 0.22f, 0.95f);
        public static readonly Color KnockbackPickupColor = new(1f, 0.22f, 0.22f, 0.95f);
        public static readonly ArenaConfig GameplayConfig = ArenaConfig.CreateDefault();

        public static readonly Color[] LocalDefaultPlayerPalette =
        {
            new(0.3f, 0.78f, 0.96f, 1f),
            new(1f, 0.58f, 0.18f, 1f),
            new(0.2f, 0.96f, 0.55f, 1f)
        };

        public static readonly Color[] RemotePalette =
        {
            new(0.2f, 0.96f, 0.67f, 1f),
            new(1f, 0.42f, 0.48f, 1f),
            new(1f, 0.74f, 0.18f, 1f),
            new(0.33f, 0.76f, 1f, 1f),
            new(0.88f, 0.49f, 1f, 1f),
            new(1f, 0.61f, 0.3f, 1f)
        };
    }
}
