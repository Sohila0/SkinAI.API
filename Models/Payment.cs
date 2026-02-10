using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SkinAI.API.Enums;

namespace SkinAI.API.Models
{
    public class Payment
    {
        [Key]
        public int Id { get; set; }

        // ===== Link to consultation =====
        public int ConsultationId { get; set; }

        [ForeignKey(nameof(ConsultationId))]
        public Consultation Consultation { get; set; } = null!;

        // ===== Who paid / who received (مهم للتقارير والصلاحيات) =====
        public int PatientId { get; set; }

        [ForeignKey(nameof(PatientId))]
        public Patient Patient { get; set; } = null!;

        public int DoctorId { get; set; }

        [ForeignKey(nameof(DoctorId))]
        public Doctor Doctor { get; set; } = null!;

        // ===== Money =====
        public decimal Amount { get; set; }

        // ===== Payment simulation info =====
        public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

        // بدل bool IsSuccessful الأفضل Status enum
        public PaymentStatus Status { get; set; } = PaymentStatus.PENDING;

        // لو حابة تفضلي داعمة للكود القديم اللي بيقرأ IsSuccessful
        [NotMapped]
        public bool IsSuccessful => Status == PaymentStatus.SUCCESS;

        // Provider simulated
        public string Provider { get; set; } = "SIMULATED";

        // رقم إيصال/مرجع (مفيد للعرض في UI)
        public string? ReferenceNo { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum PaymentMethod
    {
        Cash,
        Card,
        Wallet
    }
}
