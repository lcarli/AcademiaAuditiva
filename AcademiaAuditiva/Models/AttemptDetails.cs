public class AttemptDetails
{
    public object UserAnswer { get; set; }
    public object ExpectedAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public int TimeSpentSeconds { get; set; }
    public Dictionary<string, string> Filters { get; set; }
}
