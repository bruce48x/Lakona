using Server.App.Chat;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Features
{
    [HotfixFeature("chat")]
    public sealed class ChatFeature : HotfixGameFeature
    {
        public override void Configure(HotfixFeatureContext context)
        {
            context.EnsureLocalActor<ChatRoomActor>("global");
        }
    }
}
