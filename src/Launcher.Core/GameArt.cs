using System.Buffers.Binary;
using System.Text;

namespace DieReanimated.Launcher;

/// <summary>
/// Key art, read out of the game's own asset files at start-up. The launcher ships no game artwork: what it shows
/// on Home and in the match cards is decoded from the player's install (<c>resources.assets</c>,
/// <c>sharedassets0/1.assets</c> — Unity 4.6 serialized files, format 9) each time it needs it.
///
/// What can be named: a whole <c>Texture2D</c> (<c>"GameMode_Big_Scavenger"</c>), or one sprite of the crib's
/// NGUI atlases (<c>"ShopCardAtlas#ShopCard_Berg_Survivor"</c>, or just <c>"#ShopCard_Berg_Survivor"</c> to search
/// every atlas). Only the formats these files use are decoded (RGB24, RGBA32, ARGB32, DXT1, DXT5), and only the
/// region asked for — a sprite never decodes its 4096² atlas. No library: the file format and the decoders are
/// the few hundred lines below, which keeps a player-facing binary free of a dependency for a purely decorative
/// feature. Anything unexpected → <c>null</c>.
/// </summary>
public static class GameArt
{
    public sealed record Entry(string Name, string File, long Offset, int Width, int Height, int Format);
    public sealed record Sprite(string Name, int X, int Y, int Width, int Height, bool Rotated);
    /// <summary>One NGUI atlas: its sprite table and the texture it cuts from.</summary>
    public sealed record Atlas(string Name, string TextureName, IReadOnlyList<Sprite> Sprites);
    public sealed record Catalogue(IReadOnlyList<Entry> Textures, IReadOnlyList<Atlas> Atlases)
    {
        public Entry? Texture(string name) => Textures.FirstOrDefault(e => e.Name == name);
    }
    /// <summary>Decoded pixels, top-down, 4 bytes per pixel in B G R A order.</summary>
    public sealed record Texture(string Name, int Width, int Height, byte[] Bgra);

    private static readonly string[] AssetFiles = { "resources.assets", "sharedassets0.assets", "sharedassets1.assets" };
    private const int MaterialClassId = 21, Texture2DClassId = 28, MonoBehaviourClassId = 114;
    private const int SerializedFormat = 9;

    // Unity TextureFormat
    private const int RGB24 = 3, RGBA32 = 4, ARGB32 = 5, DXT1 = 10, DXT5 = 12;

    public static string DataDir(string gameDir) => Path.Combine(gameDir, "Dead Island Epidemic - Crib_Data");

    /// <summary>Every Texture2D and every NGUI atlas, with where they are. Headers and sprite tables only.</summary>
    public static Catalogue Index(string gameDir)
    {
        var textures = new List<Entry>();
        var atlases = new List<Atlas>();
        foreach (string file in AssetFiles)
        {
            string path = Path.Combine(DataDir(gameDir), file);
            if (!File.Exists(path)) continue;
            try
            {
                using var fs = File.OpenRead(path);
                var objects = Objects(fs);
                var materials = new Dictionary<long, string>();
                foreach (var o in objects)
                {
                    fs.Position = o.offset;
                    switch (o.classId)
                    {
                        case Texture2DClassId:
                            if (ReadHeader(fs, o.offset) is { } h) textures.Add(new Entry(h.name, path, o.offset, h.width, h.height, h.format));
                            break;
                        case MaterialClassId:
                            try { materials[o.pathId] = new Reader(fs, o.offset).AlignedString(); } catch (InvalidDataException) { }
                            break;
                    }
                }
                foreach (var o in objects)
                {
                    if (o.classId != MonoBehaviourClassId || o.size < 3000) continue;
                    fs.Position = o.offset;
                    if (ReadAtlas(fs, o.offset, o.size, materials) is { } a) atlases.Add(a);
                }
            }
            catch (Exception e) when (e is IOException or EndOfStreamException or InvalidDataException) { /* not the files we know */ }
        }
        return new Catalogue(textures, atlases);
    }

    /// <summary>Decode what a spec names — <c>"Texture"</c>, <c>"Atlas#Sprite"</c> or <c>"#Sprite"</c> — or <c>null</c>.</summary>
    public static Texture? Load(string gameDir, string spec) => Load(Index(gameDir), spec);

    public static Texture? Load(Catalogue cat, string spec)
    {
        int hash = spec.IndexOf('#');
        if (hash < 0)
            return cat.Texture(spec) is { } e ? Load(e) : null;
        string atlasName = spec[..hash], spriteName = spec[(hash + 1)..];
        foreach (var atlas in cat.Atlases)
        {
            if (atlasName.Length > 0 && atlas.Name != atlasName) continue;
            var sprite = atlas.Sprites.FirstOrDefault(s => s.Name == spriteName);
            if (sprite is null || cat.Texture(atlas.TextureName) is not { } tex) continue;
            var region = LoadRegion(tex, sprite.X, sprite.Y, sprite.Width, sprite.Height);
            if (region is null) return null;
            return sprite.Rotated ? RotateLeft(region with { Name = spec }) : region with { Name = spec };
        }
        return null;
    }

    public static Texture? Load(Entry e) => LoadRegion(e, 0, 0, e.Width, e.Height);

    /// <summary>Decode a rectangle of a texture (top-down coordinates), reading only the rows it covers.</summary>
    public static Texture? LoadRegion(Entry e, int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > e.Width || y + h > e.Height) return null;
        try
        {
            using var fs = File.OpenRead(e.File);
            fs.Position = e.Offset;
            var hdr = ReadHeader(fs, e.Offset);
            if (hdr is null) return null;
            long data = fs.Position;                     // first image byte; mip 0, stored bottom-up
            byte[] bgra;
            switch (hdr.Value.format)
            {
                case RGB24 or RGBA32 or ARGB32:
                {
                    int bpp = hdr.Value.format == RGB24 ? 3 : 4;
                    long stride = (long)e.Width * bpp;
                    if (stride * e.Height > hdr.Value.imageSize) return null;
                    byte[] rows = new byte[w * h * bpp];
                    for (int r = 0; r < h; r++)
                    {
                        int fileRow = e.Height - 1 - (y + r);           // bottom-up → this output row
                        fs.Position = data + fileRow * stride + (long)x * bpp;
                        fs.ReadExactly(rows, r * w * bpp, w * bpp);
                    }
                    bgra = hdr.Value.format switch
                    {
                        RGB24 => FromRgb24(rows, w, h),
                        RGBA32 => FromRgba32(rows, w, h, argb: false),
                        _ => FromRgba32(rows, w, h, argb: true),
                    };
                    break;
                }
                case DXT1 or DXT5:
                {
                    bool dxt5 = hdr.Value.format == DXT5;
                    int bs = dxt5 ? 16 : 8, bw = Math.Max(1, e.Width / 4), bh = Math.Max(1, e.Height / 4);
                    if ((long)bw * bh * bs > hdr.Value.imageSize) return null;
                    // the block rows covering the region, in file order (bottom-up)
                    int fileY0 = e.Height - (y + h), fileY1 = e.Height - y;
                    int b0 = fileY0 / 4, b1 = Math.Min(bh, (fileY1 + 3) / 4);
                    byte[] blocks = new byte[(b1 - b0) * bw * bs];
                    fs.Position = data + (long)b0 * bw * bs;
                    fs.ReadExactly(blocks);
                    byte[] band = FromDxt(blocks, e.Width, (b1 - b0) * 4, dxt5);   // still bottom-up
                    bgra = new byte[w * h * 4];
                    for (int r = 0; r < h; r++)
                    {
                        int fileRow = e.Height - 1 - (y + r) - b0 * 4;
                        Buffer.BlockCopy(band, (fileRow * e.Width + x) * 4, bgra, r * w * 4, w * 4);
                    }
                    break;
                }
                default:
                    return null;
            }
            return new Texture(e.Name, w, h, bgra);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException) { return null; }
    }

    // ── serialized file (format 9) ───────────────────────────────────────────────────────────────────

    /// <summary>The object table: (path id, class id, absolute data offset, byte size).</summary>
    private static List<(long pathId, int classId, long offset, uint size)> Objects(FileStream fs)
    {
        Span<byte> hdr = stackalloc byte[20];
        fs.ReadExactly(hdr);
        uint version = BinaryPrimitives.ReadUInt32BigEndian(hdr[8..]);
        uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(hdr[12..]);
        if (version != SerializedFormat || hdr[16] != 0) throw new InvalidDataException("not a little-endian Unity 4 serialized file");

        var r = new Reader(fs);
        r.CString();                       // unity version
        r.I32();                           // target platform
        int types = r.I32();
        for (int i = 0; i < types; i++) { r.I32(); SkipTypeTree(r); }
        bool bigIds = r.I32() != 0;
        int objects = r.I32();
        var found = new List<(long, int, long, uint)>();
        for (int i = 0; i < objects; i++)
        {
            long pathId = bigIds ? r.I64() : r.I32();
            uint byteStart = r.U32();
            uint byteSize = r.U32();
            r.I32();                                    // type id
            ushort classId = r.U16();
            r.U16();                                    // is destroyed
            found.Add((pathId, classId, dataOffset + byteStart, byteSize));
        }
        return found;
    }

    /// <summary>An NGUI <c>UIAtlas</c> MonoBehaviour, parsed from its raw bytes (this Unity 4 build ships no type
    /// tree for scripts): the header (GameObject and Script PPtrs, enabled, name, material PPtr), then the sprite
    /// count and per sprite {name; outer rect; inner rect; rotated; padding}. Rects are pixels from the atlas's
    /// TOP-LEFT. Anything that does not fit that shape is not an atlas.</summary>
    private static Atlas? ReadAtlas(FileStream fs, long start, uint size, Dictionary<long, string> materials)
    {
        try
        {
            var r = new Reader(fs, start);
            r.Skip(8 + 4 + 8);                          // m_GameObject, m_Enabled (+pad), m_Script
            r.AlignedString();                          // m_Name (empty in these files)
            r.I32(); long materialId = r.I32();         // material PPtr (file id, path id)
            int count = r.I32();
            if (count is <= 5 or >= 5000) return null;
            if (!materials.TryGetValue(materialId, out string? textureName) || textureName.Length == 0) return null;
            var sprites = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                string name = r.AlignedString();
                float x = r.F32(), y = r.F32(), w = r.F32(), h = r.F32();
                r.Skip(16);                             // inner rect
                bool rotated = r.U32() != 0;
                r.Skip(16);                             // padding (4 floats)
                if (name.Length == 0 || w < 0 || h < 0 || w > 4096 || h > 4096) return null;
                sprites.Add(new Sprite(name, (int)x, (int)y, (int)w, (int)h, rotated));
            }
            if (fs.Position - start > size) return null;
            return new Atlas(textureName, textureName, sprites);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException) { return null; }
    }

    private static void SkipTypeTree(Reader r)
    {
        r.CString(); r.CString();                       // type, name
        r.I32(); r.I32(); r.I32(); r.I32(); r.I32();    // size, index, isArray, version, metaFlag
        int children = r.I32();
        for (int i = 0; i < children; i++) SkipTypeTree(r);
    }

    /// <summary>Texture2D as Unity 4.6 lays it out; the stream is left at the first image byte.</summary>
    private static (string name, int width, int height, int format, int imageSize)? ReadHeader(FileStream fs, long objectStart)
    {
        var r = new Reader(fs, objectStart);
        string name = r.AlignedString();
        int width = r.I32(), height = r.I32();
        r.I32();                                        // complete image size
        int format = r.I32();
        r.Skip(3); r.Align();                           // mipmap, readable, read-allowed
        r.I32(); r.I32();                               // image count, dimension
        r.Skip(16);                                     // texture settings (filter, aniso, mip bias, wrap)
        r.I32(); r.I32();                               // lightmap format, colour space
        int imageSize = r.I32();
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || imageSize < 0) return null;
        return (name, width, height, format, imageSize);
    }

    private sealed class Reader
    {
        private readonly FileStream _fs;
        private readonly long _base;
        private readonly byte[] _buf = new byte[8];
        public Reader(FileStream fs, long? baseOffset = null) { _fs = fs; _base = baseOffset ?? 0; }
        private ReadOnlySpan<byte> Take(int n) { _fs.ReadExactly(_buf, 0, n); return _buf.AsSpan(0, n); }
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public void Skip(int n) => _fs.Position += n;
        public void Align() { long rel = _fs.Position - _base; long pad = (4 - rel % 4) % 4; _fs.Position += pad; }
        public string CString()
        {
            var sb = new List<byte>();
            int b;
            while ((b = _fs.ReadByte()) > 0) sb.Add((byte)b);
            if (b < 0) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(sb.ToArray());
        }
        public string AlignedString()
        {
            int len = I32();
            if (len < 0 || len > 4096) throw new InvalidDataException("string length");
            byte[] s = new byte[len];
            _fs.ReadExactly(s);
            Align();
            return Encoding.UTF8.GetString(s);
        }
    }

    // ── pixel formats ────────────────────────────────────────────────────────────────────────────────

    private static byte[] FromRgb24(byte[] d, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0, j = 0; i < w * h; i++, j += 3) { o[i * 4] = d[j + 2]; o[i * 4 + 1] = d[j + 1]; o[i * 4 + 2] = d[j]; o[i * 4 + 3] = 255; }
        return o;
    }

    private static byte[] FromRgba32(byte[] d, int w, int h, bool argb)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int j = i * 4;
            byte r, g, b, a;
            if (argb) { a = d[j]; r = d[j + 1]; g = d[j + 2]; b = d[j + 3]; }
            else { r = d[j]; g = d[j + 1]; b = d[j + 2]; a = d[j + 3]; }
            o[j] = b; o[j + 1] = g; o[j + 2] = r; o[j + 3] = a;
        }
        return o;
    }

    /// <summary>S3TC block decode: 4×4 blocks, two RGB565 endpoints + 2-bit indices; DXT5 adds an 8-entry alpha ramp.</summary>
    private static byte[] FromDxt(byte[] d, int w, int h, bool dxt5)
    {
        byte[] o = new byte[w * h * 4];
        int bw = Math.Max(1, w / 4), bh = Math.Max(1, h / 4), bs = dxt5 ? 16 : 8;
        Span<byte> alpha = stackalloc byte[8];
        Span<(byte r, byte g, byte b)> c = stackalloc (byte, byte, byte)[4];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                int p = (by * bw + bx) * bs;
                ulong alphaBits = 0;
                if (dxt5)
                {
                    alpha[0] = d[p]; alpha[1] = d[p + 1];
                    if (alpha[0] > alpha[1])
                        for (int i = 2; i < 8; i++) alpha[i] = (byte)(((8 - i) * alpha[0] + (i - 1) * alpha[1]) / 7);
                    else
                    {
                        for (int i = 2; i < 6; i++) alpha[i] = (byte)(((6 - i) * alpha[0] + (i - 1) * alpha[1]) / 5);
                        alpha[6] = 0; alpha[7] = 255;
                    }
                    for (int i = 0; i < 6; i++) alphaBits |= (ulong)d[p + 2 + i] << (8 * i);
                    p += 8;
                }
                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p)), c1 = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p + 2));
                c[0] = Rgb565(c0); c[1] = Rgb565(c1);
                if (c0 > c1 || dxt5)
                {
                    c[2] = ((byte)((2 * c[0].r + c[1].r) / 3), (byte)((2 * c[0].g + c[1].g) / 3), (byte)((2 * c[0].b + c[1].b) / 3));
                    c[3] = ((byte)((c[0].r + 2 * c[1].r) / 3), (byte)((c[0].g + 2 * c[1].g) / 3), (byte)((c[0].b + 2 * c[1].b) / 3));
                }
                else
                {
                    c[2] = ((byte)((c[0].r + c[1].r) / 2), (byte)((c[0].g + c[1].g) / 2), (byte)((c[0].b + c[1].b) / 2));
                    c[3] = (0, 0, 0);
                }
                uint idx = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 4));
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int px = bx * 4 + x, py = by * 4 + y;
                        if (px >= w || py >= h) continue;
                        int i = y * 4 + x;
                        var col = c[(int)((idx >> (2 * i)) & 3)];
                        byte a = dxt5 ? alpha[(int)((alphaBits >> (3 * i)) & 7)] : (byte)(!(c0 > c1) && ((idx >> (2 * i)) & 3) == 3 ? 0 : 255);
                        int q = (py * w + px) * 4;
                        o[q] = col.b; o[q + 1] = col.g; o[q + 2] = col.r; o[q + 3] = a;
                    }
            }
        return o;
    }

    private static (byte r, byte g, byte b) Rgb565(ushort v)
    {
        int r = (v >> 11) & 31, g = (v >> 5) & 63, b = v & 31;
        return ((byte)(r * 255 / 31), (byte)(g * 255 / 63), (byte)(b * 255 / 31));
    }

    /// <summary>A sprite stored rotated in its atlas: turn it 90° counter-clockwise, as the game's UI does.</summary>
    private static Texture RotateLeft(Texture t)
    {
        int w = t.Height, h = t.Width;
        byte[] o = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // output (x, y) ← input (col = t.Width - 1 - y, row = x)
                int src = (x * t.Width + (t.Width - 1 - y)) * 4, dst = (y * w + x) * 4;
                o[dst] = t.Bgra[src]; o[dst + 1] = t.Bgra[src + 1]; o[dst + 2] = t.Bgra[src + 2]; o[dst + 3] = t.Bgra[src + 3];
            }
        return new Texture(t.Name, w, h, o);
    }
}
