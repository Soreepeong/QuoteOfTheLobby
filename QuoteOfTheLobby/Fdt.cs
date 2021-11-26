using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace QuoteOfTheLobby {
    public class Fdt {
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct FdtHeader {
            public fixed byte Signature[8];
            public int FontTableHeaderOffset;
            public int KerningTableHeaderOffset;
            public fixed byte Padding[0x10];
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct FontTableHeader {
            public fixed byte Signature[4];
            public int FontTableEntryCount;
            public int KerningTableEntryCount;
            public fixed byte Padding[0x04];
            public ushort TextureWidth;
            public ushort TextureHeight;
            public float Points;
            public int LineHeight;
            public int Ascent;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct FontTableEntry : IComparable<FontTableEntry> {
            public int CharUtf8;
            public ushort CharSjis;
            public ushort TextureIndex;
            public ushort TextureOffsetX;
            public ushort TextureOffsetY;
            public byte BoundingWidth;
            public byte BoundingHeight;
            public sbyte NextOffsetX;
            public sbyte CurrentOffsetY;

            public int CompareTo(FontTableEntry other) {
                return CharUtf8 - other.CharUtf8;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct KerningTableHeader {
            public fixed byte Signature[4];
            public int Count;
            public fixed byte Padding[0x08];
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct KerningTableEntry : IComparable<KerningTableEntry> {
            public int LeftUtf8;
            public int RightUtf8;
            public ushort LeftSjis;
            public ushort RightSjis;
            public int RightOffset;

            public int CompareTo(KerningTableEntry other) {
                if (LeftUtf8 == other.LeftUtf8)
                    return RightUtf8 - other.RightUtf8;
                else
                    return LeftUtf8 - other.LeftUtf8;
            }
        }

        public FdtHeader Fcsv { get; init; } = new();
        public FontTableHeader Fthd { get; init; } = new();
        public KerningTableHeader Knhd { get; init; } = new();
        public List<FontTableEntry> Glyphs { get; init; } = new();
        public List<KerningTableEntry> Distances { get; init; } = new();

        unsafe public Fdt(byte[] data) {
            fixed (byte* ptr = data) {
                Fcsv = Marshal.PtrToStructure<FdtHeader>((IntPtr)ptr);
                Fthd = Marshal.PtrToStructure<FontTableHeader>(IntPtr.Add((IntPtr)ptr, Fcsv.FontTableHeaderOffset));
                Knhd = Marshal.PtrToStructure<KerningTableHeader>(IntPtr.Add((IntPtr)ptr, Fcsv.KerningTableHeaderOffset));
                for (int p = Fcsv.FontTableHeaderOffset, p_ = Fcsv.FontTableHeaderOffset + Fthd.FontTableEntryCount * Marshal.SizeOf<FontTableEntry>(); p < p_; p += Marshal.SizeOf<FontTableEntry>())
                    Glyphs.Add(Marshal.PtrToStructure<FontTableEntry>(IntPtr.Add((IntPtr)ptr, p)));
                for (int p = Fcsv.KerningTableHeaderOffset, p_ = Fcsv.KerningTableHeaderOffset + Knhd.Count * Marshal.SizeOf<KerningTableEntry>(); p < p_; p += Marshal.SizeOf<KerningTableEntry>())
                    Distances.Add(Marshal.PtrToStructure<KerningTableEntry>(IntPtr.Add((IntPtr)ptr, p)));
            }
        }

        static int CodePointToUtf8int32(int codepoint) {
            if (codepoint <= 0x7F) {
                return codepoint;
            } else if (codepoint <= 0x7FF) {
                return ((0xC0 | ((codepoint >> 6))) << 8)
                    | ((0x80 | ((codepoint >> 0) & 0x3F)) << 0);
            } else if (codepoint <= 0xFFFF) {
                return ((0xE0 | ((codepoint >> 12))) << 16)
                    | ((0x80 | ((codepoint >> 6) & 0x3F)) << 8)
                    | ((0x80 | ((codepoint >> 0) & 0x3F)) << 0);
            } else if (codepoint <= 0x10FFFF) {
                return ((0xF0 | ((codepoint >> 18))) << 24)
                    | ((0x80 | ((codepoint >> 12) & 0x3F)) << 16)
                    | ((0x80 | ((codepoint >> 6) & 0x3F)) << 8)
                    | ((0x80 | ((codepoint >> 0) & 0x3F)) << 0);
            } else {
                return 0xFFFE;
            }
        }

        public FontTableEntry? FindGlyph(int codepoint) {
            var i = Glyphs.BinarySearch(new FontTableEntry { CharUtf8 = CodePointToUtf8int32(codepoint) });
            if (i < 0 || i == Glyphs.Count)
                return null;
            return Glyphs[i];
        }

        public FontTableEntry Glyph(int codepoint) {
            FontTableEntry? glyph;
            if ((glyph = FindGlyph(codepoint)) == null)
                if ((glyph = FindGlyph('＝')) == null)
                    if ((glyph = FindGlyph('=')) == null)
                        glyph = FindGlyph('!');
            return (FontTableEntry)glyph!;
        }

        public int Distance(int codepoint1, int codepoint2) {
            var i = Distances.BinarySearch(new KerningTableEntry { LeftUtf8 = CodePointToUtf8int32(codepoint1), RightUtf8 = CodePointToUtf8int32(codepoint2) });
            if (i < 0 || i == Distances.Count)
                return 0;
            return Distances[i].RightOffset;
        }

        public class LayoutPlan {
            public class Element {
                public int Codepoint { get; init; }
                public bool Italic { get; init; }

                public int X { get; internal set; }
                public int Y { get; internal set; }

                public FontTableEntry Glyph { get; internal set; }

                public bool IsControl {
                    get {
                        return Codepoint < 0x10000 && char.IsControl((char)Codepoint);
                    }
                }

                public bool IsSpace {
                    get {
                        return Codepoint < 0x10000 && char.IsWhiteSpace((char)Codepoint);
                    }
                }

                public bool IsLineBreak {
                    get {
                        return Codepoint == '\n' || Codepoint == '\r';
                    }
                }

                public bool IsWordBreakPoint {
                    get {
                        if (Codepoint >= 0x10000)
                            return false;

                        // TODO: Whatever
                        switch (char.GetUnicodeCategory((char)Codepoint)) {
                            case System.Globalization.UnicodeCategory.SpaceSeparator:
                            case System.Globalization.UnicodeCategory.LineSeparator:
                            case System.Globalization.UnicodeCategory.ParagraphSeparator:
                            case System.Globalization.UnicodeCategory.Control:
                            case System.Globalization.UnicodeCategory.Format:
                            case System.Globalization.UnicodeCategory.Surrogate:
                            case System.Globalization.UnicodeCategory.PrivateUse:
                            case System.Globalization.UnicodeCategory.ConnectorPunctuation:
                            case System.Globalization.UnicodeCategory.DashPunctuation:
                            case System.Globalization.UnicodeCategory.OpenPunctuation:
                            case System.Globalization.UnicodeCategory.ClosePunctuation:
                            case System.Globalization.UnicodeCategory.InitialQuotePunctuation:
                            case System.Globalization.UnicodeCategory.FinalQuotePunctuation:
                            case System.Globalization.UnicodeCategory.OtherPunctuation:
                            case System.Globalization.UnicodeCategory.MathSymbol:
                            case System.Globalization.UnicodeCategory.ModifierSymbol:
                            case System.Globalization.UnicodeCategory.OtherSymbol:
                            case System.Globalization.UnicodeCategory.OtherNotAssigned:
                                return true;
                        }
                        return false;
                    }
                }
            }

            public int Width;
            public int Height;
            public List<Element> Elements = new();
        }

        public class LayoutBuilder {
            public enum HorizontalAlignment {
                Left,
                Center,
                Right,
            }

            private readonly Fdt _fdt;
            private readonly SeString _text;
            private int _maxWidth = int.MaxValue;
            private int _translateX, _translateY;
            private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Left;

            internal LayoutBuilder(Fdt fdt, SeString text) {
                _fdt = fdt;
                _text = text;
            }

            public LayoutBuilder WithMaxWidth(int maxWidth) {
                _maxWidth = maxWidth;
                return this;
            }

            public LayoutBuilder WithTranslate(int dx, int dy) {
                _translateX = dx;
                _translateY = dy;
                return this;
            }

            public LayoutBuilder WithHorizontalAlignment(HorizontalAlignment horizontalAlignment) {
                _horizontalAlignment = horizontalAlignment;
                return this;
            }

            private void Build_LoadCodepointAndItalics(LayoutPlan plan) {
                bool italic = false;
                foreach (var payload in _text.Payloads) {
                    if (payload.Type == PayloadType.NewLine) {
                        plan.Elements.Add(new LayoutPlan.Element { Codepoint = '\n', Italic = italic });
                    } else if (payload.Type == PayloadType.SeHyphen) {
                        plan.Elements.Add(new LayoutPlan.Element { Codepoint = '-', Italic = italic });
                    } else if (payload.Type == PayloadType.RawText) {
                        var bs = payload.Encode()!;
                        for (var i = 0; i < bs.Length;) {
                            if ((bs[i] & 0x80) == 0) {
                                plan.Elements.Add(new LayoutPlan.Element { Codepoint = bs[i], Italic = italic });
                                i += 1;
                            } else if ((bs[i] & 0xE0) == 0xC0) {
                                plan.Elements.Add(new LayoutPlan.Element { Codepoint = (bs[i] & 0x1F) << 6 | (bs[i + 1] & 0x3F), Italic = italic });
                                i += 2;
                            } else if ((bs[i] & 0xF0) == 0xE0) {
                                plan.Elements.Add(new LayoutPlan.Element { Codepoint = (bs[i] & 0x0F) << 12 | (bs[i + 1] & 0x3F) << 6 | (bs[i + 2] & 0x3F), Italic = italic });
                                i += 3;
                            } else if ((bs[i] & 0xF8) == 0xF0) {
                                plan.Elements.Add(new LayoutPlan.Element { Codepoint = (bs[i] & 0x07) << 18 | (bs[i + 1] & 0x3F) << 12 | (bs[i + 2] & 0x3F) << 6 | (bs[i + 3] & 0x3F), Italic = italic });
                                i += 4;
                            } else {
                                plan.Elements.Add(new LayoutPlan.Element { Codepoint = 0xFFFE });
                                i += 1;
                            }
                            if (_fdt.FindGlyph(plan.Elements[^1].Codepoint) == null) {
                                PluginLog.Warning($"Missing glyph for character U+{plan.Elements[^1].Codepoint:x04}");
                            }
                        }
                    } else if (payload.Type == PayloadType.EmphasisItalic) {
                        italic = ((EmphasisItalicPayload)payload).IsEnabled;
                    }
                }

                for (var i = 0; i < plan.Elements.Count; i++)
                    plan.Elements[i].Glyph = _fdt.Glyph(plan.Elements[i].Codepoint);
            }


            public LayoutPlan Build() {
                var plan = new LayoutPlan();
                Build_LoadCodepointAndItalics(plan);

                int lastBreakIndex = 0;
                List<int> lineBreakIndices = new() { 0 };
                for (var i = 1; i < plan.Elements.Count; i++) {
                    var prev = plan.Elements[i - 1];
                    var curr = plan.Elements[i];

                    if (prev.IsLineBreak) {
                        curr.X = 0;
                        curr.Y = prev.Y + _fdt.Fthd.LineHeight;
                        lineBreakIndices.Add(i);
                    } else {
                        curr.X = prev.X + prev.Glyph.NextOffsetX + prev.Glyph.BoundingWidth + _fdt.Distance(prev.Codepoint, curr.Codepoint);
                        curr.Y = prev.Y;
                    }

                    if (prev.IsWordBreakPoint)
                        lastBreakIndex = i;

                    if (curr.IsSpace)
                        continue;

                    if (curr.X + curr.Glyph.BoundingWidth < _maxWidth)
                        continue;

                    if (!prev.IsSpace && plan.Elements[lastBreakIndex].X > 0) {
                        prev = plan.Elements[lastBreakIndex - 1];
                        curr = plan.Elements[lastBreakIndex];
                        i = lastBreakIndex;
                    } else {
                        lastBreakIndex = i;
                    }
                    curr.X = 0;
                    curr.Y = prev.Y + _fdt.Fthd.LineHeight;
                    lineBreakIndices.Add(i);
                }

                for (var i = 0; i < plan.Elements.Count; i++) {
                    plan.Elements[i].X += _translateX;
                    plan.Elements[i].Y += _translateY;
                }

                lineBreakIndices.Add(plan.Elements.Count);
                for (var i = 1; i < lineBreakIndices.Count; i++) {
                    var from = lineBreakIndices[i - 1];
                    var to = lineBreakIndices[i];
                    while (to > from && plan.Elements[to - 1].IsSpace) {
                        to--;
                    }
                    if (from >= to)
                        continue;
                    var width = plan.Elements[to - 1].X + plan.Elements[to - 1].Glyph.BoundingWidth - plan.Elements[from].X;
                    plan.Width = Math.Max(plan.Width, width);
                    int offsetX;
                    if (_horizontalAlignment == HorizontalAlignment.Center)
                        offsetX = (_maxWidth - width) / 2;
                    else if (_horizontalAlignment == HorizontalAlignment.Right)
                        offsetX = _maxWidth - width;
                    else if (_horizontalAlignment == HorizontalAlignment.Left)
                        offsetX = 0;
                    else
                        throw new ArgumentException("Invalid horizontal alignment");
                    for (var j = from; j < to; j++)
                        plan.Elements[j].X += offsetX;
                }
                plan.Height = Math.Max(plan.Height, _fdt.Fthd.LineHeight * (lineBreakIndices.Count - 1));

                return plan;
            }
        }

        public LayoutBuilder BuildLayout(SeString text) {
            return new LayoutBuilder(this, text);
        }
    };

}
