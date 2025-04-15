namespace AcademiaAuditiva.Models
{
	public class ValidateGuessNoteDto
	{
		public int Id { get; set; }
		public string UserGuess { get; set; }
		public string ActualNote { get; set; }
		public int TimeSpentSeconds { get; set; }
	}
}
