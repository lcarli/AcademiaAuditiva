public class ExerciseSessionData
{
    public string ExpectedAnswer { get; set; }

    /// <summary>Free practice round (see <c>PlayRequestDto.Free</c>).</summary>
    public bool Free { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}