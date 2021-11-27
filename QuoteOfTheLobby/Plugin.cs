using Dalamud;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Components;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Utility;
using ImGuiNET;
using Lumina.Data.Files;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace QuoteOfTheLobby {
    public sealed class Plugin : IDalamudPlugin {
        private static readonly string HideNamedUiElementSignature = "40 57 48 83 EC 20 48 8B F9 48 8B 89 C8 00 00 00 48 85 C9 0F ?? ?? ?? ?? ?? 8B 87 B0 01 00 00 C1 E8 07 A8 01";
        private static readonly string ShowNamedUiElementSignature = "40 53 48 83 EC 40 48 8B 91 C8 00 00 00 48 8B D9 48 85 D2";

        public string Name => "Quote of the Lobby";

        public readonly DalamudPluginInterface PluginInterface;
        public readonly CommandManager CommandManager;
        public readonly DataManager DataManager;
        public readonly ClientState ClientState;
        public readonly SigScanner SigScanner;

        private readonly Configuration _config;

        private delegate IntPtr HideShowNamedUiElementDelegate(IntPtr pThis);
        private readonly Hook<HideShowNamedUiElementDelegate> _hideHook, _showHook;

        private IntPtr _gameWindowHwnd = IntPtr.Zero;

        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.World> _world;
        private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.GeneratedSheets.WorldDCGroupType> _worldDcGroupType;
        private readonly List<Fdt> _fdts = new();
        private readonly List<byte[]> _fontTextureData = new();
        private readonly List<ImGuiScene.TextureWrap> _fontTextures = new();

        private readonly Dictionary<Guid, TextLayer> _layers = new();
        private readonly Dictionary<string, bool> _gameLayerVisibility = new();
        private Dictionary<ClientLanguage, RandomQuoteReader> _randomQuoteReaders = new();

        private readonly List<IDisposable> _disposableList = new();

        private bool _configWindowVisible = false;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            [RequiredVersion("1.0")] DataManager dataManager,
            [RequiredVersion("1.0")] ClientState clientState,
            [RequiredVersion("1.0")] SigScanner sigScanner) {
            try {
                PluginInterface = pluginInterface;
                CommandManager = commandManager;
                DataManager = dataManager;
                ClientState = clientState;
                SigScanner = sigScanner;

                _config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
                _config.Initialize(PluginInterface);

                _world = DataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.World>()!;
                _worldDcGroupType = DataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.WorldDCGroupType>()!;

                foreach (var fontName in Constants.FontNames)
                    _fdts.Add(new Fdt(DataManager.GetFile($"common/font/{fontName}.fdt")!.Data));
                byte[] buf = new byte[1024 * 1024 * 4];
                foreach (var i in Enumerable.Range(1, 100)) {
                    var tf = DataManager.GameData.GetFile<TexFile>($"common/font/font{i}.tex");
                    if (tf == null)
                        break;

                    PluginLog.Information($"Read common/font/font{i}.tex ({tf.Header.Width} x {tf.Header.Height})");
                    if (tf.ImageData.Length != tf.Header.Width * tf.Header.Height * 4)
                        throw new Exception("TexE");

                    _fontTextureData.Add(tf.ImageData);
                    if (buf.Length < tf.Header.Width * tf.Header.Height * 4)
                        buf = new byte[tf.Header.Width * tf.Header.Height * 4];
                    foreach (var j in Constants.TextureChannelOrder) {
                        for (int k = 0, k_ = tf.Header.Width * tf.Header.Height * 4, s = j; k < k_; s += 4) {
                            buf[k++] = tf.ImageData[s];
                            buf[k++] = tf.ImageData[s];
                            buf[k++] = tf.ImageData[s];
                            buf[k++] = tf.ImageData[s];
                        }
                        _fontTextures.Add(PluginInterface.UiBuilder.LoadImageRaw(buf, tf.Header.Width, tf.Header.Height, 4));
                    }
                }
                _disposableList.AddRange(_fontTextures);

                if (_config.TextLayers.Count == 0) {
                    _config.TextLayers.Add(new Configuration.TextLayerConfiguration() {
                        Name = "Random Quote",
                        Type = Configuration.TextLayerType.RandomDialogue,
                        VerticalPosition = 0.1f,
                    });
                    _config.TextLayers.Add(new Configuration.TextLayerConfiguration() {
                        Name = "Default Datacenter",
                        Type = Configuration.TextLayerType.DefaultDatacenter,
                        VerticalPosition = 0.95f,
                        FontIndex = 11,
                    });
                }
                foreach (var t in _config.TextLayers)
                    _layers[t.Guid] = new TextLayer(this, t);

                PluginInterface.UiBuilder.Draw += DrawUI;
                PluginInterface.UiBuilder.OpenConfigUi += () => { _configWindowVisible = !_configWindowVisible; };

                var hideNamedUiElementAddress = SigScanner.ScanText(HideNamedUiElementSignature);
                var showNamedUiElementAddress = SigScanner.ScanText(ShowNamedUiElementSignature);

                _disposableList.Add(_hideHook = new Hook<HideShowNamedUiElementDelegate>(hideNamedUiElementAddress, this.HideNamedUiElementDetour));
                _disposableList.Add(_showHook = new Hook<HideShowNamedUiElementDelegate>(showNamedUiElementAddress, this.ShowNamedUiElementDetour));
                _hideHook.Enable();
                _showHook.Enable();

            } catch {
                Dispose();
                throw;
            }
        }

        private void Save() {
            if (_config == null)
                return;

            _config.TextLayers.Clear();
            foreach (var e in _layers.Values)
                _config.TextLayers.Add(e.Config);
            _config.Save();
        }

        public void Dispose() {
            Save();
            foreach (var layer in _layers.Values)
                layer.Dispose();
            _layers.Clear();
            foreach (var item in _disposableList.AsEnumerable().Reverse()) {
                try {
                    item.Dispose();
                } catch (Exception e) {
                    PluginLog.Warning(e, "Dispose failure");
                }
            }
            _disposableList.Clear();
        }

        private unsafe IntPtr ShowNamedUiElementDetour(IntPtr pThis) {
            var res = _showHook.Original(pThis);
            var windowName = Marshal.PtrToStringUTF8(pThis + 8)!;
            PluginLog.Debug($"Show: {windowName} from {pThis}");
            _gameLayerVisibility[windowName] = true;
            return res;
        }

        private unsafe IntPtr HideNamedUiElementDetour(IntPtr pThis) {
            var res = _hideHook.Original(pThis);
            var windowName = Marshal.PtrToStringUTF8(pThis + 8)!;
            PluginLog.Debug($"Hide: {windowName} from {pThis}");
            _gameLayerVisibility[windowName] = false;
            return res;
        }

        public SeString? GetDatacenterNameFromWorldId(uint worldId) {
            return _world.GetRow(worldId)?.DataCenter.Value?.Name.ToDalamudString();
        }

        public RandomQuoteReader GetRandomQuoteReader(ClientLanguage? language) {
            if (language == null)
                language = ClientState.ClientLanguage;

            if (_randomQuoteReaders.ContainsKey((ClientLanguage)language))
                return _randomQuoteReaders[(ClientLanguage)language];
            else
                return _randomQuoteReaders[(ClientLanguage)language] = new RandomQuoteReader(DataManager, (ClientLanguage)language);
        }

        public Fdt GetFdt(int index) {
            return _fdts[index];
        }

        public byte[] GetFontTextureData(int index) {
            return _fontTextureData[index];
        }

        public ImGuiScene.TextureWrap GetFontTexture(int index) {
            return _fontTextures[index];
        }

        private void DrawUI() {
            if (_gameWindowHwnd == IntPtr.Zero) {
                while (IntPtr.Zero != (_gameWindowHwnd = Native.FindWindowEx(IntPtr.Zero, _gameWindowHwnd, "FFXIVGAME", null))) {
                    Native.GetWindowThreadProcessId(_gameWindowHwnd, out var pid);
                    if (pid == Environment.ProcessId && Native.IsWindowVisible(_gameWindowHwnd)) break;
                }

                if (_gameWindowHwnd == IntPtr.Zero)
                    return;
            }

            Native.GetClientRect(_gameWindowHwnd, out Native.RECT rc);
            Native.ClientToScreen(_gameWindowHwnd, ref rc);

            ImGui.SetNextWindowSize(new Vector2(400, 500), ImGuiCond.Once);

            if (_configWindowVisible) {
                if (ImGui.Begin("Quote of the Lobby Config", ref _configWindowVisible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                    try {
                        if (ImGui.Button("Add")) {
                            var layer = new TextLayer(this);
                            _layers[layer.Config.Guid] = layer;
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Shuffle All")) {
                            foreach (var e in _layers.Values)
                                e.RefreshText();
                        }
                        ImGui.BeginTabBar("Text Layers", ImGuiTabBarFlags.AutoSelectNewTabs);
                        try {
                            SortedSet<Guid>? dels = null;
                            foreach (var e in _layers.Values) {
                                if (!ImGui.BeginTabItem($"{e.Config.Name}###{e.Config.Guid}"))
                                    continue;
                                try {
                                    e.DrawConfigElements();
                                    if (e.Deleted) {
                                        if (dels == null)
                                            dels = new SortedSet<Guid>();
                                        dels.Add(e.Config.Guid);
                                    }
                                } finally {
                                    ImGui.EndTabItem();
                                }
                            }
                            if (dels != null) {
                                foreach (var d in dels) {
                                    _layers[d].Dispose();
                                    _layers.Remove(d);
                                }
                            }
                        } finally { ImGui.EndTabBar(); }
                    } finally { ImGui.End(); }
                else {
                    Save();
                    _configWindowVisible = false;
                }
            }

            ImGui.SetNextWindowPos(new Vector2(rc.Left, rc.Top));
            ImGui.SetNextWindowSize(new Vector2(rc.Width, rc.Height), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            try {
                bool v = true;
                if (ImGui.Begin("Quote of the Lobby Text", ref v, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs))
                    try {
                        foreach (var e in _layers.Values) {
                            if (e.Config.VisibleWith == "" || e.Config.VisibleWith.Split(",").Any(x => _gameLayerVisibility.GetValueOrDefault(x.Trim(), false)))
                                e.DrawText(rc);
                            else
                                e.RefreshText();
                        }
                    } catch (Exception ex) {
                        PluginLog.Error(ex, "?");
                    } finally {
                        ImGui.End();
                    }
            } finally {
                ImGui.PopStyleVar();
            }
        }

    }
}
