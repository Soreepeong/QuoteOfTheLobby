using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Components;
using Dalamud.Logging;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuoteOfTheLobby {
    public class TextLayer : IDisposable {
        private readonly Plugin _plugin;
        public readonly Configuration.TextLayerConfiguration Config;
        public bool Deleted { get; internal set; }

        private SeString? _text;
        private ImGuiScene.TextureWrap? _borderTexture;
        private ImGuiScene.TextureWrap? _fillTexture;
        private Vector2 _lastWindowSize = new();

        public TextLayer(Plugin plugin, Configuration.TextLayerConfiguration? config = null) {
            if (config == null)
                config = new Configuration.TextLayerConfiguration();
            _plugin = plugin;
            Config = config!;
        }

        public void Dispose() {
            _borderTexture?.Dispose();
            _fillTexture?.Dispose();
            _borderTexture = null;
            _fillTexture = null;
        }

        public void RefreshText() {
            _text = null;
        }

        private void ResolveText() {
            if (Config.Type == Configuration.TextLayerType.RandomDialogue)
                _text = _plugin.GetRandomQuoteReader(Config.Language).GetRandomQuote();
            else if (Config.Type == Configuration.TextLayerType.DefaultDatacenter) {
                try {
                    var cfg = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\My Games\\FINAL FANTASY XIV - A Realm Reborn\\FFXIV.cfg");
                    uint worldId = uint.Parse(Regex.Match(cfg, @"WorldId\s+([0-9]+)").Groups[1].Value);
                    _text = _plugin.GetDatacenterNameFromWorldId(worldId)!;
                } catch (Exception e) {
                    PluginLog.Warning(e, "Failed to read last world ID");
                    _text = SeString.Empty;
                }
            }
        }

        public void DrawText(Native.RECT rc) {
            if (_text == null) {
                ResolveText();
                if (_text == null)
                    return;
                _borderTexture?.Dispose();
                _fillTexture?.Dispose();
                _borderTexture = null;
                _fillTexture = null;
            }

            if (_borderTexture == null || _fillTexture == null || _lastWindowSize != ImGui.GetWindowSize()) {
                _lastWindowSize = ImGui.GetWindowSize();
                _borderTexture?.Dispose();
                _fillTexture?.Dispose();

                var pad = (int)Math.Ceiling(Config.BorderWidth);

                var fdt = _plugin.GetFdt(Config.FontIndex);
                var plan = fdt
                    .BuildLayout(_text)
                    .WithMaxWidth((int)(_lastWindowSize.X * (1 - Config.HorizontalMargin)) - 2 * pad)
                    .WithHorizontalAlignment((Fdt.LayoutBuilder.HorizontalAlignment)Config.HorizontalAlignment)
                    .Build();

                var width = plan.Width + 2 * pad;
                var height = plan.Height + 2 * pad;

                var distanceMap = new float[2 * pad + 1, 2 * pad + 1];
                for (var x = 0; x <= 2 * pad; x++) {
                    for (var y = 0; y <= 2 * pad; y++) {
                        distanceMap[x, y] = (float)Math.Pow(1 - Math.Min(1, Math.Sqrt(Math.Pow(x - pad, 2) + Math.Pow(y - pad, 2)) / Config.BorderWidth), Math.Pow(2, -Config.BorderStrength));
                    }
                }

                var borderBuffer = new byte[width * height * 4];
                for (var x = 0; x <= 2 * pad; x++) {
                    for (var y = 0; y <= 2 * pad; y++) {
                        foreach (var p in plan.Elements) {
                            if (p.IsControl || p.IsSpace)
                                continue;
                            var sourceBuffer = _plugin.GetFontTextureData(p.Glyph.TextureIndex / 4);
                            var sourceBufferDelta = Constants.TextureChannelOrder[p.Glyph.TextureIndex % 4];
                            for (var i = 0; i < p.Glyph.BoundingWidth; i++) {
                                for (var j = 0; j < p.Glyph.BoundingHeight; j++) {
                                    var pos = 4 * ((i + x + p.X - plan.Left) + width * (j + y + p.Y));
                                    borderBuffer[pos + 0] = (byte)(255 * Config.ColorBorder.X);
                                    borderBuffer[pos + 1] = (byte)(255 * Config.ColorBorder.Y);
                                    borderBuffer[pos + 2] = (byte)(255 * Config.ColorBorder.Z);
                                    borderBuffer[pos + 3] = Math.Max(borderBuffer[pos + 3],
                                        (byte)(distanceMap[x, y] *
                                        sourceBuffer[sourceBufferDelta + 4 * (
                                            (p.Glyph.TextureOffsetX + i) +
                                            (p.Glyph.TextureOffsetY + j) * fdt.Fthd.TextureWidth
                                            )]));
                                }
                            }
                        }
                    }
                }

                var fillBuffer = new byte[width * height * 4];
                foreach (var p in plan.Elements) {
                    if (p.IsControl || p.IsSpace)
                        continue;
                    var sourceBuffer = _plugin.GetFontTextureData(p.Glyph.TextureIndex / 4);
                    var sourceBufferDelta = Constants.TextureChannelOrder[p.Glyph.TextureIndex % 4];
                    for (var i = 0; i < p.Glyph.BoundingWidth; i++) {
                        for (var j = 0; j < p.Glyph.BoundingHeight; j++) {
                            var pos = 4 * ((i + pad + p.X - plan.Left) + width * (j + pad + p.Y));
                            fillBuffer[pos + 0] = (byte)(255 * Config.ColorFill.X);
                            fillBuffer[pos + 1] = (byte)(255 * Config.ColorFill.Y);
                            fillBuffer[pos + 2] = (byte)(255 * Config.ColorFill.Z);
                            fillBuffer[pos + 3] = Math.Max(fillBuffer[pos + 3],
                                sourceBuffer[sourceBufferDelta + 4 * (
                                    (p.Glyph.TextureOffsetX + i) +
                                    (p.Glyph.TextureOffsetY + j) * fdt.Fthd.TextureWidth
                                    )]);
                            borderBuffer[pos + 3] = Math.Min(borderBuffer[pos + 3], (byte)(255 - fillBuffer[pos + 3]));
                        }
                    }
                }
                for (var i = 3; i < borderBuffer.Length; i += 4) {
                    borderBuffer[i] = (byte)(Math.Min(borderBuffer[i], (byte)(255 - fillBuffer[i])) * Config.ColorBorder.W);
                    fillBuffer[i] = (byte)(fillBuffer[i] * Config.ColorFill.W);
                }

                _borderTexture = _plugin.PluginInterface.UiBuilder.LoadImageRaw(borderBuffer, width, height, 4);
                _fillTexture = _plugin.PluginInterface.UiBuilder.LoadImageRaw(fillBuffer, width, height, 4);
            }

            int xd = (int)(ImGui.GetWindowWidth() * Config.HorizontalMargin / 2);
            if (Config.HorizontalAlignment == Fdt.LayoutBuilder.HorizontalAlignment.Center)
                xd = (int)(ImGui.GetWindowWidth() - _fillTexture.Width) / 2;
            else if (Config.HorizontalAlignment == Fdt.LayoutBuilder.HorizontalAlignment.Right)
                xd = (int)(ImGui.GetWindowWidth() - _fillTexture.Width) - xd;

            var yd = (int)(rc.Height * Config.VerticalPosition);
            if (Config.VerticalSnap == Configuration.VerticalSnapType.Middle) {
                yd -= _fillTexture.Height / 2;
            } else if (Config.VerticalSnap == Configuration.VerticalSnapType.Bottom) {
                yd -= _fillTexture.Height;
            }
            yd = Math.Max(0, Math.Min(rc.Height - _fillTexture.Height, yd));

            if (Config.ColorBackground.W > 0) {
                ImGui.GetWindowDrawList().AddRectFilled(
                    new Vector2(
                        rc.Left + xd - Config.BackgroundPadding,
                        rc.Top + yd - Config.BackgroundPadding),
                    new Vector2(rc.Left + xd + _fillTexture.Width + Config.BackgroundPadding,
                        rc.Top + yd + _fillTexture.Height + Config.BackgroundPadding),
                    ImGui.ColorConvertFloat4ToU32(Config.ColorBackground));
            }

            ImGui.GetWindowDrawList().AddImage(_borderTexture.ImGuiHandle,
                new Vector2(rc.Left + xd, rc.Top + yd),
                new Vector2(rc.Left + xd + _borderTexture.Width, rc.Top + yd + _borderTexture.Height));
            ImGui.GetWindowDrawList().AddImage(_fillTexture.ImGuiHandle,
                new Vector2(rc.Left + xd, rc.Top + yd),
                new Vector2(rc.Left + xd + _fillTexture.Width, rc.Top + yd + _fillTexture.Height));
        }

        public void DrawConfigElements() {
            try {
                if (ImGui.Button("Shuffle")) {
                    RefreshText();
                }
                ImGui.SameLine();
                if (ImGui.Button("Remove")) {
                    Deleted = true;
                }

                ImGui.Text($"GUID: {Config.Guid}");
                ImGui.InputText("Name", ref Config.Name, 64);
                ImGui.InputText("Visible With", ref Config.VisibleWith, 64);
                ImGui.Combo("Type", ref Config.TypeVal, Constants.TextLayerTypeNames, Constants.TextLayerTypeNames.Length);
                if (Config.Type == Configuration.TextLayerType.RandomDialogue) {
                    ImGui.Combo("Language", ref Config.LanguageVal, Constants.LanguageNames, Constants.LanguageNames.Length);
                }

                bool relayoutRequired = false;
                relayoutRequired |= ImGui.Combo("Horizontal Alignment", ref Config.HorizontalAlignmentInt, Constants.HorizontalAlignmentNames, Constants.HorizontalAlignmentNames.Length);
                relayoutRequired |= ImGui.Combo("Vertical Snap", ref Config.VerticalSnapInt, Constants.VerticalSnapNames, Constants.VerticalSnapNames.Length);
                relayoutRequired |= ImGui.SliderFloat("Horizontal Margin", ref Config.HorizontalMargin, 0.0f, 1f);
                relayoutRequired |= ImGui.SliderFloat("Vertical Position", ref Config.VerticalPosition, 0.0f, 1f);
                relayoutRequired |= ImGui.SliderInt("Background Padding", ref Config.BackgroundPadding, 0, 16);
                relayoutRequired |= ImGui.Combo("Font", ref Config.FontIndex, Constants.FontNames, Constants.FontNames.Length);
                relayoutRequired |= ImGui.SliderFloat("Border Width", ref Config.BorderWidth, 0, 8f);
                relayoutRequired |= ImGui.SliderFloat("Border Strength", ref Config.BorderStrength, -4f, 4f);

                relayoutRequired |= ColorPickerWithPalette(1, "Fill Color", ref Config.ColorFill, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Fill Color");
                ImGui.SameLine();
                relayoutRequired |= ColorPickerWithPalette(2, "Border Color", ref Config.ColorBorder, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Border Color");
                ImGui.SameLine();
                relayoutRequired |= ColorPickerWithPalette(3, "Background Color", ref Config.ColorBackground, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Background Color");

                if (relayoutRequired) {
                    _borderTexture?.Dispose();
                    _fillTexture?.Dispose();
                    _borderTexture = null;
                    _fillTexture = null;
                }
            } catch (Exception ex) {
                PluginLog.Error(ex, "?");
            }
        }

        private bool ColorPickerWithPalette(int id, string description, ref Vector4 color, ImGuiColorEditFlags flags) {
            var color2 = ImGuiComponents.ColorPickerWithPalette(id, description, color, flags);
            if (color == color2)
                return false;
            color = color2;
            return true;
        }
    }
}

