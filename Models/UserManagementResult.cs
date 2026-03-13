namespace TvGuide.Models;

/// <summary>
/// User management operation result.
/// </summary>
public enum UserManagementResult
{
    /// <summary>
    /// The requested user could not be found.
    /// </summary>
    NotFound = 0,

    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Success = 1,

    /// <summary>
    /// The user is already being tracked.
    /// </summary>
    AlreadyExists = 2,

    /// <summary>
    /// The operation failed unexpectedly.
    /// </summary>
    Error = 3
}