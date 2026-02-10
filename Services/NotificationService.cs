using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Models;

namespace SkinAI.API.Services
{
    public interface INotificationService
    {
        Task NotifyAdminsOfNewDoctorAsync(int doctorId);
        Task NotifyDoctorApprovalAsync(int doctorId);
        Task NotifyDoctorRejectionAsync(int doctorId, string reason);

        Task NotifyPaymentSuccessAsync(int paymentId);

        // ✅ Flow notifications
        Task NotifyNewConsultationRequestAsync(int consultationId);
        Task NotifyNewOfferAsync(int offerId);
        Task NotifyOfferAcceptedAsync(int offerId);
        Task NotifyNewMessageAsync(int consultationId, int receiverUserId);
        Task NotifyDiagnosisCompletedAsync(int consultationId);

        Task CreateNotificationAsync(
            int userId,
            string title,
            string message,
            NotificationType type,
            int? relatedEntityId = null,
            string? relatedEntityType = null,
            string? actionUrl = null
        );
    }

    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPushSender _push;

        public NotificationService(ApplicationDbContext context, IPushSender push)
        {
            _context = context;
            _push = push;
        }

        // ===================== Doctors Registration Flow =====================

        public async Task NotifyAdminsOfNewDoctorAsync(int doctorId)
        {
            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.Id == doctorId);

            if (doctor == null) return;

            // الأفضل تعتمد على Roles (Identity) لكن بما إنك بتستخدم Role string هنمشي عليه
            var adminUsers = await _context.Users
                .Where(u => u.Role == "Admin")
                .ToListAsync();

            foreach (var admin in adminUsers)
            {
                await CreateNotificationAsync(
                    admin.Id,
                    "طلب تسجيل طبيب جديد",
                    $"تم تسجيل طبيب جديد: {doctor.User.FullName}  - {doctor.Specialization}",
                    NotificationType.AdminAlert,
                    relatedEntityId: doctor.Id,
                    relatedEntityType: "Doctor",
                    actionUrl: $"/admin/doctors/pending"
                );
            }
        }

        public async Task NotifyDoctorApprovalAsync(int doctorId)
        {
            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.Id == doctorId);

            if (doctor == null) return;

            await CreateNotificationAsync(
                doctor.UserId,
                "✅ تم تفعيل حسابك",
                "مبروك! تم الموافقة على طلبك. يمكنك الآن تحديد سعر الاستشارة والبدء في استقبال المرضى.",
                NotificationType.DoctorApproval,
                relatedEntityId: doctor.Id,
                relatedEntityType: "Doctor",
                actionUrl: "/doctor/home"
            );
        }

        public async Task NotifyDoctorRejectionAsync(int doctorId, string reason)
        {
            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.Id == doctorId);

            if (doctor == null) return;

            await CreateNotificationAsync(
                doctor.UserId,
                "❌ تم رفض طلبك",
                $"للأسف تم رفض طلب التسجيل. السبب: {reason}. يمكنك التواصل معنا للمزيد من التفاصيل.",
                NotificationType.DoctorRejection,
                relatedEntityId: doctor.Id,
                relatedEntityType: "Doctor",
                actionUrl: "/doctor/support"
            );
        }

        // ===================== Payments =====================

        public async Task NotifyPaymentSuccessAsync(int paymentId)
        {
            var payment = await _context.Payments
                .Include(p => p.Consultation)
                    .ThenInclude(c => c.Patient)
                        .ThenInclude(p => p.User)
                .Include(p => p.Consultation)
                    .ThenInclude(c => c.Doctor)
                        .ThenInclude(d => d.User)
                .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null) return;
            if (payment.Consultation?.Doctor == null) return;

            var consultationId = payment.Consultation.Id;

            // patient notification
            await CreateNotificationAsync(
                payment.Consultation.Patient.UserId,
                "✅ تم الدفع بنجاح",
                $"تم تأكيد حجز استشارتك مع د. {payment.Consultation.Doctor.User.FullName}",
                NotificationType.PaymentSuccess,
                relatedEntityId: payment.Id,
                relatedEntityType: "Payment",
                actionUrl: $"/consultations/{consultationId}"
            );

            // doctor notification
            await CreateNotificationAsync(
                payment.Consultation.Doctor.UserId,
                "💰 استشارة جديدة مدفوعة",
                $"لديك استشارة جديدة من {payment.Consultation.Patient.User.FullName}  - المبلغ: {payment.Amount} جنيه",
                NotificationType.NewConsultation,
                relatedEntityId: consultationId,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{consultationId}"
            );
        }

        // ===================== New Consultation Request (OPEN to doctors) =====================

        public async Task NotifyNewConsultationRequestAsync(int consultationId)
        {
            var consultation = await _context.Consultations
                .Include(c => c.Patient).ThenInclude(p => p.User)
                .Include(c => c.DiseaseCase)
                .FirstOrDefaultAsync(c => c.Id == consultationId);

            if (consultation == null) return;

            var doctorUserIds = await _context.Doctors
                .Select(d => d.UserId)
                .ToListAsync();

            foreach (var doctorUserId in doctorUserIds)
            {
                await CreateNotificationAsync(
                    doctorUserId,
                    "طلب استشارة جديد",
                    $"يوجد طلب استشارة جديد من {consultation.Patient.User.FullName} .",
                    NotificationType.NewConsultationRequest,
                    relatedEntityId: consultation.Id,
                    relatedEntityType: "Consultation",
                    actionUrl: $"/consultations/open"
                );
            }
        }

        // ===================== Offers =====================

        public async Task NotifyNewOfferAsync(int offerId)
        {
            var offer = await _context.ConsultationOffers
                .Include(o => o.Consultation)
                    .ThenInclude(c => c.Patient)
                        .ThenInclude(p => p.User)
                .Include(o => o.Doctor)
                    .ThenInclude(d => d.User)
                .FirstOrDefaultAsync(o => o.Id == offerId);

            if (offer == null) return;

            var patientUserId = offer.Consultation.Patient.UserId;

            await CreateNotificationAsync(
                patientUserId,
                "عرض جديد من طبيب",
                $"تم تقديم عرض جديد من د. {offer.Doctor.User.FullName}  بسعر {offer.Price} جنيه.",
                NotificationType.NewOffer,
                relatedEntityId: offer.ConsultationId,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{offer.ConsultationId}/offers"
            );
        }

        public async Task NotifyOfferAcceptedAsync(int offerId)
        {
            var offer = await _context.ConsultationOffers
                .Include(o => o.Consultation)
                    .ThenInclude(c => c.Patient)
                        .ThenInclude(p => p.User)
                .Include(o => o.Doctor)
                    .ThenInclude(d => d.User)
                .FirstOrDefaultAsync(o => o.Id == offerId);

            if (offer == null) return;

            var doctorUserId = offer.Doctor.UserId;

            await CreateNotificationAsync(
                doctorUserId,
                "✅ تم اختيار عرضك",
                $"تم اختيار عرضك من المريض {offer.Consultation.Patient.User.FullName} . يمكنك الآن انتظار الدفع لبدء الجلسة.",
                NotificationType.OfferAccepted,
                relatedEntityId: offer.ConsultationId,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{offer.ConsultationId}"
            );
        }

        // ===================== Chat messages =====================

        public async Task NotifyNewMessageAsync(int consultationId, int receiverUserId)
        {
            var consultation = await _context.Consultations
                .Include(c => c.Patient).ThenInclude(p => p.User)
                .Include(c => c.Doctor).ThenInclude(d => d.User)
                .FirstOrDefaultAsync(c => c.Id == consultationId);

            if (consultation == null) return;

            await CreateNotificationAsync(
                receiverUserId,
                "رسالة جديدة",
                "لديك رسالة جديدة في الاستشارة.",
                NotificationType.NewMessage,
                relatedEntityId: consultation.Id,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{consultation.Id}/chat"
            );
        }

        // ===================== Final diagnosis =====================

        public async Task NotifyDiagnosisCompletedAsync(int consultationId)
        {
            var consultation = await _context.Consultations
                .Include(c => c.Patient).ThenInclude(p => p.User)
                .Include(c => c.Doctor).ThenInclude(d => d.User)
                .FirstOrDefaultAsync(c => c.Id == consultationId);

            if (consultation == null) return;

            await CreateNotificationAsync(
                consultation.Patient.UserId,
                "✅ تم التشخيص النهائي",
                "الطبيب أضاف التشخيص النهائي لحالتك. يمكنك الآن مراجعة النتيجة.",
                NotificationType.DiagnosisCompleted,
                relatedEntityId: consultation.Id,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{consultation.Id}"
            );
        }

        // ===================== Core Create (DB + Push) =====================

        public async Task CreateNotificationAsync(
            int userId,
            string title,
            string message,
            NotificationType type,
            int? relatedEntityId = null,
            string? relatedEntityType = null,
            string? actionUrl = null)
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                RelatedEntityId = relatedEntityId,
                RelatedEntityType = relatedEntityType,
                ActionUrl = actionUrl,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            // ✅ بعد ما نخزن في DB نبعت Push (لو النوع من الأنواع المهمة)
            if (!ShouldSendPush(type))
                return;

            try
            {
                var data = BuildPushData(type, relatedEntityId, relatedEntityType, actionUrl);

                await _push.SendToUserAsync(
                    userId,
                    title,
                    message,
                    data,
                    CancellationToken.None
                );
            }
            catch
            {
                // ✅ لا تكسر السيستم لو OneSignal وقع
            }
        }

        private static bool ShouldSendPush(NotificationType type)
        {
            // اختاري اللي محتاج Push فعلاً
            return type switch
            {
                NotificationType.NewMessage => true,
                NotificationType.PaymentSuccess => true,
                NotificationType.NewConsultation => true,
                NotificationType.DoctorApproval => true,
                NotificationType.DoctorRejection => true,
                NotificationType.NewOffer => true,
                NotificationType.OfferAccepted => true,
                NotificationType.DiagnosisCompleted => true,
                NotificationType.NewConsultationRequest => true,
                NotificationType.AdminAlert => true,
                _ => false
            };
        }

        private static Dictionary<string, string> BuildPushData(
            NotificationType type,
            int? relatedEntityId,
            string? relatedEntityType,
            string? actionUrl)
        {
            var data = new Dictionary<string, string>
            {
                ["type"] = type.ToString()
            };

            if (relatedEntityId.HasValue) data["relatedEntityId"] = relatedEntityId.Value.ToString();
            if (!string.IsNullOrWhiteSpace(relatedEntityType)) data["relatedEntityType"] = relatedEntityType!;
            if (!string.IsNullOrWhiteSpace(actionUrl)) data["actionUrl"] = actionUrl!;

            return data;
        }
    }

    // ✅ Expanded Notification Type Enum
    public enum NotificationType
    {
        General = 0,
        DoctorApproval = 1,
        DoctorRejection = 2,
        PaymentSuccess = 3,
        PaymentFailed = 4,
        NewConsultation = 5,
        ConsultationUpdate = 6,
        AdminAlert = 7,
        DiagnosisReady = 8,

        NewConsultationRequest = 9,
        NewOffer = 10,
        OfferAccepted = 11,
        NewMessage = 12,
        DiagnosisCompleted = 13
    }
}
