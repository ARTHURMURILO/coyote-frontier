using Robust.Shared.Configuration;

namespace Content.Shared._CS.CCVar;

/// <summary>
/// Contains CVars used by Coyote.
/// </summary>
[CVarDefs]
public sealed class CSCVars
{
    /// <summary>
    /// Max number of items on a belt before we destroy it/warn admins
    /// </summary>
    public static readonly CVarDef<int> ConveyorMaxItemCount =
    CVarDef.Create("conveyor.max_item_count", 200, CVar.SERVERONLY);
    /// <summary>
    /// Max number of items on a belt before we destroy it/warn admins
    /// </summary>
    public static readonly CVarDef<float> ConveyorCleanupIntervalSeconds =
    CVarDef.Create("conveyor.cleanup_interval_seconds", 51f, CVar.SERVERONLY);

    /*
     * AI Core
     */

    /// <summary>
    /// Default API endpoint URL for AI cores. Can be overridden per-core.
    /// </summary>
    public static readonly CVarDef<string> AICoreDefaultEndpoint =
    CVarDef.Create("ai_core.default_endpoint", "http://localhost:1234/v1/chat/completions", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default model name for AI cores. If empty, the LLM API default is used.
    /// </summary>
    public static readonly CVarDef<string> AICoreDefaultModel =
    CVarDef.Create("ai_core.default_model", "", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default API key for AI cores. Empty means no default key.
    /// </summary>
    public static readonly CVarDef<string> AICoreDefaultApiKey =
    CVarDef.Create("ai_core.default_api_key", "", CVar.SERVERONLY | CVar.ARCHIVE | CVar.CONFIDENTIAL);

    /// <summary>
    /// Default temperature for AI core responses.
    /// </summary>
    public static readonly CVarDef<float> AICoreDefaultTemperature =
    CVarDef.Create("ai_core.default_temperature", 0.7f, CVar.SERVERONLY | CVar.ARCHIVE);
}
