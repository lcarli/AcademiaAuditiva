public class ExerciseAttemptLog
{
    public string UserId { get; set; }
    public string Exercise { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string QuestionId { get; set; }
    public AttemptDetails Attempt { get; set; }
}
