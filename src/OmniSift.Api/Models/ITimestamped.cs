// ============================================================
// OmniSift.Api — Timestamped entity markers
// ============================================================

namespace OmniSift.Api.Models;

/// <summary>
/// An entity whose <see cref="CreatedAt"/> (UTC) is stamped on insert by
/// <see cref="Data.TimestampInterceptor"/>. Never set it by hand.
/// </summary>
public interface ICreationTimestamped
{
    DateTime CreatedAt { get; set; }
}

/// <summary>
/// An entity whose <see cref="UpdatedAt"/> (UTC) is also stamped by
/// <see cref="Data.TimestampInterceptor"/>, on insert and on every update.
/// Never set it by hand.
/// </summary>
public interface ITimestamped : ICreationTimestamped
{
    DateTime UpdatedAt { get; set; }
}
