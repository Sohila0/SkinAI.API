namespace SkinAI.API.Dtos.Consultations
{
    public class FinalizeConsultationDto
    {
        public string FinalDiagnosis { get; set; } = null!;
        public string? DoctorFinalNotes { get; set; }
    }
}
