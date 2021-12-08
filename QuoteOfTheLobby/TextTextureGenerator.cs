using Dalamud.Game.Text.SeStringHandling;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace QuoteOfTheLobby {
    public class TextTextureGenerator {
        private readonly List<byte[]> _textureData;
        private readonly Fdt _fdt;

        private SeString _text = SeString.Empty;
        public Fdt.LayoutBuilder.HorizontalAlignment _horizontalAlignment;
        private float _borderWidth;
        private float _borderStrength;
        private int _maxWidth;
        private Vector4 _borderColor;
        private Vector4 _fillColor;

        public TextTextureGenerator(List<byte[]> textureData, Fdt fdt) {
            _textureData = textureData;
            _fdt = fdt;
        }

        public TextTextureGenerator WithText(SeString text) {
            _text = text;
            return this;
        }

        public TextTextureGenerator WithBorderWidth(float borderWidth) {
            _borderWidth = borderWidth;
            return this;
        }

        public TextTextureGenerator WithBorderStrength(float borderStrength) {
            _borderStrength = borderStrength;
            return this;
        }

        public TextTextureGenerator WithMaxWidth(int maxWidth) {
            _maxWidth = maxWidth;
            return this;
        }

        public TextTextureGenerator WithBorderColor(Vector4 borderColor) {
            _borderColor = borderColor;
            return this;
        }

        public TextTextureGenerator WithFillColor(Vector4 fillColor) {
            _fillColor = fillColor;
            return this;
        }

        public TextTextureGenerator WithHorizontalAlignment(Fdt.LayoutBuilder.HorizontalAlignment horizontalAlignment) {
            _horizontalAlignment = horizontalAlignment;
            return this;
        }

        public class Result {
            public byte[]? Buffer { get; internal set; }
            public int Width { get; internal set; }
            public int Height { get; internal set; }
        }

        public Tuple<Result, Thread, CancellationTokenSource> Build() {
            var ctSource = new CancellationTokenSource();

            var r = new Result();
            var thr = new Thread(() => {
                var pad = (int)Math.Ceiling(_borderWidth);

                var plan = _fdt
                    .BuildLayout(_text)
                    .WithMaxWidth(_maxWidth - 2 * pad)
                    .WithHorizontalAlignment(_horizontalAlignment)
                    .Build();

                var width = plan.Width + 2 * pad;
                var height = plan.Height + 2 * pad;

                var distanceMap = new float[2 * pad + 1, 2 * pad + 1];
                var strength = Math.Pow(2, -_borderStrength);
                for (var x = 0; x <= 2 * pad; x++) {
                    for (var y = 0; y <= 2 * pad; y++) {
                        distanceMap[x, y] = (float)Math.Pow(1 - Math.Min(1, Math.Sqrt(Math.Pow(x - pad, 2) + Math.Pow(y - pad, 2)) / _borderWidth), strength);
                    }
                }
                if (ctSource.Token.IsCancellationRequested)
                    return;

                var fillBuffer = new byte[width * height * 4];
                foreach (var p in plan.Elements) {
                    if (p.IsControl || p.IsSpace)
                        continue;
                    var sourceBuffer = _textureData[p.Glyph.TextureIndex / 4];
                    var sourceBufferDelta = Constants.TextureChannelOrder[p.Glyph.TextureIndex % 4];
                    for (var i = 0; i < p.Glyph.BoundingWidth; i++) {
                        for (var j = 0; j < p.Glyph.BoundingHeight; j++) {
                            var pos = 4 * ((i + pad + p.X - plan.Left) + width * (j + pad + p.Y));
                            fillBuffer[pos + 3] = Math.Max(fillBuffer[pos + 3],
                                sourceBuffer[sourceBufferDelta + 4 * (
                                    (p.Glyph.TextureOffsetX + i) +
                                    (p.Glyph.TextureOffsetY + j) * _fdt.Fthd.TextureWidth
                                    )]);
                        }
                    }
                }
                if (ctSource.Token.IsCancellationRequested)
                    return;

                if (pad > 0) {
                    var borderBuffer = new byte[width * height * 4];
                    foreach (var p in plan.Elements) {
                        if (p.IsControl || p.IsSpace)
                            continue;
                        var sourceBuffer = _textureData[p.Glyph.TextureIndex / 4];
                        var sourceBufferDelta = Constants.TextureChannelOrder[p.Glyph.TextureIndex % 4];
                        for (var x = 0; x <= 2 * pad; x++) {
                            for (var y = 0; y <= 2 * pad; y++) {
                                for (var i = 0; i < p.Glyph.BoundingWidth; i++) {
                                    for (var j = 0; j < p.Glyph.BoundingHeight; j++) {
                                        var pos = 4 * ((i + x + p.X - plan.Left) + width * (j + y + p.Y));
                                        borderBuffer[pos + 3] = Math.Max(borderBuffer[pos + 3],
                                            (byte)(distanceMap[x, y] *
                                            sourceBuffer[sourceBufferDelta + 4 * (
                                                (p.Glyph.TextureOffsetX + i) +
                                                (p.Glyph.TextureOffsetY + j) * _fdt.Fthd.TextureWidth
                                                )]));
                                    }
                                }
                            }
                            if (ctSource.Token.IsCancellationRequested)
                                return;
                        }
                    }

                    for (var i = 0; i < borderBuffer.Length; i += 4) {
                        float fillAlpha = fillBuffer[i + 3] / 255f;
                        float borderAlpha = Math.Min(borderBuffer[i + 3], (byte)(255 - fillBuffer[i + 3])) / 255f;
                        fillBuffer[i + 0] = (byte)(255 * (_fillColor.X * fillAlpha + _borderColor.X * (1 - fillAlpha)));
                        fillBuffer[i + 1] = (byte)(255 * (_fillColor.Y * fillAlpha + _borderColor.Y * (1 - fillAlpha)));
                        fillBuffer[i + 2] = (byte)(255 * (_fillColor.Z * fillAlpha + _borderColor.Z * (1 - fillAlpha)));
                        fillBuffer[i + 3] = (byte)(255 * (fillAlpha * _fillColor.W + (1 - fillAlpha * _fillColor.W) * borderAlpha * _borderColor.W));
                    }
                } else {
                    for (var i = 0; i < fillBuffer.Length; i += 4) {
                        fillBuffer[i + 0] = (byte)(255 * _fillColor.X);
                        fillBuffer[i + 1] = (byte)(255 * _fillColor.Y);
                        fillBuffer[i + 2] = (byte)(255 * _fillColor.Z);
                        fillBuffer[i + 3] = (byte)(fillBuffer[i + 3] * _fillColor.W);
                    }
                }

                r.Buffer = fillBuffer;
                r.Width = width;
                r.Height = height;
            });
            thr.Start();

            return new Tuple<Result, Thread, CancellationTokenSource>(r, thr, ctSource);
        }
    }
}
