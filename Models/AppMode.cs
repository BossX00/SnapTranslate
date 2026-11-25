namespace SnapTranslate.Models;

public enum AppMode
{
    Popup,
    Typing
}

public sealed record TranslationResult(
    string SourceText,
    string TranslatedText,
    string SourceLang,
    string TargetLang);
