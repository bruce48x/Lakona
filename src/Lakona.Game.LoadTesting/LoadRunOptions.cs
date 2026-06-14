namespace Lakona.Game.LoadTesting;

public sealed record LoadRunOptions
{
    public LoadRunOptions(int Users, TimeSpan RampUp, TimeSpan Duration)
    {
        if (Users <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Users), Users, "Users must be greater than zero.");
        }

        if (RampUp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RampUp), RampUp, "RampUp must be zero or positive.");
        }

        if (Duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Duration), Duration, "Duration must be greater than zero.");
        }

        this.Users = Users;
        this.RampUp = RampUp;
        this.Duration = Duration;
    }

    public int Users { get; }

    public TimeSpan RampUp { get; }

    public TimeSpan Duration { get; }
}
