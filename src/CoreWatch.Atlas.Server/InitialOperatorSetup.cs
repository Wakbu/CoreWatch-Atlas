using System.ComponentModel.DataAnnotations;

namespace CoreWatch.Atlas.Server;

public sealed record InitialOperatorSetupRequest(
    [property: Required, StringLength(64, MinimumLength = 3)] string Username,
    [property: Required, StringLength(128, MinimumLength = 12)] string Password);
// CoreWatch Atlas module: InitialOperatorSetup.
