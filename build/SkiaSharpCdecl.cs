// Compiled by Directory.Build.targets as an inline MSBuild task (RoslynCodeTaskFactory, so it has to build with the
// C# and .NET Framework that Visual Studio's MSBuild offers: no records, spans or System.Reflection.Metadata) and by
// the test project (with SARD_TESTS defined, which leaves out the MSBuild task).
using System;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Marks the P/Invokes of an assembly into one native library that use the platform default calling convention
/// (Winapi) as cdecl, by editing the flags of its ImplMap metadata table in place. Nothing else in the file changes.
/// </summary>
/// <remarks>
/// SkiaSharp 3 and 4 declare their libSkiaSharp P/Invokes with [LibraryImport] and no calling convention, which is
/// stdcall on 32-bit Windows, while libSkiaSharp.dll exports cdecl functions
/// (https://github.com/mono/SkiaSharp/issues/5168). Its P/Invokes into Windows DLLs (Kernel32, ole32, libEGL) really
/// are stdcall and are left alone, as are explicit conventions, so the patch is a no-op once upstream fixes it.
/// </remarks>
public static class SkiaSharpCdeclPatch
{
    private const int CallConvMask = 0x0700;
    private const int CallConvWinapi = 0x0100;
    private const int CallConvCdecl = 0x0200;

    private const int ModuleRef = 0x1A;
    private const int ImplMap = 0x1C;

    /// <summary>
    /// Patches the assembly <paramref name="image"/> in place and returns how many P/Invokes into
    /// <paramref name="library"/> (the DllImport name, e.g. libSkiaSharp) changed.
    /// </summary>
    /// <exception cref="BadImageFormatException">The bytes aren't a .NET assembly.</exception>
    public static int Apply(byte[] image, string library)
    {
        var metadata = new MetadataLayout(image);
        var implMap = metadata.Table(ImplMap);
        var moduleRefs = metadata.Table(ModuleRef);
        var importScope = 2 + metadata.Sizes.Coded(1, 0x04, 0x06) + metadata.Sizes.StringIndex; // after flags, member, name

        var changed = 0;
        for (var row = 0; row < implMap.Rows; row++)
        {
            var at = implMap.Offset + row * implMap.RowSize;
            var flags = U16(image, at);
            if ((flags & CallConvMask) != CallConvWinapi) continue;

            var module = Read(image, at + importScope, metadata.Sizes.Index(ModuleRef));
            var name = metadata.String(Read(image, moduleRefs.Offset + (module - 1) * moduleRefs.RowSize, metadata.Sizes.StringIndex));
            if (!IsLibrary(name, library)) continue;

            flags = (flags & ~CallConvMask) | CallConvCdecl;
            image[at] = (byte)flags;
            image[at + 1] = (byte)(flags >> 8);
            changed++;
        }
        return changed;
    }

    /// <summary>File offset, row size and row count of the ImplMap table (ECMA-335 II.22.22).</summary>
    public static TableLocation FindImplMap(byte[] image) => new MetadataLayout(image).Table(ImplMap);

    private static bool IsLibrary(string name, string library) =>
        string.Equals(name, library, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, library + ".dll", StringComparison.OrdinalIgnoreCase);

    private static int U16(byte[] b, int at) => b[at] | b[at + 1] << 8;

    private static int I32(byte[] b, int at) => b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24;

    private static int Read(byte[] b, int at, int size) => size == 2 ? U16(b, at) : I32(b, at);

    /// <summary>Where the metadata tables and the string heap are in an assembly file (ECMA-335 II.24, II.25).</summary>
    private sealed class MetadataLayout
    {
        private readonly byte[] image;
        private readonly int strings;
        private readonly int firstTable;
        private readonly int[] rows = new int[64];

        public MetadataLayout(byte[] image)
        {
            this.image = image;

            // PE headers: the CLI header is data directory 14.
            var pe = I32(image, 0x3C);
            if (I32(image, pe) != 0x00004550) throw new BadImageFormatException("Not a PE file.");
            var coff = pe + 4;
            var sectionCount = U16(image, coff + 2);
            var optional = coff + 20;
            var sections = optional + U16(image, coff + 16);
            var directories = optional + (U16(image, optional) == 0x20B ? 112 : 96);
            var cliRva = I32(image, directories + 14 * 8);
            if (cliRva == 0) throw new BadImageFormatException("Not a .NET assembly.");

            Func<int, int> fileOffset = rva =>
            {
                for (var i = 0; i < sectionCount; i++)
                {
                    var section = sections + i * 40;
                    var address = I32(image, section + 12);
                    var size = Math.Max(I32(image, section + 8), I32(image, section + 16));
                    if (rva >= address && rva < address + size) return rva - address + I32(image, section + 20);
                }
                throw new BadImageFormatException("RVA outside every section.");
            };

            // Metadata root and stream headers.
            var metadata = fileOffset(I32(image, fileOffset(cliRva) + 8));
            if (I32(image, metadata) != 0x424A5342) throw new BadImageFormatException("No metadata signature.");
            var at = metadata + 16 + I32(image, metadata + 12);
            var streamCount = U16(image, at + 2);
            at += 4;
            int tables = -1, stringHeap = -1;
            for (var i = 0; i < streamCount; i++)
            {
                var streamOffset = I32(image, at);
                var name = at + 8;
                var end = Array.IndexOf(image, (byte)0, name);
                var streamName = Encoding.ASCII.GetString(image, name, end - name);
                if (streamName == "#~" || streamName == "#-") tables = metadata + streamOffset;
                if (streamName == "#Strings") stringHeap = metadata + streamOffset;
                at = name + ((end - name + 4) & ~3);
            }
            if (tables < 0 || stringHeap < 0) throw new BadImageFormatException("No metadata tables or string heap.");
            strings = stringHeap;

            // Tables stream header: row counts of the tables present, then the tables in order.
            var heapSizes = image[tables + 6];
            var valid = BitConverter.ToUInt64(image, tables + 8);
            at = tables + 24;
            for (var t = 0; t < 64; t++)
            {
                if ((valid & (1UL << t)) == 0) continue;
                rows[t] = I32(image, at);
                at += 4;
            }
            if ((heapSizes & 0x40) != 0) at += 4; // extra data, honoured by the runtime's metadata reader
            firstTable = at;
            Sizes = new ColumnSizes(heapSizes, rows);
        }

        public ColumnSizes Sizes { get; private set; }

        public TableLocation Table(int table)
        {
            var offset = firstTable;
            for (var t = 0; t < table; t++) offset += rows[t] * Sizes.RowSize(t);
            return new TableLocation(offset, Sizes.RowSize(table), rows[table]);
        }

        public string String(int index)
        {
            var start = strings + index;
            return Encoding.UTF8.GetString(image, start, Array.IndexOf(image, (byte)0, start) - start);
        }
    }

    /// <summary>Column and row sizes of the tables up to ImplMap (II.22.2 to II.22.22).</summary>
    private sealed class ColumnSizes
    {
        private readonly int[] rows;
        private readonly bool allLarge;
        private readonly int guidIndex, blobIndex;

        public ColumnSizes(int heapSizes, int[] rows)
        {
            this.rows = rows;
            allLarge = (heapSizes & 0x20) != 0; // EnC delta: every index is 4 bytes
            StringIndex = allLarge || (heapSizes & 1) != 0 ? 4 : 2;
            guidIndex = allLarge || (heapSizes & 2) != 0 ? 4 : 2;
            blobIndex = allLarge || (heapSizes & 4) != 0 ? 4 : 2;
        }

        public int StringIndex { get; private set; }

        public int RowSize(int table)
        {
            int s = StringIndex, g = guidIndex, b = blobIndex;
            switch (table)
            {
                case 0x00: return 2 + s + 3 * g;                                              // Module
                case 0x01: return Coded(2, 0x00, 0x1A, 0x23, 0x01) + 2 * s;                   // TypeRef
                case 0x02: return 4 + 2 * s + TypeDefOrRef() + List(0x03, 0x04) + List(0x05, 0x06); // TypeDef
                case 0x03: return Index(0x04);                                                // FieldPtr
                case 0x04: return 2 + s + b;                                                  // Field
                case 0x05: return Index(0x06);                                                // MethodPtr
                case 0x06: return 8 + s + b + List(0x07, 0x08);                               // MethodDef
                case 0x07: return Index(0x08);                                                // ParamPtr
                case 0x08: return 4 + s;                                                      // Param
                case 0x09: return Index(0x02) + TypeDefOrRef();                               // InterfaceImpl
                case 0x0A: return Coded(3, 0x02, 0x01, 0x1A, 0x06, 0x1B) + s + b;             // MemberRef
                case 0x0B: return 2 + Coded(2, 0x04, 0x08, 0x17) + b;                         // Constant
                case 0x0C: return Coded(5, 0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x00, 0x0E, 0x17, 0x14, 0x11,
                                        0x1A, 0x1B, 0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2C, 0x2B)
                                  + Coded(3, 0x06, 0x0A) + b;                                 // CustomAttribute
                case 0x0D: return Coded(1, 0x04, 0x08) + b;                                   // FieldMarshal
                case 0x0E: return 2 + Coded(2, 0x02, 0x06, 0x20) + b;                         // DeclSecurity
                case 0x0F: return 6 + Index(0x02);                                            // ClassLayout
                case 0x10: return 4 + Index(0x04);                                            // FieldLayout
                case 0x11: return b;                                                          // StandAloneSig
                case 0x12: return Index(0x02) + List(0x13, 0x14);                             // EventMap
                case 0x13: return Index(0x14);                                                // EventPtr
                case 0x14: return 2 + s + TypeDefOrRef();                                     // Event
                case 0x15: return Index(0x02) + List(0x16, 0x17);                             // PropertyMap
                case 0x16: return Index(0x17);                                                // PropertyPtr
                case 0x17: return 2 + s + b;                                                  // Property
                case 0x18: return 2 + Index(0x06) + Coded(1, 0x14, 0x17);                     // MethodSemantics
                case 0x19: return Index(0x02) + 2 * Coded(1, 0x06, 0x0A);                     // MethodImpl
                case 0x1A: return s;                                                          // ModuleRef
                case 0x1B: return b;                                                          // TypeSpec
                case 0x1C: return 2 + Coded(1, 0x04, 0x06) + s + Index(0x1A);                 // ImplMap
                default: throw new ArgumentOutOfRangeException("table");
            }
        }

        public int Index(int table) => allLarge || rows[table] >= 1 << 16 ? 4 : 2;

        public int Coded(int tagBits, params int[] tables) =>
            allLarge || tables.Max(t => rows[t]) >= 1 << (16 - tagBits) ? 4 : 2;

        private int TypeDefOrRef() => Coded(2, 0x02, 0x01, 0x1B);

        /// <summary>A list column (TypeDef.FieldList and the like), sized the way the runtime's reader sizes it.</summary>
        private int List(int pointerTable, int table) => Index(pointerTable) == 4 ? 4 : Index(table);
    }
}

/// <summary>Where a metadata table is in the file.</summary>
public sealed class TableLocation
{
    public TableLocation(int offset, int rowSize, int rows)
    {
        Offset = offset;
        RowSize = rowSize;
        Rows = rows;
    }

    public int Offset { get; private set; }
    public int RowSize { get; private set; }
    public int Rows { get; private set; }
}

#if !SARD_TESTS
/// <summary>Writes a copy of <see cref="Source"/> whose P/Invokes into <see cref="Library"/> are cdecl to <see cref="Destination"/>.</summary>
public class SetCdeclCallingConvention : Microsoft.Build.Utilities.Task
{
    [Microsoft.Build.Framework.Required]
    public string Source { get; set; }

    [Microsoft.Build.Framework.Required]
    public string Destination { get; set; }

    [Microsoft.Build.Framework.Required]
    public string Library { get; set; }

    public override bool Execute()
    {
        var image = File.ReadAllBytes(Source);
        var changed = SkiaSharpCdeclPatch.Apply(image, Library);

        // Rewrite only when the result differs, so incremental builds don't copy an unchanged file again.
        if (!File.Exists(Destination) || !File.ReadAllBytes(Destination).SequenceEqual(image))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Destination));
            File.WriteAllBytes(Destination, image);
        }

        Log.LogMessage(Microsoft.Build.Framework.MessageImportance.Normal,
            "{0}: {1} P/Invokes into {2} marked cdecl in {3}", GetType().Name, changed, Library, Destination);
        return true;
    }
}
#endif
