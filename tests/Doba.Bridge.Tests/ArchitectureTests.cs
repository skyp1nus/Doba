using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Doba.Bridge.Tests;

/// <summary>Doba.Bridge and Doba.Mcp stay small and safe: no UI, and nothing that could start Doba (or anything else)
/// on the real machine.</summary>
public sealed class ArchitectureTests
{
    public static TheoryData<string> Assemblies => new()
    {
        typeof(BridgeNames).Assembly.Location,
        TestPaths.BuiltMcpDll,
    };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void References_neither_the_app_nor_avalonia(string path)
    {
        var references = ReadMetadata(path, md => md.AssemblyReferences
            .Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList());

        Assert.NotEmpty(references);
        Assert.DoesNotContain("Doba", references); // Doba.App's assembly name
        Assert.DoesNotContain(references, r => r.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain("Doba.Storage", references); // the server never opens the database
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Never_starts_a_process(string path)
    {
        var calls = ReadMetadata(path, md => md.MemberReferences
            .Select(h => md.GetMemberReference(h))
            .Where(m => m.Parent.Kind == HandleKind.TypeReference)
            .Select(m =>
            {
                var type = md.GetTypeReference((TypeReferenceHandle)m.Parent);
                return md.GetString(type.Namespace) + "." + md.GetString(type.Name) + "::" + md.GetString(m.Name);
            })
            .ToList());

        Assert.DoesNotContain("System.Diagnostics.Process::Start", calls);
    }

    [Fact]
    public void Mcp_output_folder_has_no_ui_assemblies()
    {
        var folder = Path.GetDirectoryName(TestPaths.BuiltMcpDll)!;
        Assert.True(File.Exists(TestPaths.BuiltMcpDll), "Doba.Mcp was not built: " + TestPaths.BuiltMcpDll);
        var files = Directory.GetFiles(folder, "*.dll").Select(Path.GetFileName).ToList();
        Assert.DoesNotContain(files, f => f!.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Doba.dll", files, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Doba.Storage.dll", files, StringComparer.OrdinalIgnoreCase);
    }

    private static T ReadMetadata<T>(string path, Func<MetadataReader, T> read)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return read(pe.GetMetadataReader());
    }
}
