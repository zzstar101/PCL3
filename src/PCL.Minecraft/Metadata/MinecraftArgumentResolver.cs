namespace PCL3.Minecraft.Metadata;

public static class MinecraftArgumentResolver
{
    public static IReadOnlyList<string> Resolve(
        IEnumerable<MinecraftArgument> arguments,
        MinecraftRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var result = new List<string>();

        foreach (var argument in arguments)
        {
            if (argument.Rules.Count == 0 ||
                MinecraftRuleEvaluator.IsAllowed(argument.Rules, context))
            {
                result.AddRange(argument.Values);
            }
        }

        return result;
    }
}
