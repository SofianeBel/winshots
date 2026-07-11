namespace Winshots.App.Capture;

public sealed record TextContext(TextExtractionResult UiAutomation, OcrTextExtractionResult Ocr)
{
    public bool HasUsableUiAutomation =>
        UiAutomation.Status is not ("failed" or "timed-out") &&
        !string.IsNullOrWhiteSpace(UiAutomation.Text);

    public string TextSource => Ocr.Status == "succeeded"
        ? HasUsableUiAutomation ? "windows_ui_automation+ocr" : "windows_ocr"
        : "windows_ui_automation";

    public string MatchText => string.Join(
        Environment.NewLine,
        new[] { HasUsableUiAutomation ? UiAutomation.Text : null, Ocr.Text }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));

    public string ArtifactText
    {
        get
        {
            string uiText = string.IsNullOrWhiteSpace(UiAutomation.Text)
                ? "[No UI Automation text was exposed by this window.]"
                : UiAutomation.Text.Trim();
            if (Ocr.Status == "not-needed")
            {
                return uiText;
            }

            string ocrText = string.IsNullOrWhiteSpace(Ocr.Text)
                ? $"[OCR {Ocr.Status}: {Ocr.Detail ?? "no text was recognized"}]"
                : Ocr.Text.Trim();
            return $"{uiText}{Environment.NewLine}{Environment.NewLine}--- OCR context ---{Environment.NewLine}{ocrText}";
        }
    }
}
