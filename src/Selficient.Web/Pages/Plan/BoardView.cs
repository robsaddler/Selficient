using Selficient.Application.Resilience;

namespace Selficient.Web.Pages.Plan;

/// <summary>What the board partial needs.</summary>
public sealed record BoardView(BoardSet Set, bool HasOwnRoof);
