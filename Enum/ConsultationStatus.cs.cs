namespace SkinAI.API.Enums
{
    public enum ConsultationStatus
    {
        OPEN = 0,            // الطلب ظاهر لكل الأطباء
        OFFERING = 1,        // في عروض اتبعتت
        OFFER_SELECTED = 2,  // المريض اختار عرض
        PAID = 3,            // الدفع (simulation) تم
        IN_CHAT = 4,         // بدأ الشات
        CLOSED = 5,          // اتقفل بعد التشخيص النهائي
        CANCELLED = 6        // المريض لغى (اختياري)
    }
}
