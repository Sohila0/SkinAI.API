using SkinAI.API.Models;

namespace SkinAI.API.Dtos.Payments
{
    public class SimulatePaymentDto
    {
        public int ConsultationId { get; set; }
        public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    }
}
