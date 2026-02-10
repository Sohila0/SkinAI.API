using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using SkinAI.API.Enums;

namespace SkinAI.API.Models
{
    public class DiseaseCase
    {
        public int Id { get; set; }

        public string ImagePath { get; set; } = null!;

        // ===== AI Result =====
        public string AiDiagnosis { get; set; } = null!;
        public double Confidence { get; set; }

        // الأعراض أو ملاحظات المريض
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ===== Owner Patient =====
        public int PatientId { get; set; }

        [ForeignKey(nameof(PatientId))]
        public Patient Patient { get; set; } = null!;

        // ===== Case status through lifecycle =====
        public CaseStatus Status { get; set; } = CaseStatus.AI_DONE;

        // ===== Linked consultations (لو المريض طلب دكتور للحالة دي) =====
        public ICollection<Consultation> Consultations { get; set; } = new List<Consultation>();
    }
}
