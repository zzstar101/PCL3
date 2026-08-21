using System.Text.Json;

namespace PCL3.Minecraft.Metadata;

public static class MinecraftVersionJson
{
    public static MinecraftVersionMetadata Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("Minecraft version metadata root must be a JSON object.");
        }

        var id = GetRequiredString(root, "id");
        var type = GetOptionalString(root, "type");
        var mainClass = GetOptionalString(root, "mainClass");
        var inheritsFrom = GetOptionalString(root, "inheritsFrom");
        var legacyMinecraftArguments = GetOptionalString(root, "minecraftArguments");

        var javaVersion = ParseJavaVersion(root);
        var (jvmArguments, gameArguments) = ParseArguments(root);
        var libraries = ParseLibraries(root);

        return new MinecraftVersionMetadata(
            id,
            type,
            mainClass,
            inheritsFrom,
            javaVersion,
            jvmArguments,
            gameArguments,
            legacyMinecraftArguments,
            libraries);
    }

    private static MinecraftJavaVersion? ParseJavaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("javaVersion", out var element) ||
            element.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("'javaVersion' must be a JSON object.");
        }

        var component = GetRequiredString(element, "component");

        if (!element.TryGetProperty("majorVersion", out var majorVersionElement) ||
            !majorVersionElement.TryGetInt32(out var majorVersion))
        {
            throw new JsonException("'javaVersion.majorVersion' must be an integer.");
        }

        return new MinecraftJavaVersion(component, majorVersion);
    }

    private static (
        IReadOnlyList<MinecraftArgument> Jvm,
        IReadOnlyList<MinecraftArgument> Game) ParseArguments(JsonElement root)
    {
        if (!root.TryGetProperty("arguments", out var argumentsElement) ||
            argumentsElement.ValueKind is JsonValueKind.Null)
        {
            return (Array.Empty<MinecraftArgument>(), Array.Empty<MinecraftArgument>());
        }

        if (argumentsElement.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("'arguments' must be a JSON object.");
        }

        return (
            ParseArgumentArray(argumentsElement, "jvm"),
            ParseArgumentArray(argumentsElement, "game"));
    }

    private static IReadOnlyList<MinecraftArgument> ParseArgumentArray(
        JsonElement argumentsElement,
        string propertyName)
    {
        if (!argumentsElement.TryGetProperty(propertyName, out var arrayElement) ||
            arrayElement.ValueKind is JsonValueKind.Null)
        {
            return Array.Empty<MinecraftArgument>();
        }

        if (arrayElement.ValueKind is not JsonValueKind.Array)
        {
            throw new JsonException($"'arguments.{propertyName}' must be a JSON array.");
        }

        var result = new List<MinecraftArgument>();

        foreach (var element in arrayElement.EnumerateArray())
        {
            result.Add(ParseArgument(element));
        }

        return result;
    }

    private static MinecraftArgument ParseArgument(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.String)
        {
            return new MinecraftArgument(
                new[] { element.GetString()! },
                Array.Empty<MinecraftRule>());
        }

        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException(
                "Minecraft arguments must be either strings or conditional argument objects.");
        }

        if (!element.TryGetProperty("value", out var valueElement))
        {
            throw new JsonException("Conditional Minecraft argument is missing 'value'.");
        }

        var values = valueElement.ValueKind switch
        {
            JsonValueKind.String => new[] { valueElement.GetString()! },
            JsonValueKind.Array => valueElement
                .EnumerateArray()
                .Select(ReadArgumentValue)
                .ToArray(),
            _ => throw new JsonException(
                "Conditional Minecraft argument 'value' must be a string or string array.")
        };

        var rules = element.TryGetProperty("rules", out var rulesElement)
            ? ParseRules(rulesElement)
            : Array.Empty<MinecraftRule>();

        return new MinecraftArgument(values, rules);
    }

    private static string ReadArgumentValue(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.String)
        {
            throw new JsonException(
                "Conditional Minecraft argument arrays may contain only strings.");
        }

        return element.GetString()!;
    }

    private static IReadOnlyList<MinecraftLibrary> ParseLibraries(JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var librariesElement) ||
            librariesElement.ValueKind is JsonValueKind.Null)
        {
            return Array.Empty<MinecraftLibrary>();
        }

        if (librariesElement.ValueKind is not JsonValueKind.Array)
        {
            throw new JsonException("'libraries' must be a JSON array.");
        }

        var libraries = new List<MinecraftLibrary>();

        foreach (var libraryElement in librariesElement.EnumerateArray())
        {
            if (libraryElement.ValueKind is not JsonValueKind.Object)
            {
                throw new JsonException("Each library entry must be a JSON object.");
            }

            var name = GetRequiredString(libraryElement, "name");
            var repositoryUrl = GetOptionalString(libraryElement, "url");
            var rules = libraryElement.TryGetProperty("rules", out var rulesElement)
                ? ParseRules(rulesElement)
                : Array.Empty<MinecraftRule>();
            var natives = ParseNatives(libraryElement);
            var downloads = ParseLibraryDownloads(libraryElement);
            var extract = ParseLibraryExtract(libraryElement);

            libraries.Add(new MinecraftLibrary(
                name,
                repositoryUrl,
                rules,
                natives,
                downloads,
                extract));
        }

        return libraries;
    }

    private static MinecraftLibraryDownloads? ParseLibraryDownloads(JsonElement libraryElement)
    {
        if (!libraryElement.TryGetProperty("downloads", out var downloadsElement) ||
            downloadsElement.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (downloadsElement.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("'libraries[].downloads' must be a JSON object.");
        }

        MinecraftDownloadArtifact? artifact = null;
        if (downloadsElement.TryGetProperty("artifact", out var artifactElement) &&
            artifactElement.ValueKind is not JsonValueKind.Null)
        {
            artifact = ParseDownloadArtifact(artifactElement, "libraries[].downloads.artifact");
        }

        var classifiers = new Dictionary<string, MinecraftDownloadArtifact>(StringComparer.Ordinal);
        if (downloadsElement.TryGetProperty("classifiers", out var classifiersElement) &&
            classifiersElement.ValueKind is not JsonValueKind.Null)
        {
            if (classifiersElement.ValueKind is not JsonValueKind.Object)
            {
                throw new JsonException("'libraries[].downloads.classifiers' must be a JSON object.");
            }

            foreach (var classifier in classifiersElement.EnumerateObject())
            {
                classifiers[classifier.Name] = ParseDownloadArtifact(
                    classifier.Value,
                    $"libraries[].downloads.classifiers.{classifier.Name}");
            }
        }

        return new MinecraftLibraryDownloads(artifact, classifiers);
    }

    private static MinecraftDownloadArtifact ParseDownloadArtifact(
        JsonElement element,
        string propertyPath)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException($"'{propertyPath}' must be a JSON object.");
        }

        long? size = null;
        if (element.TryGetProperty("size", out var sizeElement) &&
            sizeElement.ValueKind is not JsonValueKind.Null)
        {
            if (!sizeElement.TryGetInt64(out var parsedSize) || parsedSize < 0)
            {
                throw new JsonException($"'{propertyPath}.size' must be a non-negative integer.");
            }

            size = parsedSize;
        }

        return new MinecraftDownloadArtifact(
            GetOptionalString(element, "path"),
            GetOptionalString(element, "url"),
            GetOptionalString(element, "sha1"),
            size);
    }

    private static MinecraftLibraryExtract? ParseLibraryExtract(JsonElement libraryElement)
    {
        if (!libraryElement.TryGetProperty("extract", out var extractElement) ||
            extractElement.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (extractElement.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("'libraries[].extract' must be a JSON object.");
        }

        if (!extractElement.TryGetProperty("exclude", out var excludeElement) ||
            excludeElement.ValueKind is JsonValueKind.Null)
        {
            return new MinecraftLibraryExtract(Array.Empty<string>());
        }

        if (excludeElement.ValueKind is not JsonValueKind.Array)
        {
            throw new JsonException("'libraries[].extract.exclude' must be a JSON array.");
        }

        var excludes = new List<string>();
        foreach (var item in excludeElement.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String)
            {
                throw new JsonException("'libraries[].extract.exclude' values must be strings.");
            }

            excludes.Add(item.GetString()!);
        }

        return new MinecraftLibraryExtract(excludes);
    }

    private static IReadOnlyDictionary<string, string> ParseNatives(JsonElement libraryElement)
    {
        if (!libraryElement.TryGetProperty("natives", out var nativesElement) ||
            nativesElement.ValueKind is JsonValueKind.Null)
        {
            return new Dictionary<string, string>();
        }

        if (nativesElement.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("'libraries[].natives' must be a JSON object.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in nativesElement.EnumerateObject())
        {
            if (property.Value.ValueKind is not JsonValueKind.String)
            {
                throw new JsonException(
                    "'libraries[].natives' values must be strings.");
            }

            result[property.Name] = property.Value.GetString()!;
        }

        return result;
    }

    private static IReadOnlyList<MinecraftRule> ParseRules(JsonElement rulesElement)
    {
        if (rulesElement.ValueKind is not JsonValueKind.Array)
        {
            throw new JsonException("'rules' must be a JSON array.");
        }

        var rules = new List<MinecraftRule>();

        foreach (var ruleElement in rulesElement.EnumerateArray())
        {
            if (ruleElement.ValueKind is not JsonValueKind.Object)
            {
                throw new JsonException("Each rule must be a JSON object.");
            }

            var action = GetRequiredString(ruleElement, "action") switch
            {
                "allow" => MinecraftRuleAction.Allow,
                "disallow" => MinecraftRuleAction.Disallow,
                var value => throw new JsonException(
                    $"Unsupported Minecraft rule action '{value}'.")
            };

            MinecraftOsRule? os = null;

            if (ruleElement.TryGetProperty("os", out var osElement))
            {
                if (osElement.ValueKind is not JsonValueKind.Object)
                {
                    throw new JsonException("'rules[].os' must be a JSON object.");
                }

                os = new MinecraftOsRule(
                    GetOptionalString(osElement, "name"),
                    GetOptionalString(osElement, "arch"),
                    GetOptionalString(osElement, "version"));
            }

            IReadOnlyDictionary<string, bool>? features = null;

            if (ruleElement.TryGetProperty("features", out var featuresElement))
            {
                if (featuresElement.ValueKind is not JsonValueKind.Object)
                {
                    throw new JsonException("'rules[].features' must be a JSON object.");
                }

                var parsedFeatures = new Dictionary<string, bool>();

                foreach (var property in featuresElement.EnumerateObject())
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and
                        not JsonValueKind.False)
                    {
                        throw new JsonException(
                            "'rules[].features' values must be booleans.");
                    }

                    parsedFeatures[property.Name] = property.Value.GetBoolean();
                }

                features = parsedFeatures;
            }

            rules.Add(new MinecraftRule(action, os, features));
        }

        return rules;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var value = GetOptionalString(element, propertyName);

        return value ?? throw new JsonException(
            $"Required string property '{propertyName}' is missing.");
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind is not JsonValueKind.String)
        {
            throw new JsonException($"'{propertyName}' must be a string.");
        }

        return property.GetString();
    }
}
