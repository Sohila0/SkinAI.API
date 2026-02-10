using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SkinAI.API.Services
{
    public class SkinDiseasePredictionService
    {
        private readonly string _pythonExe;
        private readonly string _predictScriptPath;

        public SkinDiseasePredictionService(string contentRootPath)
        {
            // 1) مسارات ثابتة داخل المشروع
            _predictScriptPath = Path.Combine(contentRootPath, "AI", "predict.py");

            if (!File.Exists(_predictScriptPath))
                throw new FileNotFoundException($"predict.py not found: {_predictScriptPath}");

            // 2) بايثون: جرّب venv أولاً ثم python من PATH
            var venvPython = Path.Combine(contentRootPath, "venv310", "Scripts", "python.exe");
            _pythonExe = File.Exists(venvPython) ? venvPython : "python";
        }

        public PredictionResult Predict(byte[] imageBytes)
        {
            // احفظ الصورة مؤقتًا
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
            File.WriteAllBytes(tmp, imageBytes);

            try
            {
                var json = RunPython(tmp);

                // parse JSON
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    throw new Exception("Python error: " + err.GetString());
                }

                var label = doc.RootElement.GetProperty("diagnosis").GetString() ?? "Unknown_Normal";
                var confidence = doc.RootElement.GetProperty("confidence").GetSingle();

                float[] scores = Array.Empty<float>();
                if (doc.RootElement.TryGetProperty("scores", out var scoresEl) && scoresEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<float>();
                    foreach (var s in scoresEl.EnumerateArray())
                        list.Add(s.GetSingle());
                    scores = list.ToArray();
                }

                return new PredictionResult
                {
                    Label = label,
                    Confidence = confidence,
                    Scores = scores
                };
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* ignore */ }
            }
        }

        private string RunPython(string imagePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _pythonExe,
                Arguments = $"\"{_predictScriptPath}\" \"{imagePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,

                // مهم: نخلي الـ WorkingDirectory = ContentRoot (عشان أي مسارات نسبية تشتغل)
                WorkingDirectory = Path.GetDirectoryName(_predictScriptPath) ?? ""
            };

            // UTF-8 عشان العربي
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            using var p = new Process { StartInfo = psi };

            p.Start();

            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();

            p.WaitForExit();

            // لو python وقع 강조
            if (p.ExitCode != 0)
                throw new Exception($"Python failed (ExitCode={p.ExitCode}). Error: {error}\nOutput: {output}");

            // أحيانًا tensorflow بيطبع تحذيرات في stderr حتى لو نجح
            // فإحنا نعتمد على output (JSON) طالما exitcode=0
            output = output.Trim();

            // تأكد إنه JSON فعلاً
            if (!output.StartsWith("{"))
            {
                throw new Exception("Python did not return JSON. Output: " + output + "\nError: " + error);
            }

            return output;
        }
    }

    public class PredictionResult
    {
        public string Label { get; set; } = "Unknown_Normal";
        public float Confidence { get; set; }
        public float[] Scores { get; set; } = Array.Empty<float>();
    }
}
