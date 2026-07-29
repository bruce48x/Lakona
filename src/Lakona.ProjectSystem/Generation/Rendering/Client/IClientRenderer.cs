using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Rendering.Client;

internal interface IClientRenderer : IPlanContributor
{
    bool Supports(ClientEngine engine);
}
