using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Models;

namespace SkinAI.API.Data
{
    public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<int>, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<DiseaseCase> DiseaseCases => Set<DiseaseCase>();
        public DbSet<Consultation> Consultations => Set<Consultation>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<DoctorReview> DoctorReviews => Set<DoctorReview>();
        public DbSet<ConsultationMessage> ConsultationMessages => Set<ConsultationMessage>();
        public DbSet<ConsultationOffer> ConsultationOffers => Set<ConsultationOffer>();

        // ✅ OneSignal tokens
        public DbSet<UserPushToken> UserPushTokens => Set<UserPushToken>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ===================== 1:1 User <-> Doctor =====================
            builder.Entity<Doctor>()
                .HasOne(d => d.User)
                .WithOne(u => u.Doctor)
                .HasForeignKey<Doctor>(d => d.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===================== 1:1 User <-> Patient =====================
            builder.Entity<Patient>()
                .HasOne(p => p.User)
                .WithOne(u => u.Patient)
                .HasForeignKey<Patient>(p => p.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===================== DiseaseCase -> Patient =====================
            builder.Entity<DiseaseCase>()
                .HasOne(dc => dc.Patient)
                .WithMany()
                .HasForeignKey(dc => dc.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            // ===================== Consultation -> Patient =====================
            builder.Entity<Consultation>()
                .HasOne(c => c.Patient)
                .WithMany()
                .HasForeignKey(c => c.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            // ===================== Consultation -> Doctor (optional) =====================
            builder.Entity<Consultation>()
                .HasOne(c => c.Doctor)
                .WithMany()
                .HasForeignKey(c => c.DoctorId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // ===================== Consultation -> DiseaseCase =====================
            builder.Entity<Consultation>()
                .HasOne(c => c.DiseaseCase)
                .WithMany(dc => dc.Consultations)
                .HasForeignKey(c => c.DiseaseCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            // ===================== Payment relations =====================
            builder.Entity<Payment>()
                .HasOne(p => p.Consultation)
                .WithMany()
                .HasForeignKey(p => p.ConsultationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Payment>()
                .HasOne(p => p.Patient)
                .WithMany()
                .HasForeignKey(p => p.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Payment>()
                .HasOne(p => p.Doctor)
                .WithMany()
                .HasForeignKey(p => p.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            // ===================== Notification -> User =====================
            builder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===================== ConsultationMessage -> Sender =====================
            builder.Entity<ConsultationMessage>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            // ===================== ConsultationOffer relations =====================
            builder.Entity<ConsultationOffer>()
                .HasOne(o => o.Consultation)
                .WithMany()
                .HasForeignKey(o => o.ConsultationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ConsultationOffer>()
                .HasOne(o => o.Doctor)
                .WithMany()
                .HasForeignKey(o => o.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ConsultationOffer>()
                .Property(o => o.Price)
                .HasPrecision(18, 2);

            // ===================== Decimal precision =====================
            builder.Entity<Payment>().Property(p => p.Amount).HasPrecision(18, 2);
            builder.Entity<Consultation>().Property(c => c.Price).HasPrecision(18, 2);
            builder.Entity<Doctor>().Property(d => d.ConsultationPrice).HasPrecision(18, 2);

            // ===================== Unique medical license =====================
            builder.Entity<Doctor>()
                .HasIndex(d => d.MedicalLicenseNumber)
                .IsUnique();

            // ===================== ✅ OneSignal Push Tokens =====================
            // 1) relation: token belongs to a user
            builder.Entity<UserPushToken>()
                .HasOne(t => t.User)
                .WithMany() // أو WithMany(u => u.PushTokens) لو هتزودي nav property في User
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 2) avoid duplicates: same user same playerId
            // ===================== ✅ OneSignal Push Tokens =====================

            // العلاقة: التوكن تابع ليوزر
            builder.Entity<UserPushToken>()
                .HasOne(t => t.User)
                .WithMany() // أو WithMany(u => u.PushTokens) لو هتزوديها في User
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // منع التكرار: نفس اليوزر + نفس الـ OneSignalPlayerId
            builder.Entity<UserPushToken>()
                .HasIndex(t => new { t.UserId, t.OneSignalPlayerId })
                .IsUnique();

            // تسريع الاستعلامات
            builder.Entity<UserPushToken>()
                .HasIndex(t => t.UserId);


            // 3) query speed: by UserId
            builder.Entity<UserPushToken>()
                .HasIndex(t => t.UserId);
        }
    }
}
