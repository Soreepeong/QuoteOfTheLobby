using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Internal;
using Dalamud.Logging;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;

namespace QuoteOfTheLobby
{
    public class TextLayer : IDisposable {
        private readonly GameResourceReader _reader;
        private readonly UiBuilder _uiBuilder;

        public readonly Configuration.TextLayerConfiguration Config;
        public bool Deleted { get; internal set; }

        private class TextureLayer {
            public readonly SeString Text;
            public IDalamudTextureWrap Texture;
            public Thread? BuilderThread;
            public TextTextureGenerator.Result? BuildResult;
            public CancellationTokenSource? CancellationToken;
            public long FadeInDuration;
            public long FadeInAt;
            public long FadeOutDuration;
            public long FadeOutAt;

            public TextureLayer(SeString text) {
                Text = text;
            }

            public void Cancel() {
                BuilderThread?.Join();
                CancellationToken?.Cancel();
                BuilderThread = null;
                BuildResult = null;
                CancellationToken = null;
            }

            public void ClearTexture() {
                Cancel();
                Texture?.Dispose();
                Texture = null;
            }
        }

        private bool _needRefresh = true;
        private List<TextureLayer> _textures = new();
        private Vector2 _lastWindowSize = new();
        private long _lastRefresh = 0;

        public TextLayer(GameResourceReader reader, UiBuilder uiBuilder, Configuration.TextLayerConfiguration? config = null) {
            _reader = reader;
            _uiBuilder = uiBuilder;
            if (config == null)
                config = new Configuration.TextLayerConfiguration();
            Config = config!;
        }

        public void Dispose() {
            RefreshText(true);
        }

        public void RefreshText(bool noFade = false) {
            if (noFade) {
                foreach (var x in _textures) {
                    x.CancellationToken?.Cancel();
                    x.Texture?.Dispose();
                }
                _textures.Clear();
            }
            _needRefresh = true;
        }

        private void ResolveText() {
            if (!_needRefresh && (Config.CycleInterval == 0 || Config.Type != Configuration.TextLayerType.RandomDialogue || (Config.CycleInterval > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastRefresh < 1000 * Config.CycleInterval)))
                return;

            _needRefresh = false;
            _lastRefresh = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            TextureLayer newTextureLayer;
            if (Config.Type == Configuration.TextLayerType.RandomDialogue) {
                newTextureLayer = new TextureLayer(_reader.GetRandomQuote(Config.Language));

            } else if (Config.Type == Configuration.TextLayerType.DefaultDatacenter) {
                try {
                    var cfg = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\My Games\\FINAL FANTASY XIV - A Realm Reborn\\FFXIV.cfg");
                    uint worldId = uint.Parse(Regex.Match(cfg, @"WorldId\s+([0-9]+)").Groups[1].Value);
                    var worldName = _reader.GetDatacenterNameFromWorldId(worldId)!;
                    if (_textures.Count > 0 && _textures[^1].Text.TextValue == worldName.TextValue)
                        return;

                    newTextureLayer = new TextureLayer(worldName);
                } catch (Exception e) {
                    PluginLog.Warning(e, "Failed to read last world ID");
                    return;
                }

            } else if (Config.Type == Configuration.TextLayerType.StickyNote) {
                var builder = new SeStringBuilder();

                bool italic = false, bold = false;
                var text = Config.StickyNote.Replace("\r\n", "\n");
                for (int i = 0, i_ = text.Length; i < i_; ++i) {
                    int remaining = text.Length - i - 1;
                    if (text[i] == '\\' && remaining > 0) {
                        if (text[i + 1] == '\n')
                            builder.Add(new NewLinePayload());
                        else
                            builder.AddText(text.Substring(i + 1, 1));
                        i++;
                    } else if (text[i] == '*') {
                        if (remaining > 0 && text[i + 1] == '*') {
                            bold = !bold;
                            if (bold)
                                builder.Add(Fdt.InternalBoldPayload.BoldOn);
                            else
                                builder.Add(Fdt.InternalBoldPayload.BoldOff);
                            i++;
                        } else {
                            italic = !italic;
                            if (italic)
                                builder.AddItalicsOn();
                            else
                                builder.AddItalicsOff();
                        }
                    } else if (text[i] == '\n') {
                        builder.Add(new NewLinePayload());
                    } else {
                        builder.AddText(text.Substring(i, 1));
                    }
                }
                newTextureLayer = new TextureLayer(builder.Build());
            } else {
                PluginLog.Debug("Invalid Config.Type specified");
                return;
            }

            _textures.Add(newTextureLayer);
            if (_textures.Count >= 2) {
                for (var i = 0; i < _textures.Count - 1; ++i) {
                    _textures[i].Cancel();
                    if (_textures[i].Texture == null) {
                        _textures.RemoveAt(i);
                        --i;
                        continue;
                    }
                }
            }
        }

        public void DrawText(Native.RECT rc) {
            if (_lastWindowSize.X != rc.Width || _lastWindowSize.Y != rc.Height) {
                foreach (var t in _textures)
                    t.ClearTexture();
                _lastWindowSize.X = rc.Width;
                _lastWindowSize.Y = rc.Height;
            }

            ResolveText();

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var t in _textures) {
                if (t.Texture != null)
                    continue;

                if (t.BuilderThread == null) {
                    var r = new TextTextureGenerator(_reader.FontTextureData, _reader.Fdts[Config.FontIndex])
                        .WithText(t.Text)
                        .WithItalicness(Config.ItalicWidth)
                        .WithBoldness(Config.BoldWeight)
                        .WithBorderWidth(Config.BorderWidth)
                        .WithBorderStrength(Config.BorderStrength)
                        .WithMaxWidth((int)(_lastWindowSize.X * (1 - Config.HorizontalMargin) / Config.Zoom))
                        .WithBorderColor(Config.ColorBorder)
                        .WithFillColor(Config.ColorFill)
                        .WithHorizontalAlignment(Config.HorizontalAlignment)
                        .Build();
                    t.BuildResult = r.Item1;
                    t.BuilderThread = r.Item2;
                    t.CancellationToken = r.Item3;
                }

                var res = t.BuildResult!;
                if (res.Buffer == null)
                    continue;

                if (t.FadeOutAt > 0)  // abandoned
                    continue;

                t.Texture = _uiBuilder.LoadImageRaw(res.Buffer!, res.Width, res.Height, 4);
                t.Cancel();
                if (_textures.Count > 1) {
                    t.FadeInDuration = Config.Type == Configuration.TextLayerType.RandomDialogue ? (long)(1000 * Config.FadeDuration) : 0;
                    t.FadeInAt = now + t.FadeInDuration;
                }
            }

            for (var i = 0; i < _textures.Count; ++i) {
                var t = _textures[i];
                if (t.Texture == null)
                    continue;

                if (i < _textures.Count - 1 && _textures[_textures.Count - 1].Texture != null) {
                    if (t.FadeOutAt == 0) {
                        t.FadeOutDuration = Config.Type == Configuration.TextLayerType.RandomDialogue ? (long)(1000 * Config.FadeDuration) : 0;
                        t.FadeOutAt = Math.Max(_textures[i].FadeInAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) + _textures[i].FadeOutDuration;
                    }
                }

                float opacity;
                if (t.FadeOutAt > 0 && t.FadeOutAt <= now) {
                    t.ClearTexture();
                    _textures.RemoveAt(i);
                    --i;
                    continue;
                } else if (t.FadeOutAt > 0 && t.FadeOutAt - t.FadeOutDuration <= now && t.FadeInAt <= now)
                    opacity = (float)Math.Pow(1f * (t.FadeOutAt - now) / t.FadeOutDuration, 3);
                else if (t.FadeInAt <= now)
                    opacity = 1f;
                else if (t.FadeInAt - t.FadeInDuration <= now)
                    opacity = 1f - (float)Math.Pow(1f * (t.FadeInAt - now) / t.FadeInDuration, 3);
                else
                    continue;

                var xd = rc.Width * Config.HorizontalMargin / 2;
                if (Config.HorizontalAlignment == Fdt.LayoutBuilder.HorizontalAlignment.Center)
                    xd = (rc.Width - t.Texture.Width * Config.Zoom) / 2;
                else if (Config.HorizontalAlignment == Fdt.LayoutBuilder.HorizontalAlignment.Right)
                    xd = rc.Width - t.Texture.Width * Config.Zoom - xd;

                var yd = rc.Height * Config.VerticalPosition;
                if (Config.VerticalSnap == Configuration.VerticalSnapType.Middle)
                    yd -= t.Texture.Height * Config.Zoom / 2;
                else if (Config.VerticalSnap == Configuration.VerticalSnapType.Bottom)
                    yd -= t.Texture.Height * Config.Zoom;
                yd = Math.Max(0, Math.Min(rc.Height - t.Texture.Height * Config.Zoom, yd));

                if (Config.ColorBackground.W > 0) {
                    ImGui.GetForegroundDrawList().AddRectFilled(
                        new Vector2(
                            (int)(rc.Left + xd - Config.BackgroundPadding),
                            (int)(rc.Top + yd - Config.BackgroundPadding)
                        ),
                        new Vector2(
                            (int)(rc.Left + xd + t.Texture.Width * Config.Zoom + Config.BackgroundPadding),
                            (int)(rc.Top + yd + t.Texture.Height * Config.Zoom + Config.BackgroundPadding)
                        ),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(
                            Config.ColorBackground.X,
                            Config.ColorBackground.Y,
                            Config.ColorBackground.Z,
                            Config.ColorBackground.W * opacity
                        ))
                    );
                }

                ImGui.GetForegroundDrawList().AddImage(t.Texture.ImGuiHandle,
                    new Vector2((int)(rc.Left + xd), (int)(rc.Top + yd)),
                    new Vector2((int)(rc.Left + xd + t.Texture.Width * Config.Zoom), (int)(rc.Top + yd + t.Texture.Height * Config.Zoom)),
                    Vector2.Zero, Vector2.One, (uint)(0xFF * opacity) << 24 | 0xFFFFFF);
            }
        }

        public void DrawConfigElements() {
            try {
                if (ImGui.Button("Shuffle"))
                    RefreshText();
                ImGui.SameLine();
                if (ImGui.Button("Remove"))
                    Deleted = true;

                ImGui.InputText("Name", ref Config.Name, 64);
                ImGui.InputText("Visible With", ref Config.VisibleWith, 64);
                ImGui.SameLine();
                ImGuiComponents.HelpMarker(
                    "Set to blank to display at all times.\n" +
                    "You can enter multiple values separated by comma(,).\n" +
                    "Refer to /xldev > Dalamud > Open Data Window > Addon Inspector to decide which Addon(in-game window) to show text with.");
                ImGui.Combo("Type", ref Config.TypeVal, Constants.TextLayerTypeNames, Constants.TextLayerTypeNames.Length);
                switch (Config.Type) {
                    case Configuration.TextLayerType.RandomDialogue: {
                        _needRefresh |= ImGui.Combo("Language", ref Config.LanguageVal, Constants.LanguageNames, Constants.LanguageNames.Length);
                        ImGui.SliderFloat("Crossfade Duration", ref Config.FadeDuration, 0f, 5f);
                        ImGui.SliderInt("Cycle Interval", ref Config.CycleInterval, 0, 30);
                        break;
                    }
                    case Configuration.TextLayerType.StickyNote: {
                        ImGui.Text("Text");
                        ImGui.SameLine();
                        ImGuiComponents.HelpMarker(
                            "Use ** to toggle bold.\n" +
                            "Use * to toggle italics.\n" +
                            "Use \\* to show *.\n" +
                            "Use \\\\ to show *.");
                        _needRefresh |= ImGui.InputTextMultiline("Text", ref Config.StickyNote, 32768, new Vector2(ImGui.GetWindowWidth(), ImGui.GetTextLineHeight() * 4));
                        break;
                    }
                }

                _needRefresh |= ImGui.Combo("Horizontal Alignment", ref Config.HorizontalAlignmentInt, Constants.HorizontalAlignmentNames, Constants.HorizontalAlignmentNames.Length);
                ImGui.Combo("Vertical Snap", ref Config.VerticalSnapInt, Constants.VerticalSnapNames, Constants.VerticalSnapNames.Length);
                _needRefresh |= ImGui.SliderFloat("Horizontal Margin", ref Config.HorizontalMargin, 0f, 1f);
                ImGui.SliderFloat("Vertical Position", ref Config.VerticalPosition, 0f, 1f);
                ImGui.SliderInt("Background Padding", ref Config.BackgroundPadding, 0, 16);
                _needRefresh |= ImGui.SliderFloat("Zoom", ref Config.Zoom, 0.2f, 4f);
                ImGui.SameLine();
                if (ImGui.Button("Reset"))
                    Config.Zoom = 1f;
                _needRefresh |= ImGui.Combo("Font", ref Config.FontIndex, Constants.FontNames, Constants.FontNames.Length);
                _needRefresh |= ImGui.SliderFloat("Bold Weight", ref Config.BoldWeight, 0f, 8f);
                _needRefresh |= ImGui.SliderFloat("Italic Width", ref Config.ItalicWidth, -8f, 8f);
                _needRefresh |= ImGui.SliderFloat("Border Width", ref Config.BorderWidth, 0f, 8f);
                _needRefresh |= ImGui.SliderFloat("Border Strength", ref Config.BorderStrength, -4f, 4f);

                _needRefresh |= ColorPickerWithPalette(1, "Fill Color", ref Config.ColorFill, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Fill Color");
                ImGui.SameLine();
                _needRefresh |= ColorPickerWithPalette(2, "Border Color", ref Config.ColorBorder, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Border Color");
                ImGui.SameLine();
                ColorPickerWithPalette(3, "Background Color", ref Config.ColorBackground, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Background Color");
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

