namespace SkinAI.API.Dtos
{
    public class AiPredictionResult
    {
        public int class_index { get; set; }
        public string label { get; set; }
        public double confidence { get; set; }

        public string Label => label;
        public double Confidence => confidence;
    }
}
