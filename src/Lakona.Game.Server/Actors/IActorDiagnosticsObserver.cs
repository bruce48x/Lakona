namespace Lakona.Game.Server.Actors;

public interface IActorDiagnosticsObserver
{
    void OnDeadLetter(ActorDeadLetterDiagnostic diagnostic);

    void OnSlowMessage(ActorSlowMessageDiagnostic diagnostic);

    void OnCallTimeout(ActorCallTimeoutDiagnostic diagnostic);
}
