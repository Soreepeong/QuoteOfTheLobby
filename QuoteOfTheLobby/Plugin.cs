using Dalamud;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Utility;
using ImGuiNET;
using Lumina.Data.Files;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace QuoteOfTheLobbyPlugin {
    public sealed class Plugin : IDalamudPlugin {
        private static readonly string HideNamedUiElementSignature = "40 57 48 83 EC 20 48 8B F9 48 8B 89 C8 00 00 00 48 85 C9 0F ?? ?? ?? ?? ?? 8B 87 B0 01 00 00 C1 E8 07 A8 01";
        private static readonly string ShowNamedUiElementSignature = "40 53 48 83 EC 40 48 8B 91 C8 00 00 00 48 8B D9 48 85 D2";
        private static readonly string[] ValidDialogueSuffixes = { ".", "!", "?", "！", "！", "。", "…" };
        private static readonly string[] FontNames = {
            "AXIS_96", "AXIS_12", "AXIS_18", "AXIS_36",
            "Jupiter_16", "Jupiter_20", "Jupiter_23", "Jupiter_46",
            "MiedingerMid_10", "MiedingerMid_12", "MiedingerMid_14", "MiedingerMid_18", "MiedingerMid_36",
            "TrumpGothic_184", "TrumpGothic_23", "TrumpGothic_34", "TrumpGothic_68",
            };

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
                public uint CharUtf8;
                public ushort CharSjis;
                public ushort TextureIndex;
                public ushort TextureOffsetX;
                public ushort TextureOffsetY;
                public byte BoundingWidth;
                public byte BoundingHeight;
                public sbyte NextOffsetX;
                public sbyte CurrentOffsetY;

                public int CompareTo(FontTableEntry other) {
                    return (int)(CharUtf8 - other.CharUtf8);
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
                public uint LeftUtf8;
                public uint RightUtf8;
                public ushort LeftSjis;
                public ushort RightSjis;
                public int RightOffset;

                public int CompareTo(KerningTableEntry other) {
                    if (LeftUtf8 == other.LeftUtf8)
                        return (int)(RightUtf8 - other.RightUtf8);
                    else
                        return (int)(LeftUtf8 - other.LeftUtf8);
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

            static uint CodePointToUtf8Uint32(uint codepoint) {
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

            public FontTableEntry? FindGlyph(uint codepoint) {
                var i = Glyphs.BinarySearch(new FontTableEntry { CharUtf8 = CodePointToUtf8Uint32(codepoint) });
                if (i < 0 || i == Glyphs.Count)
                    return null;
                return Glyphs[i];
            }

            public FontTableEntry Glyph(uint codepoint) {
                FontTableEntry? glyph;
                if ((glyph = FindGlyph(codepoint)) == null)
                    if ((glyph = FindGlyph('＝')) == null)
                        if ((glyph = FindGlyph('=')) == null)
                            glyph = FindGlyph('!');
                return (FontTableEntry)glyph!;
            }

            public int Distance(uint codepoint1, uint codepoint2) {
                var i = Distances.BinarySearch(new KerningTableEntry { LeftUtf8 = CodePointToUtf8Uint32(codepoint1), RightUtf8 = CodePointToUtf8Uint32(codepoint2) });
                if (i < 0 || i == Distances.Count)
                    return 0;
                return Distances[i].RightOffset;
            }
        };

        public string Name => "Quote of the Lobby";

        private readonly DalamudPluginInterface _pluginInterface;
        private readonly CommandManager _commandManager;
        private readonly DataManager _dataManager;
        private readonly ClientState _clientState;

        private delegate IntPtr HideShowNamedUiElementDelegate(IntPtr pThis);
        private readonly Hook<HideShowNamedUiElementDelegate> _hideHook, _showHook;

        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.InstanceContentTextData> _instanceContentTextData;
        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.PublicContentTextData> _publicContentTextData;
        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.PartyContentTextData> _partyContentTextData;
        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.NpcYell> _npcYell;
        private readonly List<Fdt> _fdts = new();
        private readonly List<ImGuiScene.TextureWrap> _fontTextures = new();

        private readonly List<IDisposable> _disposableList = new();

        private SeString? _qotlText = null;
        private int _currentFontIndex = 0;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            [RequiredVersion("1.0")] DataManager dataManager,
            [RequiredVersion("1.0")] ClientState clientState) {
            try {
                _disposableList.Add(_pluginInterface = pluginInterface);
                _commandManager = commandManager;
                _disposableList.Add(_dataManager = dataManager);
                _disposableList.Add(_clientState = clientState);
                _instanceContentTextData = _dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.InstanceContentTextData>(clientState.ClientLanguage)!;
                _publicContentTextData = _dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.PublicContentTextData>(clientState.ClientLanguage)!;
                _partyContentTextData = _dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.PartyContentTextData>(clientState.ClientLanguage)!;
                _npcYell = _dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.NpcYell>(clientState.ClientLanguage)!;

                foreach (var fontName in FontNames)
                    _fdts.Add(new Fdt(_dataManager.GetFile($"common/font/{fontName}.fdt")!.Data));
                byte[] buf = new byte[1024 * 1024 * 4];
                foreach (var i in Enumerable.Range(1, 100)) {
                    var tf = _dataManager.GameData.GetFile<TexFile>($"common/font/font{i}.tex");
                    if (tf == null)
                        break;
                    PluginLog.Information($"Read common/font/font{i}.tex ({tf.Header.Width} x {tf.Header.Height})");
                    if (tf.ImageData.Length != tf.Header.Width * tf.Header.Height * 4)
                        throw new Exception("TexE");

                    if (buf.Length < tf.Header.Width * tf.Header.Height * 4)
                        buf = new byte[tf.Header.Width * tf.Header.Height * 4];
                    foreach (var j in textureChannelOrder) {
                        for (int k = 0, k_ = tf.Header.Width * tf.Header.Height * 4, s = j; k < k_; s += 4) {
                            buf[k++] = tf.ImageData[s];
                            buf[k++] = tf.ImageData[s];
                            buf[k++] = tf.ImageData[s];
                            buf[k++] = tf.ImageData[s];
                        }
                        _fontTextures.Add(_pluginInterface.UiBuilder.LoadImageRaw(buf, tf.Header.Width, tf.Header.Height, 4));
                    }
                }
                _disposableList.AddRange(_fontTextures);


                _pluginInterface.UiBuilder.Draw += DrawUI;

                var scanner = new SigScanner();
                var hideNamedUiElementAddress = scanner.ScanText(HideNamedUiElementSignature);
                var showNamedUiElementAddress = scanner.ScanText(ShowNamedUiElementSignature);

                //_disposableList.Add(_hideHook = new Hook<HideShowNamedUiElementDelegate>(hideNamedUiElementAddress, this.HideNamedUiElementDetour));
                //_disposableList.Add(_showHook = new Hook<HideShowNamedUiElementDelegate>(showNamedUiElementAddress, this.ShowNamedUiElementDetour));
                //_hideHook.Enable();
                //_showHook.Enable();

            } catch (Exception e) {
                Dispose();
                throw;
            }
        }

        public void Dispose() {
            foreach (var item in _disposableList.AsEnumerable().Reverse()) {
                try {
                    item.Dispose();
                } catch (Exception e) {
                    PluginLog.Warning(e, "Dispose failure");
                }
            }
            _disposableList.Clear();
        }

        private void PickRandomQotd() {
            SeString? txt = null;
            var i = 0;
            while (i++ < 64) {
                var n = (uint)new Random().Next((int)(_instanceContentTextData.RowCount + _publicContentTextData.RowCount + _partyContentTextData.RowCount + _npcYell.RowCount));
                try {
                    if (n < _instanceContentTextData.RowCount) {
                        txt = _instanceContentTextData.GetRow(n)!.Text.ToDalamudString();
                    } else {
                        n -= _instanceContentTextData.RowCount;
                        if (n < _publicContentTextData.RowCount) {
                            txt = _publicContentTextData.GetRow(n)!.TextData.ToDalamudString();
                        } else {
                            n -= _publicContentTextData.RowCount;
                            if (n < _partyContentTextData.RowCount) {
                                txt = _partyContentTextData.GetRow(n)!.Data.ToDalamudString();
                            } else {
                                n -= _partyContentTextData.RowCount;
                                txt = _npcYell.GetRow(n)!.Text.ToDalamudString();
                            }
                        }
                    }
                } catch (NullReferenceException) {
                    continue;
                }
                if (!ValidDialogueSuffixes.Any(x => txt.TextValue.EndsWith(x)))
                    continue;

                _qotlText = txt;
                PluginLog.Information($"Test: {txt}");
                break;
            }
        }

        private unsafe IntPtr ShowNamedUiElementDetour(IntPtr pThis) {
            var res = _showHook.Original(pThis);
            var windowName = Marshal.PtrToStringUTF8(pThis + 8)!;
            PluginLog.Debug($"Show: {windowName}");
            if (windowName == "Title") {
                // PickRandomQotd();
            }
            return res;
        }

        private unsafe IntPtr HideNamedUiElementDetour(IntPtr pThis) {
            var res = _hideHook.Original(pThis);
            var windowName = Marshal.PtrToStringUTF8(pThis + 8)!;
            PluginLog.Debug($"Hide: {windowName}");
            if (windowName == "Title") {
                _qotlText = null;
            }
            return res;
        }

        private static readonly int[] textureChannelOrder = { 2, 1, 0, 3 };

        public struct GlyphLayoutPlan {
            public List<Tuple<Fdt.FontTableEntry, int, int>> Targets = new();
            public int Width = 0;
            public int Height = 0;

            public GlyphLayoutPlan(Fdt fdt, SeString s, int x, int y, int maxWidth = int.MaxValue) {
                int cx = x;
                int cy = y;
                Width = x;
                Height = y;
                List<uint> codepoints = new();
                foreach (var payload in s.Payloads) {
                    codepoints.Clear();
                    if (payload.Type == PayloadType.NewLine) {
                        codepoints.Add('\n');
                    } else if (payload.Type == PayloadType.SeHyphen) {
                        codepoints.Add('-');
                    } else if (payload.Type == PayloadType.RawText) {
                        var bs = payload.Encode()!;
                        for (var i = 0; i < bs.Length;) {
                            if ((bs[i] & 0x80) == 0) {
                                codepoints.Add(bs[i]);
                                i += 1;
                            } else if ((bs[i] & 0xE0) == 0xC0) {
                                codepoints.Add((uint)((bs[i] & 0x1F) << 6 | (bs[i + 1] & 0x3F)));
                                i += 2;
                            } else if ((bs[i] & 0xF0) == 0xE0) {
                                codepoints.Add((uint)((bs[i] & 0x0F) << 12 | (bs[i + 1] & 0x3F) << 6 | (bs[i + 2] & 0x3F)));
                                i += 3;
                            } else if ((bs[i] & 0xF8) == 0xF0) {
                                codepoints.Add((uint)((bs[i] & 0x07) << 18 | (bs[i + 1] & 0x3F) << 12 | (bs[i + 2] & 0x3F) << 6 | (bs[i + 3] & 0x3F)));
                                i += 4;
                            } else {
                                codepoints.Add(0xFFFE);
                                i += 1;
                            }
                        }
                    } else if (payload.Type == PayloadType.EmphasisItalic) {
                        var italic = ((EmphasisItalicPayload)payload).IsEnabled;
                        codepoints.Add('<');
                        if (!italic)
                            codepoints.Add('/');
                        codepoints.Add('i');
                        codepoints.Add('>');
                    }

                    uint lastCodepoint = 0;
                    foreach (var codepoint in codepoints) {
                        if (codepoint == '\n') {
                            cx = x;
                            cy += fdt.Fthd.LineHeight;
                            lastCodepoint = 0;
                            continue;
                        }
                        cx += fdt.Distance(lastCodepoint, codepoint);
                        var glyph = fdt.Glyph(codepoint);
                        if (cx + glyph.NextOffsetX + glyph.BoundingWidth > maxWidth) {
                            cx = x;
                            cy += fdt.Fthd.LineHeight;
                        }
                        Targets.Add(new Tuple<Fdt.FontTableEntry, int, int>(glyph, cx, cy + glyph.CurrentOffsetY));
                        Height = Math.Max(Height, cy + glyph.CurrentOffsetY + glyph.BoundingHeight);
                        Width = Math.Max(Width, cx + glyph.BoundingWidth);
                        cx += glyph.NextOffsetX + glyph.BoundingWidth;
                        lastCodepoint = codepoint;
                    }
                }
                Width -= x;
                Height -= y;
            }
        }

        private void DrawUI() {
            // TODO: draw _qotlText

            ImGui.SetNextWindowSize(new Vector2(375, 330), ImGuiCond.FirstUseEver);
            bool v = true;
            if (ImGui.Begin("Quote of the Lobby", ref v, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoTitleBar))
                try {
                    if (ImGui.Button("Shuffle")) {
                        PickRandomQotd();
                    }
                    ImGui.SameLine();
                    ImGui.Combo("Font", ref _currentFontIndex, FontNames, FontNames.Length);
                    if (_qotlText != null) {
                        var colorFill = new Vector4(255, 255, 255, 255);
                        var colorBorder = new Vector4(0, 0, 0, 255);
                        var borderWeight = 1;
                        var maxWidth = (int)ImGui.GetWindowWidth() - 2 * borderWeight - 2;

                        var plan = new GlyphLayoutPlan(_fdts[_currentFontIndex], _qotlText, 2 + borderWeight, 2 + borderWeight + (int)ImGui.GetCursorPosY() + 2, maxWidth);
                        for (var x = 0; x <= 2 * borderWeight; x++) {
                            for (var y = 0; y <= 2 * borderWeight; y++) {
                                if (x == borderWeight && y == borderWeight)
                                    continue;
                                foreach (var p in plan.Targets) {
                                    var glyph = p.Item1;
                                    ImGui.SetCursorPosX(x + p.Item2);
                                    ImGui.SetCursorPosY(y + p.Item3);
                                    var tex = _fontTextures[glyph.TextureIndex];
                                    ImGui.Image(tex.ImGuiHandle,
                                        new Vector2(glyph.BoundingWidth, glyph.BoundingHeight),
                                        new Vector2(1.0f * glyph.TextureOffsetX / tex.Width, 1.0f * glyph.TextureOffsetY / tex.Height),
                                        new Vector2(1.0f * (glyph.TextureOffsetX + glyph.BoundingWidth) / tex.Width, 1.0f * (glyph.TextureOffsetY + glyph.BoundingHeight) / tex.Height),
                                        colorBorder);
                                }
                            }
                        }
                        foreach (var p in plan.Targets) {
                            var glyph = p.Item1;
                            ImGui.SetCursorPosX(borderWeight + p.Item2);
                            ImGui.SetCursorPosY(borderWeight + p.Item3);
                            var tex = _fontTextures[glyph.TextureIndex];
                            ImGui.Image(tex.ImGuiHandle,
                                new Vector2(glyph.BoundingWidth, glyph.BoundingHeight),
                                new Vector2(1.0f * glyph.TextureOffsetX / tex.Width, 1.0f * glyph.TextureOffsetY / tex.Height),
                                new Vector2(1.0f * (glyph.TextureOffsetX + glyph.BoundingWidth) / tex.Width, 1.0f * (glyph.TextureOffsetY + glyph.BoundingHeight) / tex.Height),
                                colorFill);
                        }
                    }
                } catch (Exception ex) {
                    PluginLog.Error(ex, "?");
                } finally {
                    ImGui.End();
                }
        }
    }
}
