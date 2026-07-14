using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Client;

internal interface IClientRenderer : IPlanContributor
{
    bool Supports(ClientEngine engine);
}
