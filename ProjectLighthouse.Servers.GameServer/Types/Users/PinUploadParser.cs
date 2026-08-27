using System.Text.Json;

namespace LBPUnion.ProjectLighthouse.Servers.GameServer.Types.Users;

public static class PinUploadParser
{
    public readonly record struct ProgressEntry(uint ProgressType, double Value);
    public readonly record struct AwardEntry(uint PinId, long Count);
    public sealed class ParsedPinUpload
    {
        public List<ProgressEntry> Progress { get; } = [];
        public List<AwardEntry> Awards { get; } = [];
        public bool HasProfilePins { get; internal set; }
        public List<uint> ProfilePins { get; } = [];
        public bool ProfilePinsSafeToApply { get; internal set; } = true;
        //Unknown pin id's are ignored instead of causing the upload to fail
        public List<uint> IgnoredProgressTypes { get; } = [];
        public List<uint> IgnoredAwardPinIds { get; } = [];
        public List<uint> IgnoredProfilePinIds { get; } = [];
    }

    public static bool TryParse(Pins upload, PinDefinitions.PinGame game, out ParsedPinUpload result)
    {
        result = new ParsedPinUpload();

        if (!TryParseProgress(upload.Progress, game, result))
            return false;

        if (!TryParseAwards(upload.Awards, game, result))
            return false;

        if (!TryParseProfilePins(upload.ProfilePins, game, result))
            return false;

        return true;
    }

    private static bool TryParseProgress(JsonElement[]? values, PinDefinitions.PinGame game, ParsedPinUpload result)
    {
        if (values == null)
            return true;

        if ((values.Length & 1) != 0)
            return false;

        for (int i = 0; i < values.Length; i += 2)
        {
            JsonElement progressTypeElement = values[i];
            JsonElement valueElement = values[i + 1];

            if (!progressTypeElement.TryGetUInt32(out uint progressType))
            {
                continue;
            }

            if (!valueElement.TryGetDouble(out double value))
                continue;

            if (double.IsNaN(value) || double.IsInfinity(value))
                continue;

            if (!PinDefinitions.IsValidProgressType(game, progressType))
            {
                result.IgnoredProgressTypes.Add(progressType);
                continue;
            }
            if (result.Progress.Any(p => p.ProgressType == progressType))
                return false;

            result.Progress.Add(new ProgressEntry(progressType, value));
        }

        return true;
    }

    private static bool TryParseAwards(JsonElement[]? values, PinDefinitions.PinGame game, ParsedPinUpload result)
    {
        if (values == null)
            return true;

        if ((values.Length & 1) != 0)
            return false;

        for (int i = 0; i < values.Length; i += 2)
        {
            JsonElement pinIdElement = values[i];
            JsonElement countElement = values[i + 1];

            if (!pinIdElement.TryGetUInt32(out uint pinId))
                continue;

            if (!countElement.TryGetInt64(out long count))
                continue;

            if (count < 0)
                continue;

            if (!PinDefinitions.IsValidPinId(game, pinId))
            {
                result.IgnoredAwardPinIds.Add(pinId);
                continue;
            }
            if (result.Awards.Any(a => a.PinId == pinId))
                return false;

            result.Awards.Add(new AwardEntry(pinId, count));
        }

        return true;
    }

    private static bool TryParseProfilePins(JsonElement[]? values, PinDefinitions.PinGame game, ParsedPinUpload result)
    {
        if (values == null)
        {
            result.HasProfilePins = false;
            return true;
        }

        result.HasProfilePins = true;

        if (values.Length > 3)
            return false;

        foreach (JsonElement value in values)
        {
            if (!value.TryGetUInt32(out uint pinId))
            {
                result.ProfilePinsSafeToApply = false;
                continue;
            }

            if (!PinDefinitions.IsValidPinId(game, pinId))
            {
                result.IgnoredProfilePinIds.Add(pinId);
                result.ProfilePinsSafeToApply = false;
                continue;
            }

            if (result.ProfilePins.Contains(pinId))
            {
                result.ProfilePinsSafeToApply = false;
                continue;
            }

            result.ProfilePins.Add(pinId);
        }

        return true;
    }
}