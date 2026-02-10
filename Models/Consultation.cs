using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SkinAI.API.Enums;

namespace SkinAI.API.Models
{
    public class Consultation
    {
        [Key]
        public int Id { get; set; }

        // ===== Owner patient =====
        public int PatientId { get; set; }

        [ForeignKey(nameof(PatientId))]
        public Patient Patient { get; set; } = null!;

        // ===== Selected doctor AFTER patient chooses offer =====
        // كان عندك int DoctorId -> لازم يبقى nullable لأن في الأول الطلب OPEN لكل الأطباء
        public int? DoctorId { get; set; }

        [ForeignKey(nameof(DoctorId))]
        public Doctor? Doctor { get; set; }

        // ===== Link to AI case =====
        // لازم نربط الاستشارة بالحالة اللي اتعمل لها AI diagnosis
        public int DiseaseCaseId { get; set; }

        [ForeignKey(nameof(DiseaseCaseId))]
        public DiseaseCase DiseaseCase { get; set; } = null!;

        // ===== Status for the whole flow =====
        public ConsultationStatus Status { get; set; } = ConsultationStatus.OPEN;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ===== Price after offer is selected =====
        // كان عندك Price decimal -> خليه nullable لأن السعر بيتحدد بعد اختيار العرض
        public decimal? Price { get; set; }

        // بدل IsPaid -> استخدمي Status (PAID) لكن هنسيبه لتوافق الكود الحالي
        public bool IsPaid { get; set; } = false;

        // ملاحظات المريض عند الطلب (أعراض/تفاصيل)
        public string? Notes { get; set; }

        // ===== Offer selected (اختياري - هنستخدمه لما نعمل DoctorOffer) =====
        public int? SelectedOfferId { get; set; }

        // ===== Final diagnosis by doctor =====
        public string? FinalDiagnosis { get; set; }
        public string? DoctorFinalNotes { get; set; }
        public DateTime? ClosedAt { get; set; }
        

    }
}
