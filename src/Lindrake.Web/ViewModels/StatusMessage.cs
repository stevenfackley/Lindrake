namespace OmniSift.Web.ViewModels;

/// <summary>How a <see cref="StatusMessage"/> is presented (the <c>alert-*</c> CSS classes).</summary>
public enum AlertKind
{
    Info,
    Success,
    Error
}

/// <summary>A one-line outcome shown to the user.</summary>
public sealed record StatusMessage(AlertKind Kind, string Text)
{
    public static StatusMessage Info(string text) => new(AlertKind.Info, text);
    public static StatusMessage Success(string text) => new(AlertKind.Success, text);
    public static StatusMessage Error(string text) => new(AlertKind.Error, text);
}
