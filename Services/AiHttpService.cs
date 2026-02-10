using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace SkinAI.API.Services
{
    public class AiHttpService
    {
        private readonly HttpClient _http;

        public AiHttpService(HttpClient http)
        {
            _http = http;
        }

        /// <summary>
        /// Sends image to FastAPI /predict and returns the JSON response as string.
        /// FastAPI expects multipart field name = "file".
        /// </summary>
        public async Task<string> PredictAsync(IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty.");

            using var form = new MultipartFormDataContent();

            // Read stream from uploaded file
            await using var stream = file.OpenReadStream();
            using var fileContent = new StreamContent(stream);

            // ContentType
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            // ✅ IMPORTANT: FastAPI expects the field name "file"
            // Also set file name
            var safeFileName = string.IsNullOrWhiteSpace(file.FileName) ? "image.jpg" : file.FileName;
            form.Add(fileContent, "file", safeFileName);

            using var resp = await _http.PostAsync("/predict", form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"AI service error: {(int)resp.StatusCode} - {body}");

            return body;
        }
    }
}
