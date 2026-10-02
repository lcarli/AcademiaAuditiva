namespace AcademiaAuditiva.Models;

/// <summary>Asks for the answer of a free practice round (see <c>PlayRequestDto.Free</c>).</summary>
public class RevealAnswerDto
{
    public int ExerciseId { get; set; }

    public string? RoundId { get; set; }
}
