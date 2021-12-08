using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Logging;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;

namespace QuoteOfTheLobby {
    public class TextLayer : IDisposable {
        private readonly GameResourceReader _reader;
        private readonly UiBuilder _uiBuilder;

        public readonly Configuration.TextLayerConfiguration Config;
        public bool Deleted { get; internal set; }

        private class TextureLayer {
            public readonly SeString Text;
            public ImGuiScene.TextureWrap? Texture;
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
            if (!_needRefresh && (Config.CycleInterval == 0 || (Config.CycleInterval > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastRefresh < 1000 * Config.CycleInterval)))
                return;

            _needRefresh = false;
            _lastRefresh = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (Config.Type == Configuration.TextLayerType.RandomDialogue) {
                _textures.Add(new TextureLayer(_reader.GetRandomQuote(Config.Language)));
                if (Config.FadeDuration > 0 && _textures.Count >= 2) {
                    for (var i = 0; i < _textures.Count - 1; ++i) {
                        _textures[i].Cancel();
                        if (_textures[i].Texture == null || Config.FadeDuration <= 0) {
                            _textures.RemoveAt(i);
                            --i;
                            continue;
                        }
                    }
                }
            } else if (Config.Type == Configuration.TextLayerType.DefaultDatacenter) {
                try {
                    var cfg = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\My Games\\FINAL FANTASY XIV - A Realm Reborn\\FFXIV.cfg");
                    uint worldId = uint.Parse(Regex.Match(cfg, @"WorldId\s+([0-9]+)").Groups[1].Value);
                    var worldName = _reader.GetDatacenterNameFromWorldId(worldId)!;
                    if (_textures.Count == 0 || _textures[0].Text.TextValue != worldName.TextValue) {
                        _textures.Clear();
                        _textures.Add(new TextureLayer(worldName));
                    }
                } catch (Exception e) {
                    PluginLog.Warning(e, "Failed to read last world ID");
                }
            } else if (Config.Type == Configuration.TextLayerType.FixedText) {
                _textures.Clear();
                _textures.Add(new TextureLayer(new SeStringBuilder().AddText(Config.FixedText).Build()));
            }
        }

        public void DrawText(Native.RECT rc) {
            if (_lastWindowSize != ImGui.GetWindowSize())
                foreach (var t in _textures)
                    t.ClearTexture();
            _lastWindowSize = ImGui.GetWindowSize();

            ResolveText();

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var t in _textures) {
                if (t.Texture != null)
                    continue;

                if (t.BuilderThread == null) {
                    var r = new TextTextureGenerator(_reader.FontTextureData, _reader.Fdts[Config.FontIndex])
                        .WithText(t.Text)
                        .WithBorderWidth(Config.BorderWidth)
                        .WithBorderStrength(Config.BorderStrength)
                        .WithMaxWidth((int)(_lastWindowSize.X * (1 - Config.HorizontalMargin)))
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
                if (Config.FadeDuration > 0 && _textures.Count > 1) {
                    t.FadeInDuration = (long)(1000 * Config.FadeDuration);
                    t.FadeInAt = now + t.FadeInDuration;
                }
            }

            for (var i = 0; i < _textures.Count; ++i) {
                var t = _textures[i];
                if (t.Texture == null)
                    continue;

                if (i < _textures.Count - 1 && _textures[_textures.Count - 1].Texture != null) {
                    if (t.FadeOutAt == 0) {
                        t.FadeOutDuration = (long)(1000 * Config.FadeDuration);
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

                int xd = (int)(rc.Width * Config.HorizontalMargin / 2);
                if (Config.HorizontalAlignment == Fdt.LayoutBuilder.HorizontalAlignment.Center)
                    xd = (rc.Width - t.Texture.Width) / 2;
                else if (Config.HorizontalAlignment == Fdt.LayoutBuilder.HorizontalAlignment.Right)
                    xd = rc.Width - t.Texture.Width - xd;

                var yd = (int)(rc.Height * Config.VerticalPosition);
                if (Config.VerticalSnap == Configuration.VerticalSnapType.Middle)
                    yd -= t.Texture.Height / 2;
                else if (Config.VerticalSnap == Configuration.VerticalSnapType.Bottom)
                    yd -= t.Texture.Height;
                yd = Math.Max(0, Math.Min(rc.Height - t.Texture.Height, yd));

                if (Config.ColorBackground.W > 0) {
                    ImGui.GetWindowDrawList().AddRectFilled(
                        new Vector2(
                            rc.Left + xd - Config.BackgroundPadding,
                            rc.Top + yd - Config.BackgroundPadding),
                        new Vector2(rc.Left + xd + t.Texture.Width + Config.BackgroundPadding,
                            rc.Top + yd + t.Texture.Height + Config.BackgroundPadding),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(Config.ColorBackground.X, Config.ColorBackground.Y, Config.ColorBackground.Z, Config.ColorBackground.W * opacity)));
                }

                ImGui.GetWindowDrawList().AddImage(t.Texture.ImGuiHandle,
                    new Vector2(rc.Left + xd, rc.Top + yd),
                    new Vector2(rc.Left + xd + t.Texture.Width, rc.Top + yd + t.Texture.Height),
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
                    case Configuration.TextLayerType.FixedText: {
                        _needRefresh |= ImGui.InputTextMultiline("Text", ref Config.FixedText, 32768, new Vector2(ImGui.GetWindowWidth(), ImGui.GetTextLineHeight() * 4));
                        break;
                    }
                }

                bool relayoutRequired = false;
                relayoutRequired |= ImGui.Combo("Horizontal Alignment", ref Config.HorizontalAlignmentInt, Constants.HorizontalAlignmentNames, Constants.HorizontalAlignmentNames.Length);
                ImGui.Combo("Vertical Snap", ref Config.VerticalSnapInt, Constants.VerticalSnapNames, Constants.VerticalSnapNames.Length);
                relayoutRequired |= ImGui.SliderFloat("Horizontal Margin", ref Config.HorizontalMargin, 0.0f, 1f);
                ImGui.SliderFloat("Vertical Position", ref Config.VerticalPosition, 0.0f, 1f);
                ImGui.SliderInt("Background Padding", ref Config.BackgroundPadding, 0, 16);
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
                ColorPickerWithPalette(3, "Background Color", ref Config.ColorBackground, ImGuiColorEditFlags.AlphaBar);
                ImGui.SameLine();
                ImGui.Text("Background Color");

                if (relayoutRequired || _needRefresh) {
                    foreach (var t in _textures)
                        t.ClearTexture();
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

