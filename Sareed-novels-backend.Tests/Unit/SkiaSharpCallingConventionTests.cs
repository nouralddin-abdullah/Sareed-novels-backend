using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Production runs in a 32-bit IIS app pool, where SkiaSharp's default convention P/Invokes don't match the cdecl
/// exports of libSkiaSharp (https://github.com/mono/SkiaSharp/issues/5168); Directory.Build.targets gives the build and
/// the publish a SkiaSharp.dll whose libSkiaSharp P/Invokes are cdecl. Nothing on 64-bit would show it if that stopped
/// happening (e.g. after a SkiaSharp upgrade that changes how its assembly is resolved), so these tests check it.
/// </summary>
public class SkiaSharpCallingConventionTests
{
    [Fact]
    public void Skia_calls_are_cdecl_and_its_windows_api_calls_stay_stdcall()
    {
        var pinvokes = typeof(SKImage).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => (Method: m, Import: m.GetCustomAttribute<DllImportAttribute>()!))
            .ToList();

        var skia = pinvokes.Where(p => p.Import.Value == "libSkiaSharp").ToList();
        Assert.True(skia.Count > 500, $"only {skia.Count} libSkiaSharp P/Invokes found");
        var notCdecl = skia.Where(p => p.Import.CallingConvention != CallingConvention.Cdecl).ToList();
        Assert.True(notCdecl.Count == 0,
            $"{notCdecl.Count} libSkiaSharp P/Invokes are not cdecl (first: {notCdecl.FirstOrDefault().Method?.Name}); the SkiaSharp.dll in bin isn't the patched one");

        // Kernel32, ole32 and libEGL functions really are stdcall on 32-bit Windows.
        var windowsApi = pinvokes.Where(p => p.Import.Value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(windowsApi);
        Assert.All(windowsApi, p => Assert.Equal(CallingConvention.Winapi, p.Import.CallingConvention));
    }

    [Fact]
    public void The_patch_finds_the_same_pinvoke_table_as_the_runtime_metadata_reader()
    {
        // CoreLib has every kind of table and large heaps, so every column size rule is exercised.
        foreach (var assembly in new[] { typeof(object).Assembly, typeof(SKImage).Assembly, typeof(SkiaSharpCallingConventionTests).Assembly })
        {
            var image = File.ReadAllBytes(assembly.Location);

            var table = SkiaSharpCdeclPatch.FindImplMap(image);

            using var pe = new PEReader(new MemoryStream(image));
            var metadata = pe.GetMetadataReader();
            Assert.Equal(pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.ImplMap), table.Offset);
            Assert.Equal(metadata.GetTableRowSize(TableIndex.ImplMap), table.RowSize);
            Assert.Equal(metadata.GetTableRowCount(TableIndex.ImplMap), table.Rows);
        }
    }

    [Fact]
    public void The_patch_changes_only_the_default_convention_pinvokes_into_the_given_library()
    {
        var original = File.ReadAllBytes(typeof(object).Assembly.Location);
        var before = Imports(original);
        var library = before.Values.Where(i => i.Convention == MethodImportAttributes.CallingConventionWinApi)
            .GroupBy(i => i.Library).OrderByDescending(g => g.Count()).First().Key;
        Assert.Contains(before.Values, i => i.Library != library);

        var patched = (byte[])original.Clone();
        var changed = SkiaSharpCdeclPatch.Apply(patched, library);

        var after = Imports(patched);
        Assert.Equal(before.Values.Count(i => i.Library == library && i.Convention == MethodImportAttributes.CallingConventionWinApi), changed);
        foreach (var (method, import) in before)
        {
            var expected = import.Library == library && import.Convention == MethodImportAttributes.CallingConventionWinApi
                ? MethodImportAttributes.CallingConventionCDecl
                : import.Convention;
            Assert.Equal(expected, after[method].Convention);
        }

        // Only the two flag bytes of the changed rows differ.
        var table = SkiaSharpCdeclPatch.FindImplMap(original);
        var differing = Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]).ToList();
        Assert.NotEmpty(differing);
        Assert.All(differing, i => Assert.InRange((i - table.Offset) % table.RowSize, 0, 1));
        Assert.All(differing, i => Assert.InRange(i, table.Offset, table.Offset + table.Rows * table.RowSize - 1));

        // Patching again changes nothing.
        Assert.Equal(0, SkiaSharpCdeclPatch.Apply(patched, library));
    }

    private static Dictionary<MethodDefinitionHandle, (string Library, MethodImportAttributes Convention)> Imports(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        var metadata = pe.GetMetadataReader();
        return metadata.MethodDefinitions
            .Select(h => (Handle: h, Import: metadata.GetMethodDefinition(h).GetImport()))
            .Where(m => !m.Import.Module.IsNil)
            .ToDictionary(
                m => m.Handle,
                m => (metadata.GetString(metadata.GetModuleReference(m.Import.Module).Name),
                      m.Import.Attributes & MethodImportAttributes.CallingConventionMask));
    }
}
