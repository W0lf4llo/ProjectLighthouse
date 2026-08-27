#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LBPUnion.ProjectLighthouse.Servers.GameServer.Types.Users;

// Pin metadata is stored beside this source file in Types/Users/pins.yml.
// This class only loads, validates, and queries that metadata.
public static class PinDefinitions
{
    public enum PinGame
    {
        Lbp2,
        Lbp3,
        Vita,
    }

    public readonly record struct PinDefinition(
        uint Id,
        uint ProgressType,
        int Category,
        int InitialProgressValue,
        int TargetValue,
        int TrophyToUnlock,
        int? TrophyToUnlockLbp1,
        int BehaviourFlags);

    private const int MetadataFormatVersion = 1;
    private const int ExpectedLbp2Count = 567;
    private const int ExpectedLbp3Count = 801;
    private const int ExpectedVitaCount = 399;

    private static readonly PinMetadataFile Metadata = LoadMetadata();

    public static readonly PinDefinition[] Lbp2 =
        BuildStandaloneRegistry(
            Metadata.Lbp2,
            nameof(Lbp2));

    public static readonly PinDefinition[] Lbp3 =
        BuildLbp3Registry();

    public static readonly PinDefinition[] Vita =
        BuildStandaloneRegistry(
            Metadata.Vita,
            nameof(Vita));

    private static readonly HashSet<uint> Lbp2PinIds = BuildPinIdSet(Lbp2);
    private static readonly HashSet<uint> Lbp3PinIds = BuildPinIdSet(Lbp3);
    private static readonly HashSet<uint> VitaPinIds = BuildPinIdSet(Vita);

    private static readonly HashSet<uint> Lbp2ProgressTypes = BuildProgressTypeSet(Lbp2);
    private static readonly HashSet<uint> Lbp3ProgressTypes = BuildProgressTypeSet(Lbp3);
    private static readonly HashSet<uint> VitaProgressTypes = BuildProgressTypeSet(Vita);

    static PinDefinitions()
    {
        ValidateRegistry(Lbp2, ExpectedLbp2Count, nameof(Lbp2));
        ValidateRegistry(Lbp3, ExpectedLbp3Count, nameof(Lbp3));
        ValidateRegistry(Vita, ExpectedVitaCount, nameof(Vita));
    }

    public static IReadOnlyList<PinDefinition> ForGame(PinGame game) => game switch
    {
        PinGame.Lbp2 => Lbp2,
        PinGame.Lbp3 => Lbp3,
        PinGame.Vita => Vita,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    public static bool IsValidPinId(PinGame game, uint pinId) => game switch
    {
        PinGame.Lbp2 => Lbp2PinIds.Contains(pinId),
        PinGame.Lbp3 => Lbp3PinIds.Contains(pinId),
        PinGame.Vita => VitaPinIds.Contains(pinId),
        _ => false,
    };

    public static bool IsValidProgressType(PinGame game, uint progressType) => game switch
    {
        PinGame.Lbp2 => Lbp2ProgressTypes.Contains(progressType),
        PinGame.Lbp3 => Lbp3ProgressTypes.Contains(progressType),
        PinGame.Vita => VitaProgressTypes.Contains(progressType),
        _ => false,
    };

    public static bool TryGetPin(
        PinGame game,
        uint pinId,
        out PinDefinition definition)
    {
        foreach (PinDefinition pin in ForGame(game))
        {
            if (pin.Id != pinId)
                continue;

            definition = pin;
            return true;
        }

        definition = default;
        return false;
    }

    public static bool IsLowerProgressBetter(
        PinGame game,
        uint progressType)
    {
        bool found = false;
        bool lowerIsBetter = false;

        foreach (PinDefinition pin in ForGame(game))
        {
            if (pin.ProgressType != progressType)
                continue;

            bool thisPinLowerIsBetter =
                pin.TargetValue < pin.InitialProgressValue;

            if (!found)
            {
                found = true;
                lowerIsBetter = thisPinLowerIsBetter;
                continue;
            }

            if (thisPinLowerIsBetter != lowerIsBetter)
            {
                throw new InvalidOperationException(
                    $"ProgressType {progressType} contains pins with conflicting progress directions.");
            }
        }

        if (!found)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressType),
                progressType,
                $"Unknown ProgressType for {game}.");
        }

        return lowerIsBetter;
    }

    private static PinMetadataFile LoadMetadata()
    {
        string path = GetMetadataPath();

        try
        {
            string yaml = File.ReadAllText(path);

            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            PinMetadataFile? metadata =
                deserializer.Deserialize<PinMetadataFile>(yaml);

            if (metadata == null)
                throw new InvalidOperationException("The YAML document was empty.");

            if (metadata.FormatVersion != MetadataFormatVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported pin metadata format version {metadata.FormatVersion}; expected {MetadataFormatVersion}.");
            }

            return metadata;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to load pin metadata from '{path}'.",
                exception);
        }
    }

    private static string GetMetadataPath()
    {
        //
        // In built/published GameServer output, pins.yml is copied
        // directly beside the executable.
        //
        string outputPath = Path.Combine(
            AppContext.BaseDirectory,
            "pins.yml");

        if (File.Exists(outputPath))
            return outputPath;

        //
        // Fallback for development/test execution directly from
        // a Project Lighthouse source checkout.
        //
        DirectoryInfo? directory =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string sourcePath = Path.Combine(
                directory.FullName,
                "ProjectLighthouse.Servers.GameServer",
                "Types",
                "Users",
                "pins.yml");

            if (File.Exists(sourcePath))
                return sourcePath;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate Project Lighthouse pin metadata. " +
            "Expected Types/Users/pins.yml in the source tree " +
            "or pins.yml beside the GameServer output.");
    }

    private static PinDefinition[] BuildStandaloneRegistry(
        PinGameMetadata metadata,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Inherits))
        {
            throw new InvalidOperationException(
                $"{name} unexpectedly declares inheritance from '{metadata.Inherits}'.");
        }

        if (metadata.Overrides.Count != 0)
        {
            throw new InvalidOperationException(
                $"{name} cannot contain overrides without inheritance.");
        }

        return metadata.Pins
            .Select(pin =>
                pin.ToDefinition(metadata.TrophyToUnlockLbp1))
            .ToArray();
    }

    private static PinDefinition[] BuildLbp3Registry()
    {
        PinGameMetadata metadata = Metadata.Lbp3;

        if (!string.Equals(
                metadata.Inherits,
                "lbp2",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "LBP3 pin metadata must inherit from 'lbp2'.");
        }

        List<PinDefinition> definitions =
            Lbp2
                .Select(pin => pin with
                {
                    TrophyToUnlockLbp1 =
                        metadata.TrophyToUnlockLbp1,
                })
                .ToList();

        Dictionary<uint, int> indices =
            definitions
                .Select((pin, index) => new
                {
                    pin.Id,
                    Index = index,
                })
                .ToDictionary(
                    value => value.Id,
                    value => value.Index);

        foreach (PinYamlEntry yamlPin in metadata.Overrides)
        {
            PinDefinition pin =
                yamlPin.ToDefinition(
                    metadata.TrophyToUnlockLbp1);

            if (!indices.TryGetValue(pin.Id, out int index))
            {
                throw new InvalidOperationException(
                    $"LBP3 override PinId {pin.Id} does not exist in the LBP2 base registry.");
            }

            definitions[index] = pin;
        }

        foreach (PinYamlEntry yamlPin in metadata.Pins)
        {
            PinDefinition pin =
                yamlPin.ToDefinition(
                    metadata.TrophyToUnlockLbp1);

            if (indices.ContainsKey(pin.Id))
            {
                throw new InvalidOperationException(
                    $"LBP3-only PinId {pin.Id} already exists in the inherited LBP2 registry.");
            }

            indices.Add(pin.Id, definitions.Count);
            definitions.Add(pin);
        }

        return definitions.ToArray();
    }

    private static HashSet<uint> BuildPinIdSet(
        IReadOnlyList<PinDefinition> definitions)
    {
        HashSet<uint> result = new(definitions.Count);

        foreach (PinDefinition pin in definitions)
            result.Add(pin.Id);

        return result;
    }

    private static HashSet<uint> BuildProgressTypeSet(
        IReadOnlyList<PinDefinition> definitions)
    {
        HashSet<uint> result = new();

        foreach (PinDefinition pin in definitions)
            result.Add(pin.ProgressType);

        return result;
    }

    private static void ValidateRegistry(
        IReadOnlyList<PinDefinition> definitions,
        int expectedCount,
        string name)
    {
        if (definitions.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"{name} pin registry contains {definitions.Count} definitions; expected {expectedCount}.");
        }

        HashSet<uint> ids = new(definitions.Count);

        foreach (PinDefinition pin in definitions)
        {
            if (!ids.Add(pin.Id))
            {
                throw new InvalidOperationException(
                    $"{name} contains duplicate PinId {pin.Id}.");
            }
        }
    }

    private sealed class PinMetadataFile
    {
        public int FormatVersion { get; set; }

        public PinGameMetadata Lbp2 { get; set; } = new();

        public PinGameMetadata Lbp3 { get; set; } = new();

        public PinGameMetadata Vita { get; set; } = new();
    }

    private sealed class PinGameMetadata
    {
        public string? Inherits { get; set; }

        public int? TrophyToUnlockLbp1 { get; set; }

        public List<PinYamlEntry> Overrides { get; set; } = [];

        public List<PinYamlEntry> Pins { get; set; } = [];
    }

    private sealed class PinYamlEntry
    {
        public uint Id { get; set; }

        public uint ProgressType { get; set; }

        public int Category { get; set; }

        public int InitialProgressValue { get; set; }

        public int TargetValue { get; set; }

        public int TrophyToUnlock { get; set; }

        public int BehaviourFlags { get; set; }

        public PinDefinition ToDefinition(
            int? trophyToUnlockLbp1)
        {
            return new PinDefinition(
                this.Id,
                this.ProgressType,
                this.Category,
                this.InitialProgressValue,
                this.TargetValue,
                this.TrophyToUnlock,
                trophyToUnlockLbp1,
                this.BehaviourFlags);
        }
    }
}