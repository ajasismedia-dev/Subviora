using Subviora.Core.Vision;

namespace Subviora.Vision.PaddleOcr;

/// <summary>
/// Architectural reference and evaluation documentation for PaddleOCR (PP-OCRv4).
/// Not a functioning local provider; marks itself as unavailable until native runtime is integrated.
/// </summary>
public sealed class PaddleOcrBenchmarkAdapter : IOcrProvider
{
    public string Id => "paddle-ocr";

    public string DisplayName => "PaddleOCR (Yüklü Değil / Referans)";

    public bool IsAvailable => false;

    /// <summary>
    /// License information for PaddleOCR components.
    /// </summary>
    public static string LicenseInfo => "Apache-2.0 (Commercial distribution permitted)";

    /// <summary>
    /// Total binary footprint required for a native deployment (inference runtime + models).
    /// </summary>
    public static string RuntimeFootprintDescription =>
        "Inference Runtime (ONNX Runtime / OpenVINO / Paddle Inference C++): ~150-300 MB unmanaged DLLs. Models (PP-OCRv4 detection + recognition): ~15 MB.";

    public string? AvailabilityMessage =>
        "PaddleOCR entegrasyonu kurulu değil. ONNX Runtime / Paddle Inference C++ çalışma zamanı (~150-300 MB) ve model ağırlıkları (~15 MB) gerektirir; yalnızca karşılaştırma referansıdır.";

    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException(AvailabilityMessage);
    }
}
