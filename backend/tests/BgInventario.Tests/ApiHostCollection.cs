namespace BgInventario.Tests;

// Serilog's static bootstrap logger (Program.cs) fails if two hosts start in parallel.
[CollectionDefinition(Name)]
public sealed class ApiHostCollection
{
    public const string Name = "API host";
}
