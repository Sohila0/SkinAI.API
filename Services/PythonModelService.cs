using Newtonsoft.Json;
using SkinAI.API.Dtos;
using System.Diagnostics;

namespace SkinAI.API.Services
{
    public class PythonModelService
    {
        public AiPredictionResult Predict(string imagePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"AI/predict.py \"{imagePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (!string.IsNullOrEmpty(error))
                throw new Exception(error);

            return JsonConvert.DeserializeObject<AiPredictionResult>(output)!;
        }
    }
}
