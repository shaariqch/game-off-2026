"""Makes a copy of a TrueType font whose digits 0 to 9 draw its tabular lining figures.

Alegreya Sans draws old-style figures by default (a 0 the height of an o), and UI Toolkit can't switch on the font's
lining-figure feature, so the HUD uses copies whose character map points the digits at the .tf glyphs instead.
Standard library only: the cmap table is rebuilt (format 4 for the basic plane, format 12 if the font has one), the
digital signature is dropped (it no longer matches), and the table checksums are recalculated.

    python Tools/lining_figures.py <in.ttf> <out.ttf> [suffix]   (suffix defaults to .tf)
"""
import struct
import sys

DIGITS = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"]


def read_tables(data):
    count = struct.unpack(">H", data[4:6])[0]
    tables = {}
    for i in range(count):
        tag, _, offset, length = struct.unpack(">4sIII", data[12 + 16 * i:28 + 16 * i])
        tables[tag.decode("latin1")] = data[offset:offset + length]
    return data[:4], tables


def glyph_names(post):
    if struct.unpack(">I", post[:4])[0] != 0x20000:
        raise SystemExit("needs a post table in format 2 to find glyphs by name")
    count = struct.unpack(">H", post[32:34])[0]
    indices = struct.unpack(">%dH" % count, post[34:34 + 2 * count])
    names, p = [], 34 + 2 * count
    while p < len(post):
        length = post[p]
        names.append(post[p + 1:p + 1 + length].decode("latin1"))
        p += 1 + length
    return [names[i - 258] if i >= 258 else None for i in indices]


def read_cmap(cmap):
    """Every code point the font maps, from its format 4 and format 12 subtables."""
    mapping = {}
    count = struct.unpack(">H", cmap[2:4])[0]
    for i in range(count):
        _, _, offset = struct.unpack(">HHI", cmap[4 + 8 * i:12 + 8 * i])
        fmt = struct.unpack(">H", cmap[offset:offset + 2])[0]
        if fmt == 4:
            segs = struct.unpack(">H", cmap[offset + 6:offset + 8])[0] // 2
            base = offset + 14
            ends = struct.unpack(">%dH" % segs, cmap[base:base + 2 * segs])
            starts = struct.unpack(">%dH" % segs, cmap[base + 2 * segs + 2:base + 4 * segs + 2])
            deltas = struct.unpack(">%dh" % segs, cmap[base + 4 * segs + 2:base + 6 * segs + 2])
            ranges_at = base + 6 * segs + 2
            ranges = struct.unpack(">%dH" % segs, cmap[ranges_at:ranges_at + 2 * segs])
            for s in range(segs):
                for code in range(starts[s], ends[s] + 1):
                    if code == 0xFFFF:
                        continue
                    if ranges[s] == 0:
                        glyph = (code + deltas[s]) & 0xFFFF
                    else:
                        at = ranges_at + 2 * s + ranges[s] + 2 * (code - starts[s])
                        glyph = struct.unpack(">H", cmap[at:at + 2])[0]
                        if glyph:
                            glyph = (glyph + deltas[s]) & 0xFFFF
                    if glyph:
                        mapping.setdefault(code, glyph)
        elif fmt == 12:
            groups = struct.unpack(">I", cmap[offset + 12:offset + 16])[0]
            for g in range(groups):
                start, end, glyph = struct.unpack(">III", cmap[offset + 16 + 12 * g:offset + 28 + 12 * g])
                for code in range(start, end + 1):
                    mapping.setdefault(code, glyph + code - start)
    return mapping


def format4(mapping):
    codes = sorted(c for c in mapping if c < 0xFFFF)
    segments = []
    for code in codes:
        if segments and code == segments[-1][1] + 1:
            segments[-1][1] = code
        else:
            segments.append([code, code])
    segments.append([0xFFFF, 0xFFFF])
    segs = len(segments)
    ends, starts, deltas, ranges, glyph_array = [], [], [], [], []
    for i, (start, end) in enumerate(segments):
        starts.append(start)
        ends.append(end)
        deltas.append(1 if start == 0xFFFF else 0)
        if start == 0xFFFF:
            ranges.append(0)
            continue
        # offset from this idRangeOffset entry to its glyphs in the glyph array
        ranges.append(2 * (segs - i) + 2 * len(glyph_array))
        glyph_array.extend(mapping[c] for c in range(start, end + 1))
    search = 1
    while search * 2 <= segs:
        search *= 2
    body = struct.pack(">HHHH", 2 * segs, 2 * search, search.bit_length() - 1, 2 * segs - 2 * search)
    body += struct.pack(">%dH" % segs, *ends) + b"\0\0"
    body += struct.pack(">%dH" % segs, *starts)
    body += struct.pack(">%dh" % segs, *deltas)
    body += struct.pack(">%dH" % segs, *ranges)
    body += struct.pack(">%dH" % len(glyph_array), *glyph_array)
    if 6 + len(body) > 0xFFFF:
        raise SystemExit("format 4 subtable too big")
    return struct.pack(">HHH", 4, 6 + len(body), 0) + body


def format12(mapping):
    groups = []
    for code in sorted(mapping):
        glyph = mapping[code]
        if groups and code == groups[-1][1] + 1 and glyph == groups[-1][2] + code - groups[-1][0]:
            groups[-1][1] = code
        else:
            groups.append([code, code, glyph])
    body = b"".join(struct.pack(">III", *g) for g in groups)
    return struct.pack(">HHIII", 12, 0, 16 + len(body), 0, len(groups)) + body


def checksum(data):
    data += b"\0" * (-len(data) % 4)
    return sum(struct.unpack(">%dI" % (len(data) // 4), data)) & 0xFFFFFFFF


def main():
    source, target = sys.argv[1], sys.argv[2]
    suffix = sys.argv[3] if len(sys.argv) > 3 else ".tf"
    data = open(source, "rb").read()
    version, tables = read_tables(data)
    names = glyph_names(tables["post"])
    mapping = read_cmap(tables["cmap"])
    for digit, name in enumerate(DIGITS):
        mapping[0x30 + digit] = names.index(name + suffix)

    sub4 = format4(mapping)
    subtables = [(0, 3, sub4), (3, 1, sub4)]
    if any(c > 0xFFFF for c in mapping):
        sub12 = format12(mapping)
        subtables += [(0, 4, sub12), (3, 10, sub12)]
    header = struct.pack(">HH", 0, len(subtables))
    offset = 4 + 8 * len(subtables)
    records, bodies, placed = b"", b"", {}
    for platform, encoding, body in subtables:
        if id(body) not in placed:
            placed[id(body)] = offset + len(bodies)
            bodies += body
        records += struct.pack(">HHI", platform, encoding, placed[id(body)])
    tables["cmap"] = header + records + bodies
    tables.pop("DSIG", None)

    head = bytearray(tables["head"])
    head[8:12] = b"\0\0\0\0"
    tables["head"] = bytes(head)
    tags = sorted(tables)
    count = len(tags)
    search = 1
    while search * 2 <= count:
        search *= 2
    out = version + struct.pack(">HHHH", count, 16 * search, search.bit_length() - 1, 16 * count - 16 * search)
    offset = 12 + 16 * count
    directory, blob = b"", b""
    for tag in tags:
        table = tables[tag]
        directory += struct.pack(">4sIII", tag.encode("latin1"), checksum(table), offset + len(blob), len(table))
        blob += table + b"\0" * (-len(table) % 4)
    font = bytearray(out + directory + blob)
    adjustment = (0xB1B0AFBA - checksum(bytes(font))) & 0xFFFFFFFF
    head_at = 12 + 16 * count + sum(len(tables[t]) + (-len(tables[t]) % 4) for t in tags[:tags.index("head")])
    font[head_at + 8:head_at + 12] = struct.pack(">I", adjustment)
    open(target, "wb").write(font)
    print(target, "digits ->", suffix)


main()
