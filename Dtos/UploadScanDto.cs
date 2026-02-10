using Microsoft.AspNetCore.Http;

namespace SkinAI.API.Dtos
{
    public class UploadScanDto
    {
        public int PatientId { get; set; }
        public IFormFile Image { get; set; }
    }
}
