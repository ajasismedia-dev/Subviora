namespace Subviora.Core.Translation;

/// <summary>A translation request.</summary>
/// <param name="Text">Source text.</param>
/// <param name="SourceLanguage">BCP-47 source language tag, e.g. "en".</param>
/// <param name="TargetLanguage">BCP-47 target language tag, e.g. "tr".</param>
public sealed record TranslationRequest(string Text, string SourceLanguage, string TargetLanguage);

/// <summary>A translation result.</summary>
/// <param name="TranslatedText">Translated text.</param>
/// <param name="ProviderId">Identifier of the provider that produced the translation.</param>
/// <param name="FromCache">True when the result was served from a cache rather than freshly computed.</param>
public sealed record TranslationResult(string TranslatedText, string ProviderId, bool FromCache);

/// <summary>Translates text. Offline providers are the default; online providers are optional.</summary>
public interface ITranslationProvider
{
    /// <summary>Stable provider identifier.</summary>
    string Id { get; }

    /// <summary>True if the provider runs entirely on the local machine without network access.</summary>
    bool IsOffline { get; }

    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}
