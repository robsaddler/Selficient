namespace Selficient.Application.Abstractions;

/// <summary>
/// Anchors the Application assembly for architecture tests and assembly scanning until the first
/// use case arrives in M1, at which point this interface is deleted.
/// </summary>
public interface IApplicationMarker;
