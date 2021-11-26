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
    public class TextLayer {
        private readonly Plugin _plugin;
        public readonly Configuration.TextLayerConfiguration Config;
        public bool Deleted { get; internal set; }

        private SeString? _text;

        public TextLayer(Plugin plugin, Configuration.TextLayerConfiguration? config = null) {
            if (config == null)
                config = new Configuration.TextLayerConfiguration();
            _plugin = plugin;
            Config = config!;
        }

        public void RefreshText() {
            _text = null;
        }

        private void ResolveText() {
            if (Config.Type == Configuration.TextLayerType.RandomDialogue)
                _text = _plugin.GetRandomQuote();
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
            }

            var maxWidth = (int)(ImGui.GetWindowWidth() * (1 - Config.HorizontalMargin)) - 2 * Config.BorderWidth;

            var plan = _plugin.GetFdt(Config.FontIndex)
                .BuildLayout(_text)
                .WithMaxWidth(maxWidth)
                .WithHorizontalAlignment((Fdt.LayoutBuilder.HorizontalAlignment)Config.HorizontalAlignment)
                .Build();

            var xd = (int)(ImGui.GetWindowWidth() * Config.HorizontalMargin / 2);
            var yd = (int)(rc.Height * Config.VerticalPosition);
            if (Config.VerticalSnap == Configuration.VerticalSnapType.Middle) {
                yd -= plan.Height / 2;
            } else if (Config.VerticalSnap == Configuration.VerticalSnapType.Bottom) {
                yd -= plan.Height;
            }
            yd = Math.Max(0, Math.Min(rc.Height - plan.Height, yd));

            for (var x = -Config.BorderWidth; x <= Config.BorderWidth; x++) {
                for (var y = -Config.BorderWidth; y <= Config.BorderWidth; y++) {
                    if (x == 0 && y == 0)
                        continue;
                    foreach (var p in plan.Elements) {
                        if (p.IsControl)
                            continue;
                        var glyph = p.Glyph;
                        ImGui.SetCursorPosX(xd + x + p.X);
                        ImGui.SetCursorPosY(yd + y + p.Y);
                        var tex = _plugin.GetFontTexture(glyph.TextureIndex);
                        ImGui.Image(tex.ImGuiHandle,
                            new Vector2(glyph.BoundingWidth, glyph.BoundingHeight),
                            new Vector2(1.0f * glyph.TextureOffsetX / tex.Width, 1.0f * glyph.TextureOffsetY / tex.Height),
                            new Vector2(1.0f * (glyph.TextureOffsetX + glyph.BoundingWidth) / tex.Width, 1.0f * (glyph.TextureOffsetY + glyph.BoundingHeight) / tex.Height),
                            Config.ColorBorder);
                    }
                }
            }
            foreach (var p in plan.Elements) {
                if (p.IsControl)
                    continue;
                var glyph = p.Glyph;
                ImGui.SetCursorPosX(xd + p.X);
                ImGui.SetCursorPosY(yd + p.Y);
                var tex = _plugin.GetFontTexture(glyph.TextureIndex);
                ImGui.Image(tex.ImGuiHandle,
                    new Vector2(glyph.BoundingWidth, glyph.BoundingHeight),
                    new Vector2(1.0f * glyph.TextureOffsetX / tex.Width, 1.0f * glyph.TextureOffsetY / tex.Height),
                    new Vector2(1.0f * (glyph.TextureOffsetX + glyph.BoundingWidth) / tex.Width, 1.0f * (glyph.TextureOffsetY + glyph.BoundingHeight) / tex.Height),
                    Config.ColorFill);

            }
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
                ImGui.Combo("Horizontal Alignment", ref Config.HorizontalAlignmentInt, Constants.HorizontalAlignmentNames, Constants.HorizontalAlignmentNames.Length);
                ImGui.Combo("Vertical Snap", ref Config.VerticalSnapInt, Constants.VerticalSnapNames, Constants.VerticalSnapNames.Length);
                ImGui.SliderFloat("Horizontal Margin", ref Config.HorizontalMargin, 0.0f, 1f);
                ImGui.SliderFloat("Vertical Position", ref Config.VerticalPosition, 0.0f, 1f);
                ImGui.Combo("Font", ref Config.FontIndex, Constants.FontNames, Constants.FontNames.Length);
                ImGui.SliderInt("Border Width", ref Config.BorderWidth, 0, 3);

                Config.ColorFill = ImGuiComponents.ColorPickerWithPalette(1, "Fill Color", Config.ColorFill, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Fill Color");
                ImGui.SameLine();
                Config.ColorBorder = ImGuiComponents.ColorPickerWithPalette(2, "Border Color", Config.ColorBorder, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Border Color");
            } catch (Exception ex) {
                PluginLog.Error(ex, "?");
            }
        }
    }
}

