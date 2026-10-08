namespace Lakona.Game.Client
{
    /// <summary>A locally observed Game connection transition, independent of session phase.</summary>
    public readonly struct LakonaGameConnectionStateChange
    {
        public LakonaGameConnectionStateChange(LakonaGameConnectionState previousState, LakonaGameConnectionState currentState)
        {
            PreviousState = previousState;
            CurrentState = currentState;
        }

        public LakonaGameConnectionState PreviousState { get; }
        public LakonaGameConnectionState CurrentState { get; }
    }
}
